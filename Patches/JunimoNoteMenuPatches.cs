using System;
using System.Collections.Generic;
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
    /// Patches for JunimoNoteMenu (Community Center bundles).
    /// Fixes controller navigation on the bundle donation page where Android's
    /// currentlySnappedComponent stays null and A-press clicks go to wrong coordinates.
    /// We manage our own cursor and generate correct click coordinates.
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;

        // Cached reflection fields
        private static FieldInfo _specificBundlePageField;
        private static FieldInfo _ingredientSlotsField;
        private static FieldInfo _ingredientListField;

        // Our tracked cursor position on the bundle donation page
        private static ClickableComponent _trackedComponent = null;
        private static bool _onBundlePage = false;
        private static bool _needsInit = false;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection
            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
            _ingredientSlotsField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientSlots");
            _ingredientListField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientList");

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveLeftClick_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            Monitor.Log("JunimoNoteMenu patches applied.", LogLevel.Trace);
        }

        public static void OnMenuChanged()
        {
            _trackedComponent = null;
            _onBundlePage = false;
            _needsInit = false;
        }

        /// <summary>
        /// On the bundle donation page, intercept all gamepad input.
        /// We manage navigation ourselves and fire receiveLeftClick at correct coordinates.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);
                if (!specificBundle)
                    return true; // Overview page — let game handle it

                // On the bundle donation page, handle navigation ourselves
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
                        // Set mouse + snapped component, then let the game's A handler run
                        if (_trackedComponent != null)
                        {
                            var center = _trackedComponent.bounds.Center;
                            Game1.setMousePosition(center.X, center.Y);
                            __instance.currentlySnappedComponent = _trackedComponent;
                            Monitor.Log($"[JunimoNote] A: set mouse to ({center.X},{center.Y}) on id={_trackedComponent.myID} '{_trackedComponent.name}'", LogLevel.Debug);
                        }
                        return true; // Let game handle A with correct position

                    case Buttons.B:
                        // Let the game handle B (close bundle page / close menu)
                        // But reset our tracking since we're leaving the bundle page
                        _trackedComponent = null;
                        _onBundlePage = false;
                        return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] receiveGamePadButton error: {ex.Message}", LogLevel.Debug);
            }

            return true; // Other buttons pass through
        }

        /// <summary>
        /// On the bundle donation page, redirect gamepad-triggered clicks to the tracked component.
        /// Touch clicks pass through as-is.
        /// </summary>
        private static void ReceiveLeftClick_Prefix(JunimoNoteMenu __instance, ref int x, ref int y)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);
                if (!specificBundle || _trackedComponent == null)
                    return;

                // If mouse position is far from any inventory/ingredient component,
                // it's likely a gamepad-triggered click at stale coordinates.
                // Redirect to our tracked component.
                var center = _trackedComponent.bounds.Center;
                if (Math.Abs(x - center.X) > 200 || Math.Abs(y - center.Y) > 200)
                {
                    Monitor.Log($"[JunimoNote] Redirecting click from ({x},{y}) to tracked ({center.X},{center.Y}) id={_trackedComponent.myID}", LogLevel.Debug);
                    x = center.X;
                    y = center.Y;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] receiveLeftClick prefix error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Detect when we enter/leave the bundle donation page and initialize cursor.
        /// </summary>
        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                if (specificBundle && !_onBundlePage)
                {
                    // Just entered the bundle donation page — initialize cursor
                    _onBundlePage = true;
                    _needsInit = true;
                }
                else if (!specificBundle && _onBundlePage)
                {
                    // Left the bundle page
                    _onBundlePage = false;
                    _trackedComponent = null;
                }

                // Defer init by one frame so allClickableComponents is fully populated
                if (_needsInit && specificBundle)
                {
                    _needsInit = false;
                    InitializeCursor(__instance);
                }

                // Keep the visual cursor at our tracked component
                if (specificBundle && _trackedComponent != null)
                {
                    __instance.currentlySnappedComponent = _trackedComponent;
                    var center = _trackedComponent.bounds.Center;
                    Game1.setMousePosition(center.X, center.Y);
                    __instance.snapCursorToCurrentSnappedComponent();
                    // Drive hover highlight so the game renders the selection box
                    __instance.performHoverAction(center.X, center.Y);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] Update_Postfix error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>Initialize the cursor to the first inventory slot.</summary>
        private static void InitializeCursor(JunimoNoteMenu menu)
        {
            // Start at inventory slot 0
            if (menu.inventory?.inventory != null && menu.inventory.inventory.Count > 0)
            {
                _trackedComponent = menu.inventory.inventory[0];
                menu.currentlySnappedComponent = _trackedComponent;
                menu.snapCursorToCurrentSnappedComponent();
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
                return; // No neighbor in that direction

            ClickableComponent target = null;

            if (neighborId == -99998)
            {
                // Auto-snap: find nearest component in the given direction
                target = FindNearestInDirection(menu, _trackedComponent, direction);
            }
            else if (neighborId == -7777)
            {
                // Custom snap — skip (bundle icons use this, don't want to navigate to them)
                return;
            }
            else
            {
                // Direct neighbor ID
                target = menu.getComponentWithID(neighborId);
            }

            if (target != null && target != _trackedComponent)
            {
                _trackedComponent = target;
                menu.currentlySnappedComponent = _trackedComponent;
                menu.snapCursorToCurrentSnappedComponent();
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
                if (comp.myID == -500) continue; // Skip back/nav buttons with broken IDs
                if (comp.fullyImmutable) continue; // Skip bundle icons

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

                // Weight: heavily favor components aligned on the perpendicular axis
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
