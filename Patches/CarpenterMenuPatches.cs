using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches for CarpenterMenu (Robin's building menu) to prevent instant close on Android.
    ///
    /// Root cause (identified in v2.7.3): The A button press from Robin's dialogue carries
    /// over as a mouse-down state into the newly opened CarpenterMenu. The menu's
    /// snapToDefaultClickableComponent() snaps to the cancel button (ID 107). When the
    /// A button is released, the call chain is:
    ///   releaseLeftClick() → OnReleaseCancelButton() → exitThisMenu()
    ///
    /// The four standard input methods (receiveLeftClick, receiveKeyPress, receiveGamePadButton)
    /// are NEVER called during this close. Only leftClickHeld fires (from the held A state)
    /// and releaseLeftClick (when A is released).
    ///
    /// Fix: Block releaseLeftClick and leftClickHeld during a grace period after menu open.
    /// exitThisMenu is also blocked as a safety net.
    /// </summary>
    internal static class CarpenterMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Tick when the CarpenterMenu was opened. -1 means not tracking.</summary>
        private static int MenuOpenTick = -1;

        /// <summary>Number of ticks to block input after menu opens.</summary>
        private const int GracePeriodTicks = 20;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
            {
                Monitor.Log("Carpenter menu fix is disabled in config.", LogLevel.Trace);
                return;
            }

            try
            {
                // Block releaseLeftClick — this is the actual close trigger.
                // The A-button release from dialogue fires releaseLeftClick on the cancel button.
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.releaseLeftClick)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReleaseLeftClick_Prefix))
                );

                // Block leftClickHeld — fires every tick while A is still held from dialogue.
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.leftClickHeld)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(LeftClickHeld_Prefix))
                );

                // Safety net: block exitThisMenu on any CarpenterMenu during grace period.
                // Catches the close even if it comes through an unexpected path.
                harmony.Patch(
                    original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.exitThisMenu)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ExitThisMenu_Prefix))
                );

                Monitor.Log("CarpenterMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply CarpenterMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a CarpenterMenu opens.</summary>
        public static void OnMenuOpened()
        {
            MenuOpenTick = Game1.ticks;
            Monitor.Log($"CarpenterMenu opened at tick {MenuOpenTick}. Grace period: {GracePeriodTicks} ticks.", LogLevel.Debug);
        }

        /// <summary>Called from ModEntry.OnMenuChanged when the CarpenterMenu closes.</summary>
        public static void OnMenuClosed()
        {
            if (MenuOpenTick >= 0)
            {
                int duration = Game1.ticks - MenuOpenTick;
                Monitor.Log($"CarpenterMenu closed after {duration} ticks (grace was {GracePeriodTicks}).", LogLevel.Debug);
            }
            MenuOpenTick = -1;
        }

        /// <summary>Check if we're within the grace period after menu open.</summary>
        private static bool IsInGracePeriod()
        {
            return MenuOpenTick >= 0 && (Game1.ticks - MenuOpenTick) < GracePeriodTicks;
        }

        /// <summary>Prefix for CarpenterMenu.releaseLeftClick — blocks the A-button release from closing the menu.</summary>
        private static bool ReleaseLeftClick_Prefix(CarpenterMenu __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED releaseLeftClick at ({x},{y}) — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>Prefix for CarpenterMenu.leftClickHeld — blocks held-click state from dialogue A press.</summary>
        private static bool LeftClickHeld_Prefix(CarpenterMenu __instance, int x, int y)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED leftClickHeld — grace period", LogLevel.Trace);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for IClickableMenu.exitThisMenu — safety net for CarpenterMenu.
        /// Only acts on CarpenterMenu instances. Blocks exit during grace period regardless
        /// of which code path triggered it.
        /// </summary>
        private static bool ExitThisMenu_Prefix(IClickableMenu __instance, bool playSound)
        {
            if (__instance is not CarpenterMenu)
                return true;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED exitThisMenu — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }
    }
}
