using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

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

        /// <summary>Tick when Furniture.performToolAction last ran. Used for debounce.</summary>
        private static int LastFurnitureActionTick = -100;

        /// <summary>Cooldown ticks between furniture interactions (~500ms at 60fps).</summary>
        private const int FurnitureCooldownTicks = 30;

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

            // Furniture debounce — separate try/catch so carpenter patches still work if this fails
            // Diagnostic: probing multiple methods to find which path ControllerX uses
            if (ModEntry.Config.EnableFurnitureDebounce)
            {
                try
                {
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), nameof(Furniture.checkForAction)),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurnitureCheckForAction_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "canBeRemoved"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurnitureCanBeRemoved_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "performRemoveAction"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurniturePerformRemoveAction_Prefix))
                    );
                    harmony.Patch(
                        original: AccessTools.Method(typeof(Furniture), "placementAction"),
                        prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(FurniturePlacementAction_Prefix))
                    );
                    Monitor.Log("Furniture debounce + diagnostic patches applied successfully.", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Failed to apply furniture debounce patch: {ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                Monitor.Log("Furniture debounce is disabled in config.", LogLevel.Trace);
            }
        }

        /// <summary>Called from ModEntry.OnMenuChanged when a CarpenterMenu opens.</summary>
        public static void OnMenuOpened()
        {
            MenuOpenTick = Game1.ticks;
            if (ModEntry.Config.VerboseLogging)
                Monitor.Log($"CarpenterMenu opened at tick {MenuOpenTick}. Grace period: {GracePeriodTicks} ticks.", LogLevel.Debug);
        }

        /// <summary>Called from ModEntry.OnMenuChanged when the CarpenterMenu closes.</summary>
        public static void OnMenuClosed()
        {
            if (MenuOpenTick >= 0)
            {
                int duration = Game1.ticks - MenuOpenTick;
                if (ModEntry.Config.VerboseLogging)
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
                if (ModEntry.Config.VerboseLogging)
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
                if (ModEntry.Config.VerboseLogging)
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
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] BLOCKED exitThisMenu — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for Furniture.checkForAction — debounces Y button rapid-toggle.
        /// On Android, furniture pickup goes through checkForAction (not performToolAction).
        /// Blocks calls within 30 ticks (~500ms) of the last successful call.
        /// </summary>
        private static bool FurnitureCheckForAction_Prefix(Furniture __instance, bool justCheckingForActivity)
        {
            // justCheckingForActivity=true is a probe (e.g. cursor icon check), don't debounce those
            if (justCheckingForActivity)
                return true;

            int elapsed = Game1.ticks - LastFurnitureActionTick;
            if (elapsed < FurnitureCooldownTicks)
            {
                Monitor.Log($"[Furniture] BLOCKED checkForAction on '{__instance.Name}' — cooldown ({elapsed}/{FurnitureCooldownTicks} ticks)", LogLevel.Info);
                return false;
            }

            Monitor.Log($"[Furniture] ALLOWED checkForAction on '{__instance.Name}' at tick {Game1.ticks} (elapsed={elapsed})", LogLevel.Info);
            LastFurnitureActionTick = Game1.ticks;
            return true;
        }

        /// <summary>Diagnostic: log when Furniture.canBeRemoved is called.</summary>
        private static void FurnitureCanBeRemoved_Prefix(Furniture __instance)
        {
            Monitor.Log($"[Furniture] canBeRemoved HIT on '{__instance.Name}' at tick {Game1.ticks}", LogLevel.Info);
        }

        /// <summary>Diagnostic: log when Furniture.performRemoveAction is called.</summary>
        private static void FurniturePerformRemoveAction_Prefix(Furniture __instance)
        {
            Monitor.Log($"[Furniture] performRemoveAction HIT on '{__instance.Name}' at tick {Game1.ticks}", LogLevel.Info);
        }

        /// <summary>Diagnostic: log when Furniture.placementAction is called.</summary>
        private static void FurniturePlacementAction_Prefix(Furniture __instance)
        {
            Monitor.Log($"[Furniture] placementAction HIT on '{__instance.Name}' at tick {Game1.ticks}", LogLevel.Info);
        }
    }
}
