using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches to swap X and Y buttons during gameplay (not menus) based on layout/style.
    ///
    /// This works by patching GamePad.GetState() to return swapped X/Y button values
    /// BEFORE the game reads them, so all game code sees the swapped buttons.
    /// </summary>
    internal static class GameplayButtonPatches
    {
        private static IMonitor Monitor;

        /// <summary>Raw right stick Y cached from GetState before suppression, for ShopMenuPatches navigation.</summary>
        internal static float RawRightStickY;

        /// <summary>Cached swapped GamePadState to ensure all GetState() calls within the same tick return identical results.</summary>
        private static GamePadState? _cachedState;
        private static float _cachedRawRightStickY;
        private static int _cachedTick = -1;

        /// <summary>Suppress logical A in GetState output until the physical button is released.
        /// Set by ItemGrabMenuPatches when A on Close X closes a chest.</summary>
        internal static bool SuppressAUntilRelease;

        /// <summary>Suppress logical B in GetState output until the physical button is released.
        /// Set by ItemGrabMenuPatches when B closes the color picker.</summary>
        internal static bool SuppressBUntilRelease;

        /// <summary>Invalidate the cached GetState so the next call recomputes.
        /// Must be called after setting Suppress*UntilRelease flags mid-tick,
        /// since the cache may already have the unsuppressed state.</summary>
        internal static void InvalidateCache() { _cachedTick = -1; }

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch GamePad.GetState to swap X/Y buttons at the source
                // This affects ALL code that reads gamepad state, not just specific methods
                var getStateMethod = typeof(GamePad).GetMethod(
                    nameof(GamePad.GetState),
                    new Type[] { typeof(PlayerIndex) }
                );

                if (getStateMethod != null)
                {
                    harmony.Patch(
                        original: getStateMethod,
                        postfix: new HarmonyMethod(typeof(GameplayButtonPatches), nameof(GetState_Postfix))
                    );
                    Monitor.Log("Gameplay button patches applied successfully.", LogLevel.Trace);
                }
                else
                {
                    Monitor.Log("Could not find GamePad.GetState method!", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Gameplay button patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Determines if X and Y should be swapped based on LAYOUT only.
        /// X/Y functions are positional: Left=Tool, Top=Craft on all consoles.
        /// Switch layout needs swap because Android maps buttons by position differently.
        /// </summary>
        public static bool ShouldSwapXY()
        {
            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;

            // Swap X/Y only for Switch layout
            // Switch: physical X is top, physical Y is left - need to swap to match game expectations
            // Xbox/PS: physical X is left, physical Y is top - matches game expectations, no swap
            return layout == ControllerLayout.Switch;
        }

        /// <summary>
        /// Determines if A and B should be swapped based on layout vs style mismatch.
        /// </summary>
        public static bool ShouldSwapAB()
        {
            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;
            var style = ModEntry.Config?.ControlStyle ?? ControlStyle.Switch;

            bool isXboxLayout = layout == ControllerLayout.Xbox || layout == ControllerLayout.PlayStation;
            bool isXboxStyle = style == ControlStyle.Xbox;

            // Swap when layout and style don't match
            return isXboxLayout != isXboxStyle;
        }

        /// <summary>Apply suppress-until-release logic to the final gamepad state.
        /// For each active suppression flag: if the button is still pressed, zeros it out
        /// in the returned state; if released, clears the flag. This runs on the post-swap
        /// state so it suppresses the LOGICAL button the game sees.</summary>
        private static GamePadState ApplyButtonSuppression(GamePadState state)
        {
            bool suppressA = false, suppressB = false;

            if (SuppressAUntilRelease)
            {
                if (state.Buttons.A == ButtonState.Pressed)
                    suppressA = true;
                else
                    SuppressAUntilRelease = false;
            }

            if (SuppressBUntilRelease)
            {
                if (state.Buttons.B == ButtonState.Pressed)
                    suppressB = true;
                else
                    SuppressBUntilRelease = false;
            }

            if (!suppressA && !suppressB)
                return state;

            var newButtons = new GamePadButtons(
                ((state.Buttons.A == ButtonState.Pressed && !suppressA) ? Buttons.A : 0) |
                ((state.Buttons.B == ButtonState.Pressed && !suppressB) ? Buttons.B : 0) |
                ((state.Buttons.X == ButtonState.Pressed) ? Buttons.X : 0) |
                ((state.Buttons.Y == ButtonState.Pressed) ? Buttons.Y : 0) |
                ((state.Buttons.Start == ButtonState.Pressed) ? Buttons.Start : 0) |
                ((state.Buttons.Back == ButtonState.Pressed) ? Buttons.Back : 0) |
                ((state.Buttons.LeftStick == ButtonState.Pressed) ? Buttons.LeftStick : 0) |
                ((state.Buttons.RightStick == ButtonState.Pressed) ? Buttons.RightStick : 0) |
                ((state.Buttons.LeftShoulder == ButtonState.Pressed) ? Buttons.LeftShoulder : 0) |
                ((state.Buttons.RightShoulder == ButtonState.Pressed) ? Buttons.RightShoulder : 0) |
                ((state.Buttons.BigButton == ButtonState.Pressed) ? Buttons.BigButton : 0)
            );

            return new GamePadState(
                state.ThumbSticks,
                state.Triggers,
                newButtons,
                state.DPad
            );
        }

        /// <summary>
        /// Postfix for GamePad.GetState - modifies the returned state to swap buttons as needed.
        /// A/B swap applies EVERYWHERE (main menu, game menus, gameplay).
        /// X/Y swap applies only during GAMEPLAY (menus use ButtonRemapper for X/Y).
        /// </summary>
        private static void GetState_Postfix(PlayerIndex playerIndex, ref GamePadState __result)
        {
            try
            {
                // Only modify for player one
                if (playerIndex != PlayerIndex.One)
                    return;

                // Return cached state if we've already computed it this tick.
                // This prevents intra-frame inconsistency when GetState() is called
                // multiple times per frame, which caused tools to fire once before charging.
                int currentTick = Game1.ticks;
                if (_cachedState.HasValue && _cachedTick == currentTick)
                {
                    __result = _cachedState.Value;
                    RawRightStickY = _cachedRawRightStickY;
                    return;
                }

                // Cache raw right stick Y before any suppression, so ShopMenuPatches can use it
                RawRightStickY = __result.ThumbSticks.Right.Y;

                // Zero out right thumbstick when ShopMenu is on buy tab.
                // This prevents vanilla (and Game1's scroll-wheel conversion) from scrolling
                // currentItemIndex via right stick; our own navigation reads RawRightStickY.
                if (ShopMenuPatches.ShouldSuppressRightStick() && __result.ThumbSticks.Right != Vector2.Zero)
                {
                    __result = new GamePadState(
                        new GamePadThumbSticks(__result.ThumbSticks.Left, Vector2.Zero),
                        __result.Triggers,
                        __result.Buttons,
                        __result.DPad
                    );
                }



                // A/B swap applies everywhere (main menu, game menus, gameplay)
                bool swapAB = ShouldSwapAB();

                // X/Y swap during gameplay and BobberBar (fishing mini-game uses gameplay buttons)
                bool swapXY = ShouldSwapXY() && (Game1.activeClickableMenu == null || Game1.activeClickableMenu is BobberBar);

                // Nothing to do if no swapping needed
                if (!swapXY && !swapAB)
                {
                    __result = ApplyButtonSuppression(__result);
                    _cachedState = __result;
                    _cachedRawRightStickY = RawRightStickY;
                    _cachedTick = currentTick;
                    return;
                }

                // Get the original button states
                bool originalA = __result.Buttons.A == ButtonState.Pressed;
                bool originalB = __result.Buttons.B == ButtonState.Pressed;
                bool originalX = __result.Buttons.X == ButtonState.Pressed;
                bool originalY = __result.Buttons.Y == ButtonState.Pressed;

                // Determine final button states after swapping
                bool finalA = swapAB ? originalB : originalA;
                bool finalB = swapAB ? originalA : originalB;
                bool finalX = swapXY ? originalY : originalX;
                bool finalY = swapXY ? originalX : originalY;

                // Only reconstruct if something actually changed
                if (finalA == originalA && finalB == originalB && finalX == originalX && finalY == originalY)
                {
                    __result = ApplyButtonSuppression(__result);
                    _cachedState = __result;
                    _cachedRawRightStickY = RawRightStickY;
                    _cachedTick = currentTick;
                    return;
                }

                // Create new GamePadState with swapped buttons
                var newButtons = new GamePadButtons(
                    (finalA ? Buttons.A : 0) |
                    (finalB ? Buttons.B : 0) |
                    (finalX ? Buttons.X : 0) |
                    (finalY ? Buttons.Y : 0) |
                    ((__result.Buttons.Start == ButtonState.Pressed) ? Buttons.Start : 0) |
                    ((__result.Buttons.Back == ButtonState.Pressed) ? Buttons.Back : 0) |
                    ((__result.Buttons.LeftStick == ButtonState.Pressed) ? Buttons.LeftStick : 0) |
                    ((__result.Buttons.RightStick == ButtonState.Pressed) ? Buttons.RightStick : 0) |
                    ((__result.Buttons.LeftShoulder == ButtonState.Pressed) ? Buttons.LeftShoulder : 0) |
                    ((__result.Buttons.RightShoulder == ButtonState.Pressed) ? Buttons.RightShoulder : 0) |
                    ((__result.Buttons.BigButton == ButtonState.Pressed) ? Buttons.BigButton : 0)
                );

                __result = new GamePadState(
                    __result.ThumbSticks,
                    __result.Triggers,
                    newButtons,
                    __result.DPad
                );

                __result = ApplyButtonSuppression(__result);
                _cachedState = __result;
                _cachedRawRightStickY = RawRightStickY;
                _cachedTick = currentTick;
            }
            catch
            {
                // Silently ignore errors to not spam logs every frame
            }
        }
    }
}
