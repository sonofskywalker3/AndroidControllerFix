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
        private static FieldInfo _bundlesField;

        // Overview page state
        private static bool _onOverviewPage;

        // Donation page state
        private static bool _onDonationPage;
        private static int _trackedSlotIndex;
        private static int _maxSlotIndex = 23;
        private const int INV_COLUMNS = 6;

        // GetMouseState override — active during A-press receiveLeftClick AND during draw
        private static bool _overridingMouse;
        private static int _overrideRawX, _overrideRawY;

        // Guard against stale touch-sim click when A opens the donation page
        private static int _enteredPageTick = -100;
        private static bool _weInitiatedClick;

        // Diagnostic: overview page component dump (one-time per page entry)
        private static bool _dumpedOverviewComponents;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection
            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
            _heldItemField = AccessTools.Field(typeof(JunimoNoteMenu), "heldItem");
            _ingredientSlotsField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientSlots");
            _ingredientListField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientList");
            _bundlesField = AccessTools.Field(typeof(JunimoNoteMenu), "bundles");

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

            // receiveLeftClick — block stale touch-sim click when A opens the page
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveLeftClick_Prefix))
            );

            // receiveGamePadButton — handle navigation and A press
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix)),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Postfix))
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
            _onOverviewPage = false;
            _overridingMouse = false;
            _dumpedOverviewComponents = false;
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

        // ===== receiveLeftClick prefix — block stale touch-sim clicks =====
        // When A opens the donation page, Android fires a touch-sim receiveLeftClick.
        // Our SnapToSlot moved the mouse to slot 0's center, so the stale click would
        // interact with slot 0 (causing item swaps/flicker). Block clicks for a few
        // ticks after page entry unless we initiated them via HandleAPress.

        private static bool ReceiveLeftClick_Prefix(JunimoNoteMenu __instance, int x, int y)
        {
            if (!_onDonationPage) return true;
            if (_weInitiatedClick) return true; // our A-press click — allow

            // Block clicks within 3 ticks of page entry
            if (Game1.ticks - _enteredPageTick <= 3)
            {
                Monitor.Log($"[JunimoNote] Blocked stale click ({x},{y}) {Game1.ticks - _enteredPageTick} ticks after entry", LogLevel.Debug);
                return false;
            }

            return true; // normal touch clicks after cooldown — allow
        }

        // ===== receiveGamePadButton prefix =====

        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                if (!GetSpecificBundlePage(__instance))
                {
                    if (!_onOverviewPage) return true;

                    // Overview page — handle all navigation + A ourselves
                    switch (b)
                    {
                        case Buttons.LeftThumbstickRight:
                        case Buttons.DPadRight:
                            NavigateOverview(__instance, "right");
                            return false;
                        case Buttons.LeftThumbstickLeft:
                        case Buttons.DPadLeft:
                            NavigateOverview(__instance, "left");
                            return false;
                        case Buttons.LeftThumbstickDown:
                        case Buttons.DPadDown:
                            NavigateOverview(__instance, "down");
                            return false;
                        case Buttons.LeftThumbstickUp:
                        case Buttons.DPadUp:
                            NavigateOverview(__instance, "up");
                            return false;
                        case Buttons.A:
                            HandleOverviewAPress(__instance);
                            return false;
                        default:
                            return true; // B closes menu, etc.
                    }
                }

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

        // ===== receiveGamePadButton postfix (diagnostic) =====

        private static void ReceiveGamePadButton_Postfix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                if (GetSpecificBundlePage(__instance)) return; // donation page handled by prefix

                var snapped = __instance.currentlySnappedComponent;
                Monitor.Log($"[JunimoNote:Overview] AFTER  button={b} snapped={FormatComponent(snapped)}", LogLevel.Debug);
            }
            catch { }
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

            _weInitiatedClick = true;
            try
            {
                menu.receiveLeftClick(center.X, center.Y);
            }
            finally
            {
                _weInitiatedClick = false;
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

                // DIAGNOSTIC: One-time dump of overview page components
                if (!specificBundle && !_dumpedOverviewComponents)
                {
                    _dumpedOverviewComponents = true;
                    DumpOverviewComponents(__instance);
                }

                // Overview page entry — populate allClickableComponents and wire neighbors
                if (!specificBundle && !_onOverviewPage)
                {
                    if (InitOverviewNavigation(__instance))
                        _onOverviewPage = true;
                }

                // Keep currentlySnappedComponent alive on overview (game may reset it)
                if (_onOverviewPage && __instance.currentlySnappedComponent == null
                    && __instance.allClickableComponents != null && __instance.allClickableComponents.Count > 0)
                {
                    __instance.currentlySnappedComponent = __instance.allClickableComponents[0];
                    __instance.snapCursorToCurrentSnappedComponent();
                }

                if (specificBundle && !_onDonationPage)
                {
                    _onDonationPage = true;
                    _onOverviewPage = false; // will re-init on return
                    _trackedSlotIndex = 0;
                    _enteredPageTick = Game1.ticks;

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
                    _dumpedOverviewComponents = false; // re-dump when returning to overview
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
                ClickableComponent target = null;

                if (_onDonationPage)
                {
                    if (__instance.inventory?.inventory != null && _trackedSlotIndex < __instance.inventory.inventory.Count)
                        target = __instance.inventory.inventory[_trackedSlotIndex];
                }
                else if (_onOverviewPage)
                {
                    target = __instance.currentlySnappedComponent;
                }

                if (target == null) return;

                var center = target.bounds.Center;
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
                ClickableComponent target = null;

                if (_onDonationPage && __instance.inventory?.inventory != null
                    && _trackedSlotIndex < __instance.inventory.inventory.Count)
                {
                    target = __instance.inventory.inventory[_trackedSlotIndex];
                }
                else if (_onOverviewPage)
                {
                    target = __instance.currentlySnappedComponent;
                }

                if (target != null)
                {
                    int cursorTile = Game1.options.snappyMenus ? 44 : Game1.mouseCursor;
                    b.Draw(
                        Game1.mouseCursors,
                        new Vector2(target.bounds.Center.X, target.bounds.Center.Y),
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

        // ===== Overview page navigation =====

        private static bool InitOverviewNavigation(JunimoNoteMenu menu)
        {
            var bundlesList = _bundlesField?.GetValue(menu) as System.Collections.IList;
            if (bundlesList == null || bundlesList.Count == 0)
                return false;

            var components = new List<ClickableComponent>();
            foreach (var item in bundlesList)
            {
                if (item is ClickableComponent cc)
                    components.Add(cc);
            }

            // Add back button with a unique ID for navigation
            var backButtonField = AccessTools.Field(typeof(JunimoNoteMenu), "backButton");
            var backButton = backButtonField?.GetValue(menu) as ClickableComponent;
            if (backButton != null)
            {
                backButton.myID = 5100;
                components.Add(backButton);
            }

            // Add area next/back buttons if they exist (room switching)
            var areaNext = AccessTools.Field(typeof(JunimoNoteMenu), "areaNextButton")?.GetValue(menu) as ClickableComponent;
            if (areaNext != null)
            {
                if (areaNext.myID < 0) areaNext.myID = 5101;
                components.Add(areaNext);
            }
            var areaBack = AccessTools.Field(typeof(JunimoNoteMenu), "areaBackButton")?.GetValue(menu) as ClickableComponent;
            if (areaBack != null)
            {
                if (areaBack.myID < 0) areaBack.myID = 5102;
                components.Add(areaBack);
            }

            menu.allClickableComponents = components;
            WireNeighborsByPosition(components);

            // Set initial snapped component to first bundle
            menu.currentlySnappedComponent = components[0];
            menu.snapCursorToCurrentSnappedComponent();

            Monitor.Log($"[JunimoNote:Overview] Initialized navigation: {components.Count} components", LogLevel.Debug);
            foreach (var c in components)
            {
                Monitor.Log($"  {FormatComponent(c)}", LogLevel.Debug);
            }

            return true;
        }

        private static void WireNeighborsByPosition(List<ClickableComponent> components)
        {
            foreach (var c in components)
            {
                int cx = c.bounds.Center.X;
                int cy = c.bounds.Center.Y;

                int bestRightId = -1, bestLeftId = -1, bestDownId = -1, bestUpId = -1;
                int bestRightScore = int.MaxValue, bestLeftScore = int.MaxValue;
                int bestDownScore = int.MaxValue, bestUpScore = int.MaxValue;

                foreach (var other in components)
                {
                    if (other == c) continue;
                    int ox = other.bounds.Center.X;
                    int oy = other.bounds.Center.Y;
                    int dx = ox - cx;
                    int dy = oy - cy;

                    // Right: must be to the right, penalize vertical offset 2x
                    if (dx > 0)
                    {
                        int score = dx + Math.Abs(dy) * 2;
                        if (score < bestRightScore) { bestRightScore = score; bestRightId = other.myID; }
                    }
                    // Left: must be to the left
                    if (dx < 0)
                    {
                        int score = -dx + Math.Abs(dy) * 2;
                        if (score < bestLeftScore) { bestLeftScore = score; bestLeftId = other.myID; }
                    }
                    // Down: must be below, penalize horizontal offset 2x
                    if (dy > 0)
                    {
                        int score = dy + Math.Abs(dx) * 2;
                        if (score < bestDownScore) { bestDownScore = score; bestDownId = other.myID; }
                    }
                    // Up: must be above
                    if (dy < 0)
                    {
                        int score = -dy + Math.Abs(dx) * 2;
                        if (score < bestUpScore) { bestUpScore = score; bestUpId = other.myID; }
                    }
                }

                c.rightNeighborID = bestRightId;
                c.leftNeighborID = bestLeftId;
                c.downNeighborID = bestDownId;
                c.upNeighborID = bestUpId;
            }
        }

        private static void NavigateOverview(JunimoNoteMenu menu, string direction)
        {
            var current = menu.currentlySnappedComponent;
            if (current == null || menu.allClickableComponents == null) return;

            int targetId;
            switch (direction)
            {
                case "right": targetId = current.rightNeighborID; break;
                case "left":  targetId = current.leftNeighborID; break;
                case "down":  targetId = current.downNeighborID; break;
                case "up":    targetId = current.upNeighborID; break;
                default: return;
            }

            if (targetId < 0) return; // no neighbor in that direction

            foreach (var c in menu.allClickableComponents)
            {
                if (c.myID == targetId)
                {
                    menu.currentlySnappedComponent = c;
                    menu.snapCursorToCurrentSnappedComponent();
                    Monitor.Log($"[JunimoNote:Overview] Nav {direction}: {FormatComponent(c)}", LogLevel.Debug);
                    return;
                }
            }
        }

        private static void HandleOverviewAPress(JunimoNoteMenu menu)
        {
            var snapped = menu.currentlySnappedComponent;
            if (snapped == null) return;

            var center = snapped.bounds.Center;
            float zoom = Game1.options.zoomLevel;
            _overrideRawX = (int)(center.X * zoom);
            _overrideRawY = (int)(center.Y * zoom);
            _overridingMouse = true;

            Monitor.Log($"[JunimoNote:Overview] A press on {FormatComponent(snapped)}, click ({center.X},{center.Y})", LogLevel.Debug);

            try
            {
                menu.receiveLeftClick(center.X, center.Y);
            }
            finally
            {
                _overridingMouse = false;
            }
        }

        // ===== Diagnostic helpers =====

        private static void DumpOverviewComponents(JunimoNoteMenu menu)
        {
            Monitor.Log("========== JUNIMO NOTE OVERVIEW: COMPONENT DUMP ==========", LogLevel.Debug);
            Monitor.Log($"Menu position: ({menu.xPositionOnScreen},{menu.yPositionOnScreen}) size {menu.width}x{menu.height}", LogLevel.Debug);
            Monitor.Log($"currentlySnappedComponent: {FormatComponent(menu.currentlySnappedComponent)}", LogLevel.Debug);

            if (menu.allClickableComponents != null)
            {
                Monitor.Log($"allClickableComponents count: {menu.allClickableComponents.Count}", LogLevel.Debug);
                for (int i = 0; i < menu.allClickableComponents.Count; i++)
                {
                    var c = menu.allClickableComponents[i];
                    Monitor.Log($"  [{i}] id={c.myID} name='{c.name}' label='{c.label}' " +
                        $"bounds=({c.bounds.X},{c.bounds.Y},{c.bounds.Width},{c.bounds.Height}) " +
                        $"neighbors L={c.leftNeighborID} R={c.rightNeighborID} U={c.upNeighborID} D={c.downNeighborID} " +
                        $"visible={c.visible} type={c.GetType().Name}", LogLevel.Debug);
                }
            }
            else
            {
                Monitor.Log("allClickableComponents is NULL", LogLevel.Debug);
            }

            // Also dump any named component collections via reflection
            var bundleFields = new[] { "bundles", "bundleButtons", "areaNextButton", "areaBackButton", "backButton" };
            foreach (var fieldName in bundleFields)
            {
                var field = AccessTools.Field(typeof(JunimoNoteMenu), fieldName);
                if (field == null) continue;
                var val = field.GetValue(menu);
                if (val is ClickableComponent cc)
                {
                    Monitor.Log($"  Field '{fieldName}': {FormatComponent(cc)}", LogLevel.Debug);
                }
                else if (val is System.Collections.IList list)
                {
                    Monitor.Log($"  Field '{fieldName}': list count={list.Count}", LogLevel.Debug);
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is ClickableComponent item)
                            Monitor.Log($"    [{i}] {FormatComponent(item)}", LogLevel.Debug);
                    }
                }
                else if (val != null)
                {
                    Monitor.Log($"  Field '{fieldName}': {val.GetType().Name} = {val}", LogLevel.Debug);
                }
                else
                {
                    Monitor.Log($"  Field '{fieldName}': null", LogLevel.Debug);
                }
            }

            Monitor.Log("========== END OVERVIEW DUMP ==========", LogLevel.Debug);
        }

        private static string FormatComponent(ClickableComponent c)
        {
            if (c == null) return "null";
            return $"id={c.myID} name='{c.name}' label='{c.label}' " +
                $"bounds=({c.bounds.X},{c.bounds.Y},{c.bounds.Width},{c.bounds.Height}) " +
                $"neighbors L={c.leftNeighborID} R={c.rightNeighborID} U={c.upNeighborID} D={c.downNeighborID}";
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
