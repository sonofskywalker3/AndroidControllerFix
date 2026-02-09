using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
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

        /// <summary>When true, all furniture interactions are blocked until the tool button is released.</summary>
        private static bool _suppressFurnitureUntilRelease = false;

        // --- Joystick cursor/panning fields ---
        private static FieldInfo OnFarmField;      // bool — true when showing farm view
        private static FieldInfo FreezeField;      // bool — true during animations/transitions
        private static FieldInfo CurrentBuildingField; // Building — the building being placed

        private const float StickDeadzone = 0.2f;
        private const float CursorSpeedMax = 16f;  // px/tick at full tilt
        private const float CursorSpeedMin = 2f;   // px/tick at deadzone edge
        private const int PanEdgeMargin = 64;       // px from viewport edge to start panning
        private const int PanSpeed = 16;             // px/tick viewport scroll at edge

        /// <summary>Whether the cursor has been centered for the current farm view session.</summary>
        private static bool _cursorCentered = false;

        /// <summary>Tracked cursor position (sub-pixel precision, UI coordinates).</summary>
        private static float _cursorX, _cursorY;

        /// <summary>When true, cursor is visible and A button triggers clicks at cursor position.</summary>
        private static bool _cursorActive = false;

        /// <summary>Previous frame's A button state for edge detection.</summary>
        private static bool _prevAPressed = true;

        /// <summary>When true, GetMouseState postfix returns cursor position. Only active during receiveLeftClick.</summary>
        private static bool _overridingMousePosition = false;

        /// <summary>Current building's tile height, cached from Update_Postfix for use in GetMouseState_Postfix.</summary>
        private static int _buildingTileHeight = 0;

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

                // Cache reflection fields for farm-view cursor movement
                OnFarmField = AccessTools.Field(typeof(CarpenterMenu), "onFarm");
                FreezeField = AccessTools.Field(typeof(CarpenterMenu), "freeze");
                CurrentBuildingField = AccessTools.Field(typeof(CarpenterMenu), "currentBuilding");

                // Postfix on update — reads left stick and moves cursor when on farm
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.update),
                                                 new[] { typeof(GameTime) }),
                    postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(Update_Postfix))
                );

                // Postfix on draw — renders visible cursor at joystick position
                harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.draw),
                                                 new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(Draw_Postfix))
                );

                Monitor.Log("CarpenterMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply CarpenterMenu patches: {ex.Message}", LogLevel.Error);
            }

            // GetMouseState patches — receiveLeftClick reads mouse position internally instead of
            // using its x,y parameters. We override GetMouseState momentarily during the call so
            // the game reads our cursor position for building placement.
            try
            {
                var inputType = Game1.input.GetType();
                var getMouseStateMethod = AccessTools.Method(inputType, "GetMouseState");
                if (getMouseStateMethod != null)
                {
                    harmony.Patch(
                        original: getMouseStateMethod,
                        postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseState_Postfix))
                    );
                    Monitor.Log($"Patched {inputType.Name}.GetMouseState postfix.", LogLevel.Trace);
                }

                var xnaMouseGetState = AccessTools.Method(typeof(Mouse), nameof(Mouse.GetState));
                if (xnaMouseGetState != null)
                {
                    harmony.Patch(
                        original: xnaMouseGetState,
                        postfix: new HarmonyMethod(typeof(CarpenterMenuPatches), nameof(GetMouseState_Postfix))
                    );
                    Monitor.Log("Patched Mouse.GetState postfix.", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply mouse state patches: {ex.Message}", LogLevel.Error);
            }

            // Furniture debounce — separate try/catch so carpenter patches still work if this fails
            // The rapid-toggle cycle on Android is: canBeRemoved → performRemoveAction → placementAction
            // repeating every ~3 ticks while the tool button is held. We debounce BOTH directions:
            // - After pickup (performRemoveAction): block auto-placement so furniture stays in inventory
            // - After placement (placementAction): block auto-pickup so furniture stays placed
            if (ModEntry.Config.EnableFurnitureDebounce)
            {
                try
                {
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
                    Monitor.Log("Furniture debounce patches applied successfully.", LogLevel.Trace);
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
            _cursorCentered = false;
            _prevAPressed = true;
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
            _cursorCentered = false;
            _cursorActive = false;
            _prevAPressed = true;
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
        /// Postfix for CarpenterMenu.update — moves joystick cursor with left stick.
        /// Stick moves a visible cursor around the screen. When the cursor reaches the
        /// viewport edge, the viewport scrolls. A button fires receiveLeftClick at cursor
        /// position to snap the building ghost there (same as a touch tap).
        /// </summary>
        private static void Update_Postfix(CarpenterMenu __instance)
        {
            // Clear cursor by default — only set when all conditions are met
            _cursorActive = false;

            if (!ModEntry.Config.EnableCarpenterMenuFix)
                return;

            if (OnFarmField == null || FreezeField == null)
                return;

            // Only active when on the farm view
            if (!(bool)OnFarmField.GetValue(__instance))
            {
                _cursorCentered = false;
                return;
            }

            // Don't move during animations/transitions
            if ((bool)FreezeField.GetValue(__instance))
                return;

            // Don't move during grace period
            if (IsInGracePeriod())
                return;

            // Don't move during screen fades
            if (Game1.IsFading())
                return;

            // Enable cursor — draw postfix will render it, A button will click at its position
            _cursorActive = true;

            // Read current building dimensions for ghost centering in GetMouseState_Postfix
            var building = CurrentBuildingField?.GetValue(__instance) as Building;
            _buildingTileHeight = building?.tilesHigh.Value ?? 0;

            // Center cursor on first farm-view frame so building ghost starts mid-screen
            if (!_cursorCentered)
            {
                _cursorX = Game1.viewport.Width / 2f;
                _cursorY = Game1.viewport.Height / 2f;
                _cursorCentered = true;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Centered cursor to ({(int)_cursorX},{(int)_cursorY})", LogLevel.Debug);
                return;
            }

            var thumbStick = GamePad.GetState(PlayerIndex.One).ThumbSticks.Left;

            float absX = Math.Abs(thumbStick.X);
            float absY = Math.Abs(thumbStick.Y);

            // Move cursor if stick is above deadzone
            if (absX > StickDeadzone || absY > StickDeadzone)
            {
                float deltaX = 0f, deltaY = 0f;

                if (absX > StickDeadzone)
                {
                    float t = (absX - StickDeadzone) / (1f - StickDeadzone);
                    float speed = CursorSpeedMin + t * (CursorSpeedMax - CursorSpeedMin);
                    deltaX = Math.Sign(thumbStick.X) * speed;
                }

                if (absY > StickDeadzone)
                {
                    // Invert Y: stick up (positive) = screen up (negative Y)
                    float t = (absY - StickDeadzone) / (1f - StickDeadzone);
                    float speed = CursorSpeedMin + t * (CursorSpeedMax - CursorSpeedMin);
                    deltaY = -Math.Sign(thumbStick.Y) * speed;
                }

                _cursorX = Math.Max(0, Math.Min(_cursorX + deltaX, Game1.viewport.Width - 1));
                _cursorY = Math.Max(0, Math.Min(_cursorY + deltaY, Game1.viewport.Height - 1));

                int ix = (int)_cursorX;
                int iy = (int)_cursorY;

                int panX = 0, panY = 0;

                if (ix < PanEdgeMargin)
                    panX = -PanSpeed;
                else if (ix > Game1.viewport.Width - PanEdgeMargin)
                    panX = PanSpeed;

                if (iy < PanEdgeMargin)
                    panY = -PanSpeed;
                else if (iy > Game1.viewport.Height - PanEdgeMargin)
                    panY = PanSpeed;

                if (panX != 0 || panY != 0)
                {
                    Game1.panScreen(panX, panY);

                    // Compensate cursor for viewport movement — keeps cursor at the same
                    // world position. Without this, the cursor sticks to the screen edge
                    // and panning continues even after the stick changes direction.
                    _cursorX -= panX;
                    _cursorY -= panY;
                    _cursorX = Math.Max(0, Math.Min(_cursorX, Game1.viewport.Width - 1));
                    _cursorY = Math.Max(0, Math.Min(_cursorY, Game1.viewport.Height - 1));
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] Cursor: ({(int)_cursorX},{(int)_cursorY}) pan=({panX},{panY})", LogLevel.Trace);
            }

            // A button → snap ghost to cursor position via receiveLeftClick.
            // GetMouseState override is active during the call so the game reads
            // our cursor coords for ghost positioning. The game's own A handler
            // also fires (we don't block it) — it handles actual building placement
            // at the ghost's current position.
            var gps = Game1.input.GetGamePadState();
            bool aPressed = gps.Buttons.A == ButtonState.Pressed;
            if (aPressed && !_prevAPressed)
            {
                _overridingMousePosition = true;
                __instance.receiveLeftClick((int)_cursorX, (int)_cursorY);
                _overridingMousePosition = false;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[CarpenterMenu] A pressed → receiveLeftClick({(int)_cursorX},{(int)_cursorY}) zoom={Game1.options.zoomLevel} buildH={_buildingTileHeight} yOffset={_buildingTileHeight * 32}", LogLevel.Debug);
            }
            _prevAPressed = aPressed;
        }

        /// <summary>
        /// Harmony postfix for GetMouseState. Only active during receiveLeftClick calls
        /// (when _overridingMousePosition is true). Replaces X/Y with cursor position
        /// so the game reads our coordinates for building placement.
        /// </summary>
        private static void GetMouseState_Postfix(ref MouseState __result)
        {
            if (!_overridingMousePosition)
                return;

            // Cursor coords are in game units (viewport space). GetMouseState returns raw
            // screen pixels, and the game divides by zoomLevel to get game units. Multiply
            // by zoom so the division yields the correct game-unit position.
            //
            // Y offset: The ghost anchor is at the building's top-left corner, so it extends
            // downward. Subtract half the building height (in game pixels) to center the
            // ghost vertically on the cursor. 1 tile = 64 game pixels, half = 32.
            float zoom = Game1.options.zoomLevel;
            float adjustedY = _cursorY - _buildingTileHeight * 32;
            __result = new MouseState(
                (int)(_cursorX * zoom), (int)(adjustedY * zoom),
                __result.ScrollWheelValue,
                __result.LeftButton,
                __result.MiddleButton,
                __result.RightButton,
                __result.XButton1,
                __result.XButton2
            );
        }

        /// <summary>
        /// Postfix for CarpenterMenu.draw — draws a visible cursor at the joystick
        /// position when on the farm view. Shows where A button will click.
        /// </summary>
        private static void Draw_Postfix(CarpenterMenu __instance, SpriteBatch b)
        {
            if (!_cursorActive)
                return;

            b.Draw(
                Game1.mouseCursors,
                new Vector2(_cursorX, _cursorY),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16),
                Color.White,
                0f,
                Vector2.Zero,
                4f + Game1.dialogueButtonScale / 150f,
                SpriteEffects.None,
                1f
            );
        }

        /// <summary>
        /// Called from ModEntry.OnUpdateTicked every tick during gameplay.
        /// Clears the suppress flag once the tool button is released, allowing
        /// the next distinct button press to interact with furniture.
        /// </summary>
        public static void OnFurnitureUpdateTicked()
        {
            if (!_suppressFurnitureUntilRelease)
                return;

            var gps = Game1.input.GetGamePadState();
            bool toolButtonHeld = gps.Buttons.X == ButtonState.Pressed
                || gps.Buttons.Y == ButtonState.Pressed;

            if (!toolButtonHeld)
            {
                _suppressFurnitureUntilRelease = false;
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("[Furniture] Tool button released — suppress cleared.", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Prefix for Furniture.canBeRemoved — blocks re-pickup while tool button is held
        /// after a previous furniture interaction (placement or pickup).
        /// </summary>
        private static bool FurnitureCanBeRemoved_Prefix(Furniture __instance, ref bool __result)
        {
            if (_suppressFurnitureUntilRelease)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[Furniture] BLOCKED canBeRemoved on '{__instance.Name}' — suppress until release", LogLevel.Debug);
                __result = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prefix for Furniture.performRemoveAction — sets suppress flag after pickup
        /// so the immediate auto-placement is blocked until the button is released.
        /// </summary>
        private static void FurniturePerformRemoveAction_Prefix()
        {
            _suppressFurnitureUntilRelease = true;
        }

        /// <summary>
        /// Prefix for Furniture.placementAction — blocks auto-placement while suppressed,
        /// and sets suppress flag when placement goes through (to block re-pickup).
        /// </summary>
        private static bool FurniturePlacementAction_Prefix(Furniture __instance, ref bool __result)
        {
            if (_suppressFurnitureUntilRelease)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[Furniture] BLOCKED placementAction on '{__instance.Name}' — suppress until release", LogLevel.Debug);
                __result = false;
                return false;
            }

            _suppressFurnitureUntilRelease = true;
            return true;
        }
    }
}
