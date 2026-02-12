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
    /// Controller support for the Community Center JunimoNoteMenu.
    /// Overview page: populates allClickableComponents from bundles, wires spatial neighbors.
    /// Donation page: manages a tracked cursor over inventory slots and ingredient list.
    /// On A press, overrides GetMouseState so receiveLeftClick reads the tracked position
    /// (Android's receiveLeftClick ignores its x,y params and reads GetMouseState internally).
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;

        // Cached reflection
        private static FieldInfo _specificBundlePageField;
        private static FieldInfo _ingredientListField;
        private static FieldInfo _bundlesField;

        // Overview page state
        private static bool _onOverviewPage;
        private static int _savedOverviewComponentId = -1;

        // Donation page state
        private static bool _onDonationPage;
        private static int _trackedSlotIndex;
        private static int _maxSlotIndex = 23;
        private const int INV_COLUMNS = 6;

        // Ingredient zone state
        private static bool _inIngredientZone;
        private static int _trackedIngredientIndex;
        private static int _lastInventoryRow;
        private static List<List<int>> _ingredientRows; // indices into ingredientList, grouped by visual row

        // GetMouseState override — active during A-press receiveLeftClick AND during draw
        private static bool _overridingMouse;
        private static int _overrideRawX, _overrideRawY;

        // Guard against stale touch-sim click when A opens the donation page
        private static int _enteredPageTick = -100;
        private static bool _weInitiatedClick;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection
            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
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
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            // update — detect page transitions, keep cursor in sync
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            // draw — override GetMouseState during draw for cursor + tooltip rendering
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
            _inIngredientZone = false;
            _ingredientRows = null;
            _savedOverviewComponentId = -1;
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

        private static bool ReceiveLeftClick_Prefix(JunimoNoteMenu __instance, int x, int y)
        {
            if (!_onDonationPage) return true;
            if (_weInitiatedClick) return true;

            if (Game1.ticks - _enteredPageTick <= 3)
                return false;

            return true;
        }

        // ===== receiveGamePadButton prefix =====

        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                if (!GetSpecificBundlePage(__instance))
                {
                    if (!_onOverviewPage) return true;

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
                            return true;
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
                        return false;

                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] receiveGamePadButton error: {ex}", LogLevel.Error);
                return true;
            }
        }

        // ===== Inventory navigation =====

        private static void Navigate(JunimoNoteMenu menu, int dx, int dy)
        {
            if (_inIngredientZone)
            {
                NavigateIngredientZone(menu, dx, dy);
                return;
            }

            int col = _trackedSlotIndex % INV_COLUMNS;
            int row = _trackedSlotIndex / INV_COLUMNS;
            int maxRow = _maxSlotIndex / INV_COLUMNS;

            // Right from rightmost column → enter ingredient zone
            if (dx > 0 && col == INV_COLUMNS - 1)
            {
                EnterIngredientZone(menu, row);
                return;
            }

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

        // ===== Ingredient zone navigation =====

        private static void BuildIngredientRows(JunimoNoteMenu menu)
        {
            _ingredientRows = new List<List<int>>();
            var ingredientList = GetIngredientList(menu);
            if (ingredientList == null || ingredientList.Count == 0) return;

            var yGroups = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < ingredientList.Count; i++)
            {
                int y = ingredientList[i].bounds.Y;
                if (!yGroups.ContainsKey(y))
                    yGroups[y] = new List<int>();
                yGroups[y].Add(i);
            }

            foreach (var group in yGroups.Values)
            {
                group.Sort((a, b) => ingredientList[a].bounds.X.CompareTo(ingredientList[b].bounds.X));
                _ingredientRows.Add(group);
            }
        }

        private static void EnterIngredientZone(JunimoNoteMenu menu, int fromInvRow)
        {
            if (_ingredientRows == null || _ingredientRows.Count == 0) return;

            var ingredientList = GetIngredientList(menu);
            if (ingredientList == null) return;

            _lastInventoryRow = fromInvRow;
            _inIngredientZone = true;

            // Find closest ingredient row by Y to the current inventory row
            int invCenterY = 0;
            int invSlotIndex = fromInvRow * INV_COLUMNS;
            if (menu.inventory?.inventory != null && invSlotIndex < menu.inventory.inventory.Count)
                invCenterY = menu.inventory.inventory[invSlotIndex].bounds.Center.Y;

            int bestRow = 0;
            int bestDist = int.MaxValue;
            for (int r = 0; r < _ingredientRows.Count; r++)
            {
                int idx = _ingredientRows[r][0];
                int rowCenterY = ingredientList[idx].bounds.Center.Y;
                int dist = Math.Abs(rowCenterY - invCenterY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestRow = r;
                }
            }

            _trackedIngredientIndex = _ingredientRows[bestRow][0];
            SnapToIngredient(menu);
        }

        private static void ExitIngredientZone(JunimoNoteMenu menu)
        {
            _inIngredientZone = false;
            int targetRow = _lastInventoryRow;
            int maxRow = _maxSlotIndex / INV_COLUMNS;
            targetRow = Math.Min(targetRow, maxRow);
            _trackedSlotIndex = targetRow * INV_COLUMNS + (INV_COLUMNS - 1);
            _trackedSlotIndex = Math.Min(_trackedSlotIndex, _maxSlotIndex);
            SnapToSlot(menu);
        }

        private static void NavigateIngredientZone(JunimoNoteMenu menu, int dx, int dy)
        {
            if (_ingredientRows == null || _ingredientRows.Count == 0) return;

            int curRow = -1, curPosInRow = -1;
            for (int r = 0; r < _ingredientRows.Count; r++)
            {
                int pos = _ingredientRows[r].IndexOf(_trackedIngredientIndex);
                if (pos >= 0)
                {
                    curRow = r;
                    curPosInRow = pos;
                    break;
                }
            }
            if (curRow < 0) return;

            if (dx != 0)
            {
                int newPos = curPosInRow + dx;
                if (newPos < 0)
                {
                    ExitIngredientZone(menu);
                    return;
                }
                if (newPos >= _ingredientRows[curRow].Count)
                    return;
                _trackedIngredientIndex = _ingredientRows[curRow][newPos];
            }
            else if (dy != 0)
            {
                int newRow = curRow + dy;
                if (newRow < 0 || newRow >= _ingredientRows.Count)
                    return;
                int newPos = Math.Min(curPosInRow, _ingredientRows[newRow].Count - 1);
                _trackedIngredientIndex = _ingredientRows[newRow][newPos];
            }

            SnapToIngredient(menu);
        }

        private static void SnapToIngredient(JunimoNoteMenu menu)
        {
            var ingredientList = GetIngredientList(menu);
            if (ingredientList == null || _trackedIngredientIndex >= ingredientList.Count) return;
            menu.currentlySnappedComponent = ingredientList[_trackedIngredientIndex];
        }

        // ===== A-press handling =====

        private static void HandleAPress(JunimoNoteMenu menu)
        {
            if (_inIngredientZone)
            {
                HandleIngredientAPress(menu);
                return;
            }

            if (menu.inventory?.inventory == null || _trackedSlotIndex >= menu.inventory.inventory.Count)
                return;

            var slot = menu.inventory.inventory[_trackedSlotIndex];
            var center = slot.bounds.Center;

            float zoom = Game1.options.zoomLevel;
            _overrideRawX = (int)(center.X * zoom);
            _overrideRawY = (int)(center.Y * zoom);
            _overridingMouse = true;

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
        }

        private static void HandleIngredientAPress(JunimoNoteMenu menu)
        {
            var ingredientList = GetIngredientList(menu);
            if (ingredientList == null || _trackedIngredientIndex >= ingredientList.Count) return;

            var comp = ingredientList[_trackedIngredientIndex];
            var center = comp.bounds.Center;

            float zoom = Game1.options.zoomLevel;
            _overrideRawX = (int)(center.X * zoom);
            _overrideRawY = (int)(center.Y * zoom);
            _overridingMouse = true;

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
        }

        // ===== Update postfix =====

        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

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
                    _savedOverviewComponentId = __instance.currentlySnappedComponent?.myID ?? -1;
                    _onDonationPage = true;
                    _onOverviewPage = false;
                    _trackedSlotIndex = 0;
                    _inIngredientZone = false;
                    _enteredPageTick = Game1.ticks;

                    int invCount = Game1.player.Items.Count;
                    int slotCount = __instance.inventory?.inventory?.Count ?? 36;
                    _maxSlotIndex = Math.Min(invCount, slotCount) - 1;
                    if (_maxSlotIndex < 0) _maxSlotIndex = 0;

                    BuildIngredientRows(__instance);
                    SnapToSlot(__instance);
                }
                else if (!specificBundle && _onDonationPage)
                {
                    _onDonationPage = false;
                    _overridingMouse = false;
                    _inIngredientZone = false;
                    _ingredientRows = null;
                }

                // Keep currentlySnappedComponent in sync (game may reset it)
                if (_onDonationPage)
                {
                    if (_inIngredientZone)
                    {
                        var ingredientList = GetIngredientList(__instance);
                        if (ingredientList != null && _trackedIngredientIndex < ingredientList.Count)
                            __instance.currentlySnappedComponent = ingredientList[_trackedIngredientIndex];
                    }
                    else if (__instance.inventory?.inventory != null
                        && _trackedSlotIndex < __instance.inventory.inventory.Count)
                    {
                        __instance.currentlySnappedComponent = __instance.inventory.inventory[_trackedSlotIndex];
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote] Update error: {ex.Message}", LogLevel.Error);
            }
        }

        // ===== Draw prefix/postfix =====

        private static void Draw_Prefix(JunimoNoteMenu __instance)
        {
            try
            {
                ClickableComponent target = null;

                if (_onDonationPage)
                {
                    if (_inIngredientZone)
                    {
                        var ingredientList = GetIngredientList(__instance);
                        if (ingredientList != null && _trackedIngredientIndex < ingredientList.Count)
                            target = ingredientList[_trackedIngredientIndex];
                    }
                    else if (__instance.inventory?.inventory != null && _trackedSlotIndex < __instance.inventory.inventory.Count)
                    {
                        target = __instance.inventory.inventory[_trackedSlotIndex];
                    }
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
                ClickableComponent target = null;

                if (_onDonationPage)
                {
                    if (_inIngredientZone)
                    {
                        var ingredientList = GetIngredientList(__instance);
                        if (ingredientList != null && _trackedIngredientIndex < ingredientList.Count)
                            target = ingredientList[_trackedIngredientIndex];
                    }
                    else if (__instance.inventory?.inventory != null
                        && _trackedSlotIndex < __instance.inventory.inventory.Count)
                    {
                        target = __instance.inventory.inventory[_trackedSlotIndex];
                    }
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

                // Draw tooltip when hovering an ingredient
                if (_onDonationPage && _inIngredientZone)
                {
                    var ingredientList = GetIngredientList(__instance);
                    if (ingredientList != null && _trackedIngredientIndex < ingredientList.Count)
                    {
                        var comp = ingredientList[_trackedIngredientIndex];
                        if (!string.IsNullOrEmpty(comp.hoverText))
                        {
                            IClickableMenu.drawHoverText(b, comp.hoverText, Game1.smallFont);
                        }
                    }
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

            ClickableComponent startComponent = components[0];
            if (_savedOverviewComponentId >= 0)
            {
                foreach (var c in components)
                {
                    if (c.myID == _savedOverviewComponentId)
                    {
                        startComponent = c;
                        break;
                    }
                }
                _savedOverviewComponentId = -1;
            }

            menu.currentlySnappedComponent = startComponent;
            menu.snapCursorToCurrentSnappedComponent();

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

                    int adx = Math.Abs(dx);
                    int ady = Math.Abs(dy);

                    if (dx > 0 && adx > ady / 4)
                    {
                        int score = dx + ady * 2;
                        if (score < bestRightScore) { bestRightScore = score; bestRightId = other.myID; }
                    }
                    if (dx < 0 && adx > ady / 4)
                    {
                        int score = adx + ady * 2;
                        if (score < bestLeftScore) { bestLeftScore = score; bestLeftId = other.myID; }
                    }
                    if (dy > 0 && ady > adx / 4)
                    {
                        int score = dy + adx * 2;
                        if (score < bestDownScore) { bestDownScore = score; bestDownId = other.myID; }
                    }
                    if (dy < 0 && ady > adx / 4)
                    {
                        int score = ady + adx * 2;
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

            if (targetId < 0) return;

            foreach (var c in menu.allClickableComponents)
            {
                if (c.myID == targetId)
                {
                    menu.currentlySnappedComponent = c;
                    menu.snapCursorToCurrentSnappedComponent();
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

            try
            {
                menu.receiveLeftClick(center.X, center.Y);
            }
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

        private static List<ClickableTextureComponent> GetIngredientList(JunimoNoteMenu menu)
        {
            try { return _ingredientListField?.GetValue(menu) as List<ClickableTextureComponent>; }
            catch { return null; }
        }
    }
}
