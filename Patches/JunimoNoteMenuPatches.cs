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
    /// Controller support for the Community Center bundle donation page.
    /// Manages a tracked cursor over inventory slots. On A press, overrides
    /// GetMouseState so receiveLeftClick reads the tracked position (Android's
    /// receiveLeftClick ignores its x,y params and reads GetMouseState internally).
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;

        // Cached reflection
        private static FieldInfo _specificBundlePageField;
        private static FieldInfo _heldItemField;
        private static FieldInfo _ingredientSlotsField;
        private static FieldInfo _ingredientListField;

        // Donation page state
        private static bool _onDonationPage;
        private static int _trackedSlotIndex;
        private static int _maxSlotIndex = 23;
        private const int INV_COLUMNS = 6;

        // GetMouseState override — active during A-press receiveLeftClick AND during draw
        private static bool _overridingMouse;
        private static int _overrideRawX, _overrideRawY;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection
            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
            _heldItemField = AccessTools.Field(typeof(JunimoNoteMenu), "heldItem");
            _ingredientSlotsField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientSlots");
            _ingredientListField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientList");

            // Patch GetMouseState — both input wrapper and XNA Mouse paths
            var inputType = Game1.input.GetType();
            var getMouseStateMethod = AccessTools.Method(inputType, "GetMouseState");
            if (getMouseStateMethod != null)
            {
                harmony.Patch(
                    original: getMouseStateMethod,
                    postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(GetMouseState_Postfix))
                );
            }

            var mouseGetState = AccessTools.Method(typeof(Mouse), nameof(Mouse.GetState));
            if (mouseGetState != null)
            {
                harmony.Patch(
                    original: mouseGetState,
                    postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(GetMouseState_Postfix))
                );
            }

            // receiveGamePadButton — handle navigation and A press
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            // update — detect page transitions, keep cursor in sync
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            // draw — override GetMouseState during draw so game's own drawMouse renders
            // its native cursor at the tracked slot position (consistent with inventory/chest)
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "draw", new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Draw_Prefix)),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Draw_Postfix))
            );

            Monitor.Log("JunimoNoteMenu patches applied.", LogLevel.Trace);
        }

        public static void OnMenuChanged()
        {
            _onDonationPage = false;
            _overridingMouse = false;
        }

        // ===== GetMouseState postfix =====

        private static void GetMouseState_Postfix(ref MouseState __result)
        {
            if (!_overridingMouse) return;
            __result = new MouseState(
                _overrideRawX, _overrideRawY,
                __result.ScrollWheelValue,
                __result.LeftButton, __result.MiddleButton, __result.RightButton,
                __result.XButton1, __result.XButton2
            );
        }

        // ===== receiveGamePadButton prefix =====

        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                if (!GetSpecificBundlePage(__instance))
                    return true; // not on donation page — let game handle

                switch (b)
                {
                    case Buttons.LeftThumbstickRight:
                    case Buttons.DPadRight:
                        Navigate(__instance, 1, 0);
                        return false;

                    case Buttons.LeftThumbstickLeft:
                    case Buttons.DPadLeft:
                        Navigate(__instance, -1, 0);
                        return false;

                    case Buttons.LeftThumbstickDown:
                    case Buttons.DPadDown:
                        Navigate(__instance, 0, 1);
                        return false;

                    case Buttons.LeftThumbstickUp:
                    case Buttons.DPadUp:
                        Navigate(__instance, 0, -1);
                        return false;

                    case Buttons.A:
                        HandleAPress(__instance);
                        return false; // we call receiveLeftClick ourselves

                    default:
                        return true; // B closes menu, etc.
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] receiveGamePadButton error: {ex}", LogLevel.Error);
                return true;
            }
        }

        // ===== Navigation =====

        private static void Navigate(JunimoNoteMenu menu, int dx, int dy)
        {
            int col = _trackedSlotIndex % INV_COLUMNS;
            int row = _trackedSlotIndex / INV_COLUMNS;
            int maxRow = _maxSlotIndex / INV_COLUMNS;

            col += dx;
            row += dy;

            col = Math.Max(0, Math.Min(col, INV_COLUMNS - 1));
            row = Math.Max(0, Math.Min(row, maxRow));

            int newIndex = row * INV_COLUMNS + col;
            newIndex = Math.Max(0, Math.Min(newIndex, _maxSlotIndex));

            if (newIndex != _trackedSlotIndex)
            {
                _trackedSlotIndex = newIndex;
                SnapToSlot(menu);
            }
        }

        private static void SnapToSlot(JunimoNoteMenu menu)
        {
            if (menu.inventory?.inventory == null || _trackedSlotIndex >= menu.inventory.inventory.Count)
                return;

            var slot = menu.inventory.inventory[_trackedSlotIndex];
            menu.currentlySnappedComponent = slot;
            menu.snapCursorToCurrentSnappedComponent();
        }

        // ===== A-press handling =====

        private static void HandleAPress(JunimoNoteMenu menu)
        {
            if (menu.inventory?.inventory == null || _trackedSlotIndex >= menu.inventory.inventory.Count)
                return;

            var slot = menu.inventory.inventory[_trackedSlotIndex];
            var center = slot.bounds.Center;

            // Set one-shot GetMouseState override so receiveLeftClick reads our position
            float zoom = Game1.options.zoomLevel;
            _overrideRawX = (int)(center.X * zoom);
            _overrideRawY = (int)(center.Y * zoom);
            _overridingMouse = true;

            var heldBefore = GetHeldItem(menu);
            Item invItem = (_trackedSlotIndex < Game1.player.Items.Count) ? Game1.player.Items[_trackedSlotIndex] : null;
            Monitor.Log($"[JunimoNote] A on slot {_trackedSlotIndex} ({invItem?.DisplayName ?? "empty"}): click ({center.X},{center.Y}) raw=({_overrideRawX},{_overrideRawY}) held={heldBefore?.DisplayName ?? "null"}", LogLevel.Debug);

            try
            {
                menu.receiveLeftClick(center.X, center.Y);
            }
            finally
            {
                _overridingMouse = false;
            }

            var heldAfter = GetHeldItem(menu);
            Monitor.Log($"[JunimoNote] After A: held={heldAfter?.DisplayName ?? "null"} cursor={Game1.player.CursorSlotItem?.DisplayName ?? "null"} onPage={GetSpecificBundlePage(menu)}", LogLevel.Debug);
        }

        // ===== Update postfix =====

        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                if (specificBundle && !_onDonationPage)
                {
                    _onDonationPage = true;
                    _trackedSlotIndex = 0;

                    // Determine max slot index based on player's actual inventory size
                    int invCount = Game1.player.Items.Count;
                    int slotCount = __instance.inventory?.inventory?.Count ?? 36;
                    _maxSlotIndex = Math.Min(invCount, slotCount) - 1;
                    if (_maxSlotIndex < 0) _maxSlotIndex = 0;

                    Monitor.Log($"[JunimoNote] Entered donation page, maxSlot={_maxSlotIndex}", LogLevel.Debug);
                    SnapToSlot(__instance);
                }
                else if (!specificBundle && _onDonationPage)
                {
                    _onDonationPage = false;
                    _overridingMouse = false;
                    Monitor.Log("[JunimoNote] Left donation page", LogLevel.Debug);
                }

                // Keep currentlySnappedComponent in sync (game may reset it)
                if (_onDonationPage && __instance.inventory?.inventory != null
                    && _trackedSlotIndex < __instance.inventory.inventory.Count)
                {
                    __instance.currentlySnappedComponent = __instance.inventory.inventory[_trackedSlotIndex];
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] Update error: {ex.Message}", LogLevel.Error);
            }
        }

        // ===== Draw prefix/postfix — override GetMouseState during draw =====
        // The game's draw method calls drawMouse(b) which reads getMouseX/Y.
        // By overriding GetMouseState during draw, the game renders its native cursor
        // (same sprite as inventory/chest) at the tracked slot's center position.
        // Touch is unaffected: override is only active during the draw phase.

        private static void Draw_Prefix(JunimoNoteMenu __instance)
        {
            try
            {
                if (!_onDonationPage) return;
                if (__instance.inventory?.inventory == null || _trackedSlotIndex >= __instance.inventory.inventory.Count) return;

                var slot = __instance.inventory.inventory[_trackedSlotIndex];
                var center = slot.bounds.Center;
                float zoom = Game1.options.zoomLevel;
                _overrideRawX = (int)(center.X * zoom);
                _overrideRawY = (int)(center.Y * zoom);
                _overridingMouse = true;
            }
            catch { }
        }

        private static void Draw_Postfix(JunimoNoteMenu __instance, SpriteBatch b)
        {
            try
            {
                // Draw our own cursor — Android suppresses the game's drawMouse on this page.
                // Use tile 44 (snappy/controller cursor) at bounds.Center for consistency
                // with how snapCursorToCurrentSnappedComponent positions the cursor.
                if (_onDonationPage && __instance.inventory?.inventory != null
                    && _trackedSlotIndex < __instance.inventory.inventory.Count)
                {
                    var slot = __instance.inventory.inventory[_trackedSlotIndex];
                    int cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor;
                    b.Draw(
                        Game1.mouseCursors,
                        new Vector2(slot.bounds.Center.X, slot.bounds.Center.Y),
                        Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, cursorTile, 16, 16),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        4f + Game1.dialogueButtonScale / 150f,
                        SpriteEffects.None,
                        1f
                    );
                }
            }
            catch { }
            finally
            {
                _overridingMouse = false;
            }
        }

        // ===== Reflection accessors =====

        private static bool GetSpecificBundlePage(JunimoNoteMenu menu)
        {
            try { return _specificBundlePageField != null && (bool)_specificBundlePageField.GetValue(menu); }
            catch { return false; }
        }

        private static Item GetHeldItem(JunimoNoteMenu menu)
        {
            try { return _heldItemField?.GetValue(menu) as Item; }
            catch { return null; }
        }

        private static List<ClickableTextureComponent> GetIngredientSlots(JunimoNoteMenu menu)
        {
            try { return _ingredientSlotsField?.GetValue(menu) as List<ClickableTextureComponent>; }
            catch { return null; }
        }

        private static List<ClickableTextureComponent> GetIngredientList(JunimoNoteMenu menu)
        {
            try { return _ingredientListField?.GetValue(menu) as List<ClickableTextureComponent>; }
            catch { return null; }
        }
    }
}
