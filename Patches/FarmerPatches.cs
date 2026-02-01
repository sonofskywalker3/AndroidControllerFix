using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace AndroidControllerFix.Patches
{
    /// <summary>Harmony patches for Farmer to fix Android trigger toolbar navigation.</summary>
    internal static class FarmerPatches
    {
        private static IMonitor Monitor;

        /// <summary>The current toolbar row, synchronized from ModEntry.</summary>
        internal static int CurrentToolbarRow = 0;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch the CurrentToolIndex property setter to intercept Android's broken trigger handling
                var setter = AccessTools.PropertySetter(typeof(Farmer), nameof(Farmer.CurrentToolIndex));
                if (setter == null)
                {
                    Monitor.Log("Could not find Farmer.CurrentToolIndex setter!", LogLevel.Error);
                    return;
                }

                harmony.Patch(
                    original: setter,
                    prefix: new HarmonyMethod(typeof(FarmerPatches), nameof(CurrentToolIndex_Prefix))
                );

                Monitor.Log("Farmer patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Farmer patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix for Farmer.CurrentToolIndex setter.
        /// Intercepts and fixes Android's native trigger handler which is hardcoded to cycle 0-9.
        /// </summary>
        /// <param name="__instance">The Farmer instance.</param>
        /// <param name="value">The new tool index value (passed by ref so we can modify it).</param>
        /// <returns>True to continue with the (possibly modified) value.</returns>
        private static bool CurrentToolIndex_Prefix(Farmer __instance, ref int value)
        {
            try
            {
                // Only apply during gameplay for main player
                if (Game1.activeClickableMenu != null || !Context.IsPlayerFree)
                    return true;
                if (__instance != Game1.player)
                    return true;
                if (!(ModEntry.Config?.EnableToolbarNavFix ?? false))
                    return true;

                int oldValue = __instance.CurrentToolIndex;
                int currentRow = CurrentToolbarRow;
                int rowStart = currentRow * 12;
                int rowEnd = rowStart + 11;

                // FIX #1: Handle negative values (LT at slot 0 goes to -1)
                if (value < 0)
                {
                    int newValue = rowEnd; // Wrap to end of current row
                    if (ModEntry.Config?.VerboseLogging ?? false)
                    {
                        Monitor.Log($"Setter intercept: Negative wrap fix {value} -> {newValue}", LogLevel.Debug);
                    }
                    value = newValue;
                }
                // FIX #2: If value is 0-9 but we're on row 1+, remap to current row
                // Android's native trigger handler only cycles 0-9, ignoring the expanded toolbar
                else if (currentRow > 0 && value >= 0 && value <= 9)
                {
                    int newValue = rowStart + value;
                    if (ModEntry.Config?.VerboseLogging ?? false)
                    {
                        Monitor.Log($"Setter intercept (row {currentRow}): Remapping {value} -> {newValue}", LogLevel.Debug);
                    }
                    value = newValue;
                }
                // FIX #3: On row 0, fix wrap patterns (Android cycles 0-9 but we have 12 slots)
                else if (currentRow == 0)
                {
                    int oldPos = oldValue % 12;

                    // RT at slot 9: native goes 9->0, should go 9->10
                    if (oldPos == 9 && value == 0)
                    {
                        if (ModEntry.Config?.VerboseLogging ?? false)
                        {
                            Monitor.Log($"Setter intercept (row 0): RT wrap fix {value} -> 10", LogLevel.Debug);
                        }
                        value = 10;
                    }
                    // LT at slot 0: native goes 0->9, should go 0->11
                    else if (oldPos == 0 && value == 9)
                    {
                        if (ModEntry.Config?.VerboseLogging ?? false)
                        {
                            Monitor.Log($"Setter intercept (row 0): LT wrap fix {value} -> 11", LogLevel.Debug);
                        }
                        value = 11;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in CurrentToolIndex prefix: {ex.Message}", LogLevel.Error);
            }

            return true; // Continue with (possibly modified) value
        }
    }
}
