using System;
using System.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches for CarpenterMenu (Robin's building menu) to prevent instant close on Android.
    ///
    /// Problem: Pressing A to select "Construct Farm Buildings" in dialogue also fires input
    /// into the newly opened CarpenterMenu, which instantly closes it.
    ///
    /// v2.7.2 attempt: Patched receiveLeftClick, leftClickHeld, receiveKeyPress, and
    /// receiveGamePadButton with a 20-tick grace period. FAILED — only leftClickHeld ever
    /// fired (and was blocked). The close comes through a path that bypasses all virtual
    /// input methods.
    ///
    /// v2.7.3 approach: Patch IClickableMenu.exitThisMenu() to intercept the actual close
    /// call, log a full stack trace to identify the caller, and block during grace period.
    /// This catches the close regardless of which code path triggers it.
    /// </summary>
    internal static class CarpenterMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Tick when the CarpenterMenu was opened. -1 means not tracking.</summary>
        private static int MenuOpenTick = -1;

        /// <summary>Number of ticks to block all input after menu opens.</summary>
        private const int GracePeriodTicks = 20;

        /// <summary>Whether exitThisMenu was called during the current menu's lifetime.</summary>
        private static bool ExitThisMenuWasCalled = false;

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
                // CRITICAL DIAGNOSTIC: Patch exitThisMenu on IClickableMenu.
                // This is the final common path for menu closes. By patching it we can:
                // 1. Log a stack trace to see exactly what code path closes the menu
                // 2. Block the close during the grace period
                // This fires for ALL menus but only acts on CarpenterMenu instances.
                harmony.Patch(
                    original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.exitThisMenu)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ExitThisMenu_Prefix))
                );

                // Keep input method patches for logging — they tell us what ISN'T being called
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveLeftClick_Prefix))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.leftClickHeld)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(LeftClickHeld_Prefix))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveKeyPress)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveKeyPress_Prefix))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                Monitor.Log("CarpenterMenu patches applied (including exitThisMenu diagnostic).", LogLevel.Trace);
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
            ExitThisMenuWasCalled = false;
            Monitor.Log($"CarpenterMenu opened at tick {MenuOpenTick}. Grace period: {GracePeriodTicks} ticks.", LogLevel.Debug);
        }

        /// <summary>Called from ModEntry.OnMenuChanged when the CarpenterMenu closes.</summary>
        public static void OnMenuClosed()
        {
            if (MenuOpenTick >= 0)
            {
                int duration = Game1.ticks - MenuOpenTick;
                string exitPath = ExitThisMenuWasCalled
                    ? "via exitThisMenu()"
                    : "NOT via exitThisMenu() — direct Game1.activeClickableMenu assignment?";
                Monitor.Log($"CarpenterMenu closed after {duration} ticks (grace was {GracePeriodTicks}). Exit path: {exitPath}", LogLevel.Debug);

                if (!ExitThisMenuWasCalled)
                {
                    // Close didn't go through exitThisMenu — log stack trace as fallback diagnostic.
                    // This stack trace will show SMAPI's MenuChanged dispatch, but confirms the exit
                    // bypassed exitThisMenu entirely (meaning direct activeClickableMenu = null).
                    Monitor.Log($"[CarpenterMenu] DIAGNOSTIC: Menu closed WITHOUT exitThisMenu! Stack trace:\n{Environment.StackTrace}", LogLevel.Debug);
                }
            }
            MenuOpenTick = -1;
            ExitThisMenuWasCalled = false;
        }

        /// <summary>Check if we're within the grace period after menu open.</summary>
        private static bool IsInGracePeriod()
        {
            return MenuOpenTick >= 0 && (Game1.ticks - MenuOpenTick) < GracePeriodTicks;
        }

        /// <summary>
        /// Prefix for IClickableMenu.exitThisMenu — the KEY diagnostic patch.
        /// Fires for ALL menus but only acts on CarpenterMenu instances.
        /// During grace period: logs full stack trace + gamepad state, then blocks the exit.
        /// After grace period: logs stack trace on first call only, then allows the exit.
        /// </summary>
        private static bool ExitThisMenu_Prefix(IClickableMenu __instance, bool playSound)
        {
            if (__instance is not CarpenterMenu)
                return true;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            ExitThisMenuWasCalled = true;
            int elapsed = MenuOpenTick >= 0 ? Game1.ticks - MenuOpenTick : -1;

            // Capture gamepad state at moment of close attempt
            GamePadState gpState = GamePad.GetState(PlayerIndex.One);
            string gpInfo = $"A={gpState.Buttons.A}, B={gpState.Buttons.B}, " +
                            $"X={gpState.Buttons.X}, Y={gpState.Buttons.Y}, " +
                            $"Start={gpState.Buttons.Start}, Back={gpState.Buttons.Back}, " +
                            $"LB={gpState.Buttons.LeftShoulder}, RB={gpState.Buttons.RightShoulder}";

            // Always log the full stack trace — this is the critical diagnostic info
            string stackTrace;
            try
            {
                stackTrace = new StackTrace(true).ToString();
            }
            catch
            {
                stackTrace = Environment.StackTrace;
            }

            Monitor.Log($"[CarpenterMenu] exitThisMenu() called! Elapsed: {elapsed}/{GracePeriodTicks} ticks, playSound={playSound}", LogLevel.Debug);
            Monitor.Log($"[CarpenterMenu] Gamepad state: {gpInfo}", LogLevel.Debug);
            Monitor.Log($"[CarpenterMenu] Stack trace:\n{stackTrace}", LogLevel.Debug);

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED exitThisMenu — grace period ({elapsed}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            Monitor.Log($"[CarpenterMenu] ALLOWED exitThisMenu — past grace period", LogLevel.Debug);
            return true;
        }

        /// <summary>Prefix for CarpenterMenu.receiveLeftClick — logs during grace period.</summary>
        private static bool ReceiveLeftClick_Prefix(CarpenterMenu __instance, int x, int y, bool playSound)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                int elapsed = Game1.ticks - MenuOpenTick;
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveLeftClick at ({x},{y}) — grace period ({elapsed}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>Prefix for CarpenterMenu.leftClickHeld — logs during grace period.</summary>
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

        /// <summary>Prefix for CarpenterMenu.receiveKeyPress — logs during grace period.</summary>
        private static bool ReceiveKeyPress_Prefix(CarpenterMenu __instance, Microsoft.Xna.Framework.Input.Keys key)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveKeyPress key={key} — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }

        /// <summary>Prefix for CarpenterMenu.receiveGamePadButton — logs during grace period.</summary>
        private static bool ReceiveGamePadButton_Prefix(CarpenterMenu __instance, Buttons b)
        {
            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return true;

            if (IsInGracePeriod())
            {
                Monitor.Log($"[CarpenterMenu] BLOCKED receiveGamePadButton button={b} — grace period ({Game1.ticks - MenuOpenTick}/{GracePeriodTicks} ticks)", LogLevel.Debug);
                return false;
            }

            return true;
        }
    }
}
