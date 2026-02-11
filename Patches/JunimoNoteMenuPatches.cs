using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Patches for JunimoNoteMenu (Community Center bundles).
    /// Fixes controller navigation on the bundle donation page where Android's
    /// currentlySnappedComponent stays null and A-press clicks go to wrong coordinates.
    ///
    /// Key insight: On Android, receiveLeftClick reads GetMouseState() internally instead
    /// of using its x,y parameters. We override GetMouseState during the A-press window
    /// (same pattern as CarpenterMenuPatches) so the game reads our tracked position.
    ///
    /// Touch is NOT intercepted — no receiveLeftClick prefix, and the GetMouseState
    /// override is only active during the brief A-press window.
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;

        // Cached reflection
        private static FieldInfo _specificBundlePageField;

        // Navigation tracking on the bundle donation page
        private static ClickableComponent _trackedComponent = null;
        private static bool _onBundlePage = false;
        private static bool _needsInit = false;

        // GetMouseState override — active only during A-press window
        private static bool _overrideMousePosition = false;
        private static int _overrideMouseX;
        private static int _overrideMouseY;
        private static int _overrideSetTick = -1;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "draw", new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Draw_Postfix))
            );

            // GetMouseState override — same pattern as CarpenterMenuPatches.
            // Both postfixes can coexist: each checks its own flag and only one
            // menu can be open at a time.
            try
            {
                var inputType = Game1.input.GetType();
                var getMouseStateMethod = AccessTools.Method(inputType, "GetMouseState");
                if (getMouseStateMethod != null)
                {
                    harmony.Patch(
                        original: getMouseStateMethod,
                        postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(GetMouseState_Postfix))
                    );
                }

                var xnaMouseGetState = AccessTools.Method(typeof(Mouse), nameof(Mouse.GetState));
                if (xnaMouseGetState != null)
                {
                    harmony.Patch(
                        original: xnaMouseGetState,
                        postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(GetMouseState_Postfix))
                    );
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] Failed to patch GetMouseState: {ex.Message}", LogLevel.Error);
            }

            Monitor.Log("JunimoNoteMenu patches applied.", LogLevel.Trace);
        }

        public static void OnMenuChanged()
        {
            _trackedComponent = null;
            _onBundlePage = false;
            _needsInit = false;
            _overrideMousePosition = false;
        }

        /// <summary>
        /// On the bundle donation page, intercept navigation and A-press.
        /// For A: activate GetMouseState override so the game reads our tracked position,
        /// then let the game's own A handler run naturally.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                if (!GetSpecificBundlePage(__instance))
                    return true; // Overview page — let game handle it

                switch (b)
                {
                    case Buttons.LeftThumbstickLeft:
                    case Buttons.DPadLeft:
                        NavigateDirection(__instance, "left");
                        return false;

                    case Buttons.LeftThumbstickRight:
                    case Buttons.DPadRight:
                        NavigateDirection(__instance, "right");
                        return false;

                    case Buttons.LeftThumbstickUp:
                    case Buttons.DPadUp:
                        NavigateDirection(__instance, "up");
                        return false;

                    case Buttons.LeftThumbstickDown:
                    case Buttons.DPadDown:
                        NavigateDirection(__instance, "down");
                        return false;

                    case Buttons.A:
                        if (_trackedComponent != null)
                        {
                            var center = _trackedComponent.bounds.Center;

                            // Activate GetMouseState override so the game reads our position
                            // when it internally calls GetMouseState during receiveLeftClick.
                            // Coordinates must be in raw screen pixels (game divides by zoom).
                            float zoom = Game1.options.zoomLevel;
                            _overrideMouseX = (int)(center.X * zoom);
                            _overrideMouseY = (int)(center.Y * zoom);
                            _overrideMousePosition = true;
                            _overrideSetTick = Game1.ticks;

                            // Also set mouse position directly for any code that reads it
                            Game1.setMousePosition(center.X, center.Y);
                            __instance.currentlySnappedComponent = _trackedComponent;

                            Monitor.Log($"[JunimoNote] A: override mouse to ({center.X},{center.Y}) raw=({_overrideMouseX},{_overrideMouseY}) on id={_trackedComponent.myID} '{_trackedComponent.name}'", LogLevel.Debug);
                        }
                        return true; // Let game handle A — it will read our overridden mouse

                    case Buttons.B:
                        _trackedComponent = null;
                        _onBundlePage = false;
                        _overrideMousePosition = false;
                        return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] receiveGamePadButton error: {ex.Message}", LogLevel.Debug);
            }

            return true;
        }

        // NO receiveLeftClick prefix — that was breaking touch by redirecting all clicks
        // to the tracked position. Touch clicks don't go through receiveGamePadButton,
        // so the GetMouseState override is never active for them.

        /// <summary>
        /// Detect bundle page entry/exit. Clear GetMouseState override after timeout.
        /// Does NOT continuously drive cursor position (that was fighting the game).
        /// </summary>
        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                if (specificBundle && !_onBundlePage)
                {
                    _onBundlePage = true;
                    _needsInit = true;
                }
                else if (!specificBundle && _onBundlePage)
                {
                    _onBundlePage = false;
                    _trackedComponent = null;
                    _overrideMousePosition = false;
                }

                // Defer init by one frame so allClickableComponents is fully populated
                if (_needsInit && specificBundle)
                {
                    _needsInit = false;
                    InitializeCursor(__instance);
                }

                // Safety: clear GetMouseState override after 3 ticks.
                // The touch-simulated receiveLeftClick fires within 1-2 ticks of A press.
                if (_overrideMousePosition && Game1.ticks - _overrideSetTick > 3)
                {
                    _overrideMousePosition = false;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] Update_Postfix error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Draw a visible highlight around the tracked component on the bundle page.
        /// The game's own cursor rendering doesn't work with our approach, so we draw our own.
        /// </summary>
        private static void Draw_Postfix(JunimoNoteMenu __instance, SpriteBatch b)
        {
            if (!_onBundlePage || _trackedComponent == null)
                return;

            try
            {
                var bounds = _trackedComponent.bounds;

                // Draw yellow border around the tracked component
                Color borderColor = Color.Yellow;
                int bw = 3;
                Texture2D pixel = Game1.staminaRect;
                // Top
                b.Draw(pixel, new Rectangle(bounds.X - bw, bounds.Y - bw, bounds.Width + bw * 2, bw), borderColor);
                // Bottom
                b.Draw(pixel, new Rectangle(bounds.X - bw, bounds.Y + bounds.Height, bounds.Width + bw * 2, bw), borderColor);
                // Left
                b.Draw(pixel, new Rectangle(bounds.X - bw, bounds.Y, bw, bounds.Height), borderColor);
                // Right
                b.Draw(pixel, new Rectangle(bounds.X + bounds.Width, bounds.Y, bw, bounds.Height), borderColor);
            }
            catch { }
        }

        /// <summary>
        /// GetMouseState postfix — when override is active, replace X/Y with our
        /// tracked component position. Only active for ~3 ticks after A press.
        /// Touch is unaffected (override is never set for touch input).
        /// </summary>
        private static void GetMouseState_Postfix(ref MouseState __result)
        {
            if (!_overrideMousePosition)
                return;

            __result = new MouseState(
                _overrideMouseX, _overrideMouseY,
                __result.ScrollWheelValue,
                __result.LeftButton,
                __result.MiddleButton,
                __result.RightButton,
                __result.XButton1,
                __result.XButton2
            );
        }

        /// <summary>Initialize the cursor to the first inventory slot.</summary>
        private static void InitializeCursor(JunimoNoteMenu menu)
        {
            if (menu.inventory?.inventory != null && menu.inventory.inventory.Count > 0)
            {
                _trackedComponent = menu.inventory.inventory[0];
                menu.currentlySnappedComponent = _trackedComponent;
                Monitor.Log($"[JunimoNote] Initialized cursor to inv slot 0, bounds={_trackedComponent.bounds}", LogLevel.Debug);
            }
        }

        /// <summary>Navigate in a direction using neighbor IDs.</summary>
        private static void NavigateDirection(JunimoNoteMenu menu, string direction)
        {
            if (_trackedComponent == null)
            {
                InitializeCursor(menu);
                return;
            }

            int neighborId;
            switch (direction)
            {
                case "left":  neighborId = _trackedComponent.leftNeighborID; break;
                case "right": neighborId = _trackedComponent.rightNeighborID; break;
                case "up":    neighborId = _trackedComponent.upNeighborID; break;
                case "down":  neighborId = _trackedComponent.downNeighborID; break;
                default: return;
            }

            if (neighborId == -1 || neighborId == -500)
                return;

            ClickableComponent target = null;

            if (neighborId == -99998)
            {
                target = FindNearestInDirection(menu, _trackedComponent, direction);
            }
            else if (neighborId == -7777)
            {
                return; // Bundle icons — don't navigate to them
            }
            else
            {
                target = menu.getComponentWithID(neighborId);
            }

            if (target != null && target != _trackedComponent)
            {
                _trackedComponent = target;
                menu.currentlySnappedComponent = _trackedComponent;
                Game1.playSound("shiny4");
            }
        }

        /// <summary>Find the nearest component in a given direction from the current one.</summary>
        private static ClickableComponent FindNearestInDirection(JunimoNoteMenu menu, ClickableComponent from, string direction)
        {
            if (menu.allClickableComponents == null) return null;

            var fromCenter = from.bounds.Center;
            ClickableComponent best = null;
            float bestDist = float.MaxValue;

            foreach (var comp in menu.allClickableComponents)
            {
                if (comp == from) continue;
                if (comp.myID == -500) continue;
                if (comp.fullyImmutable) continue;

                var compCenter = comp.bounds.Center;
                bool valid = false;

                switch (direction)
                {
                    case "right": valid = compCenter.X > fromCenter.X + 8; break;
                    case "left":  valid = compCenter.X < fromCenter.X - 8; break;
                    case "down":  valid = compCenter.Y > fromCenter.Y + 8; break;
                    case "up":    valid = compCenter.Y < fromCenter.Y - 8; break;
                }

                if (!valid) continue;

                float dx = compCenter.X - fromCenter.X;
                float dy = compCenter.Y - fromCenter.Y;
                float dist;

                if (direction == "left" || direction == "right")
                    dist = Math.Abs(dx) + Math.Abs(dy) * 3f;
                else
                    dist = Math.Abs(dy) + Math.Abs(dx) * 3f;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = comp;
                }
            }

            return best;
        }

        private static bool GetSpecificBundlePage(JunimoNoteMenu menu)
        {
            try
            {
                if (_specificBundlePageField != null)
                    return (bool)_specificBundlePageField.GetValue(menu);
            }
            catch { }
            return false;
        }
    }
}
