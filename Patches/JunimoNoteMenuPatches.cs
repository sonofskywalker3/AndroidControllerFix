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
    /// DIAGNOSTIC BUILD — pure observation, no interception.
    /// Logs everything about JunimoNoteMenu to understand the bundle donation page.
    /// Does NOT block any input, redirect any clicks, or override any state.
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;

        // Cached reflection
        private static FieldInfo _specificBundlePageField;
        private static FieldInfo _ingredientSlotsField;
        private static FieldInfo _ingredientListField;
        private static FieldInfo _heldItemField;
        private static FieldInfo _purchaseButtonField;
        private static FieldInfo _backButtonField;

        // State tracking for diagnostics
        private static bool _onBundlePage = false;
        private static bool _dumpedComponents = false;
        private static bool _drawPostfixLoggedOnce = false;
        private static int _lastLoggedTick = -1;

        // Track whether receiveLeftClick was triggered by A-press or touch
        private static bool _aPressedThisTick = false;
        private static int _aPressTick = -1;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection
            _specificBundlePageField = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
            _ingredientSlotsField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientSlots");
            _ingredientListField = AccessTools.Field(typeof(JunimoNoteMenu), "ingredientList");
            _heldItemField = AccessTools.Field(typeof(JunimoNoteMenu), "heldItem");
            _purchaseButtonField = AccessTools.Field(typeof(JunimoNoteMenu), "purchaseButton");
            _backButtonField = AccessTools.Field(typeof(JunimoNoteMenu), "backButton");

            Monitor.Log($"[JunimoNote DIAG] Reflection: specificBundlePage={_specificBundlePageField != null}, ingredientSlots={_ingredientSlotsField != null}, ingredientList={_ingredientListField != null}, heldItem={_heldItemField != null}, purchaseButton={_purchaseButtonField != null}, backButton={_backButtonField != null}", LogLevel.Debug);

            // Patch receiveGamePadButton — OBSERVE ONLY (always return true)
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            // Patch receiveLeftClick — OBSERVE ONLY via prefix + postfix
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveLeftClick_Prefix)),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveLeftClick_Postfix))
            );

            // Patch update — detect page transitions, dump components, log state
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            // Patch draw — verify draw postfix fires, try rendering diagnostic overlay
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "draw", new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Draw_Postfix))
            );

            Monitor.Log("JunimoNoteMenu DIAGNOSTIC patches applied.", LogLevel.Trace);
        }

        public static void OnMenuChanged()
        {
            _onBundlePage = false;
            _dumpedComponents = false;
            _drawPostfixLoggedOnce = false;
            _aPressedThisTick = false;
        }

        /// <summary>
        /// DIAGNOSTIC: Log every gamepad button press. Does NOT block anything.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Buttons b)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);
                var snapped = __instance.currentlySnappedComponent;
                var invSnapped = __instance.inventory?.currentlySnappedComponent;
                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();

                string snappedInfo = snapped != null
                    ? $"id={snapped.myID} name='{snapped.name}' bounds={snapped.bounds}"
                    : "null";
                string invSnappedInfo = invSnapped != null
                    ? $"id={invSnapped.myID} name='{invSnapped.name}'"
                    : "null";

                Monitor.Log($"[JunimoNote DIAG] receiveGamePadButton({b}) specificBundle={specificBundle} snapped=[{snappedInfo}] inv.snapped=[{invSnappedInfo}] mouse=({mouseX},{mouseY})", LogLevel.Debug);

                if (b == Buttons.A)
                {
                    _aPressedThisTick = true;
                    _aPressTick = Game1.ticks;

                    // Log heldItem state
                    var heldItem = GetHeldItem(__instance);
                    Monitor.Log($"[JunimoNote DIAG] A-press: heldItem={heldItem?.DisplayName ?? "null"} CursorSlotItem={Game1.player.CursorSlotItem?.DisplayName ?? "null"}", LogLevel.Debug);

                    // Log what's at the current mouse position
                    LogComponentAtPosition(__instance, mouseX, mouseY, "mouse");

                    // Log what's at snapped component position
                    if (snapped != null)
                    {
                        var c = snapped.bounds.Center;
                        LogComponentAtPosition(__instance, c.X, c.Y, "snapped-center");
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] receiveGamePadButton error: {ex.Message}", LogLevel.Debug);
            }

            return true; // ALWAYS pass through — pure diagnostic
        }

        /// <summary>
        /// DIAGNOSTIC: Log receiveLeftClick BEFORE the method runs.
        /// Does NOT modify x or y. Void return = cannot block.
        /// </summary>
        private static void ReceiveLeftClick_Prefix(JunimoNoteMenu __instance, int x, int y)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);
                if (!specificBundle) return; // Only log on bundle page

                bool fromGamepad = _aPressedThisTick || (Game1.ticks - _aPressTick <= 2);
                var heldItem = GetHeldItem(__instance);

                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick PRE: ({x},{y}) fromGamepad={fromGamepad} heldItem={heldItem?.DisplayName ?? "null"} CursorSlot={Game1.player.CursorSlotItem?.DisplayName ?? "null"} tick={Game1.ticks}", LogLevel.Debug);

                // What component is at the click position?
                LogComponentAtPosition(__instance, x, y, "click");

                // What's in the inventory at these coordinates?
                if (__instance.inventory?.inventory != null)
                {
                    foreach (var slot in __instance.inventory.inventory)
                    {
                        if (slot.bounds.Contains(x, y))
                        {
                            int idx = __instance.inventory.inventory.IndexOf(slot);
                            Item item = (idx >= 0 && idx < Game1.player.Items.Count) ? Game1.player.Items[idx] : null;
                            Monitor.Log($"[JunimoNote DIAG]   Click hits inv slot {idx} (id={slot.myID}): {item?.DisplayName ?? "empty"} x{item?.Stack ?? 0}", LogLevel.Debug);
                            break;
                        }
                    }
                }

                // Check ingredient slots
                var ingredientSlots = GetIngredientSlots(__instance);
                if (ingredientSlots != null)
                {
                    for (int i = 0; i < ingredientSlots.Count; i++)
                    {
                        if (ingredientSlots[i].bounds.Contains(x, y))
                        {
                            Monitor.Log($"[JunimoNote DIAG]   Click hits ingredient slot {i} (id={ingredientSlots[i].myID}) bounds={ingredientSlots[i].bounds}", LogLevel.Debug);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick PRE error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// DIAGNOSTIC: Log receiveLeftClick AFTER the method runs.
        /// Check if heldItem changed (donation happened).
        /// </summary>
        private static void ReceiveLeftClick_Postfix(JunimoNoteMenu __instance, int x, int y)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);
                if (!specificBundle) return;

                var heldItem = GetHeldItem(__instance);
                var snapped = __instance.currentlySnappedComponent;
                string snappedInfo = snapped != null ? $"id={snapped.myID}" : "null";

                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick POST: ({x},{y}) heldItem={heldItem?.DisplayName ?? "null"} CursorSlot={Game1.player.CursorSlotItem?.DisplayName ?? "null"} snapped=[{snappedInfo}]", LogLevel.Debug);

                // Check if we're still on the specific bundle page (donation might have closed it)
                bool stillSpecific = GetSpecificBundlePage(__instance);
                if (!stillSpecific && specificBundle)
                    Monitor.Log($"[JunimoNote DIAG]   specificBundlePage changed to FALSE after click — donation may have succeeded!", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick POST error: {ex.Message}", LogLevel.Debug);
            }

            _aPressedThisTick = false;
        }

        /// <summary>
        /// DIAGNOSTIC: Detect page transitions, dump component layout once.
        /// Log state every 60 ticks (1/sec) to track drift without flooding.
        /// </summary>
        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                if (specificBundle && !_onBundlePage)
                {
                    _onBundlePage = true;
                    _dumpedComponents = false;
                    _drawPostfixLoggedOnce = false;
                    Monitor.Log("[JunimoNote DIAG] === Entered bundle donation page ===", LogLevel.Debug);
                }
                else if (!specificBundle && _onBundlePage)
                {
                    _onBundlePage = false;
                    Monitor.Log("[JunimoNote DIAG] === Left bundle donation page ===", LogLevel.Debug);
                }

                // Dump all components once when entering the bundle page
                if (specificBundle && !_dumpedComponents)
                {
                    _dumpedComponents = true;
                    DumpAllComponents(__instance);
                }

                // Periodic state log (every 60 ticks = ~1/sec)
                if (specificBundle && Game1.ticks - _lastLoggedTick >= 60)
                {
                    _lastLoggedTick = Game1.ticks;
                    var snapped = __instance.currentlySnappedComponent;
                    var invSnapped = __instance.inventory?.currentlySnappedComponent;
                    int mx = Game1.getMouseX(), my = Game1.getMouseY();
                    Monitor.Log($"[JunimoNote DIAG] TICK {Game1.ticks}: snapped={FormatComp(snapped)} inv.snapped={FormatComp(invSnapped)} mouse=({mx},{my})", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] Update_Postfix error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// DIAGNOSTIC: Verify draw postfix fires. Draw test rectangles to confirm rendering works.
        /// </summary>
        private static void Draw_Postfix(JunimoNoteMenu __instance, SpriteBatch b)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                if (specificBundle && !_drawPostfixLoggedOnce)
                {
                    _drawPostfixLoggedOnce = true;
                    Monitor.Log($"[JunimoNote DIAG] Draw_Postfix FIRED on bundle page. staminaRect={Game1.staminaRect != null}", LogLevel.Debug);
                }

                if (!specificBundle)
                    return;

                // Draw a bright red test square at a fixed position to verify rendering works at all
                Texture2D pixel = Game1.staminaRect;
                if (pixel != null)
                {
                    // Test square at top-left — should always be visible
                    b.Draw(pixel, new Rectangle(10, 10, 30, 30), Color.Red);

                    // Test square at center of the menu
                    int cx = __instance.xPositionOnScreen + __instance.width / 2;
                    int cy = __instance.yPositionOnScreen + __instance.height / 2;
                    b.Draw(pixel, new Rectangle(cx - 15, cy - 15, 30, 30), Color.Blue);

                    // Draw around each inventory slot to see where they actually are on screen
                    if (__instance.inventory?.inventory != null)
                    {
                        for (int i = 0; i < Math.Min(12, __instance.inventory.inventory.Count); i++)
                        {
                            var slot = __instance.inventory.inventory[i];
                            var bnd = slot.bounds;
                            Color c = (i == 0) ? Color.Lime : new Color(0, 255, 0, 60);
                            int bw = (i == 0) ? 3 : 1;
                            // Top
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y, bnd.Width, bw), c);
                            // Bottom
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y + bnd.Height - bw, bnd.Width, bw), c);
                            // Left
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y, bw, bnd.Height), c);
                            // Right
                            b.Draw(pixel, new Rectangle(bnd.X + bnd.Width - bw, bnd.Y, bw, bnd.Height), c);
                        }
                    }

                    // Draw around ingredient slots
                    var ingredientSlots = GetIngredientSlots(__instance);
                    if (ingredientSlots != null)
                    {
                        foreach (var slot in ingredientSlots)
                        {
                            var bnd = slot.bounds;
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y, bnd.Width, 2), Color.Orange);
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y + bnd.Height - 2, bnd.Width, 2), Color.Orange);
                            b.Draw(pixel, new Rectangle(bnd.X, bnd.Y, 2, bnd.Height), Color.Orange);
                            b.Draw(pixel, new Rectangle(bnd.X + bnd.Width - 2, bnd.Y, 2, bnd.Height), Color.Orange);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_drawPostfixLoggedOnce)
                {
                    _drawPostfixLoggedOnce = true;
                    Monitor.Log($"[JunimoNote DIAG] Draw_Postfix ERROR: {ex.Message}\n{ex.StackTrace}", LogLevel.Debug);
                }
            }
        }

        // ===== Helper methods =====

        private static void DumpAllComponents(JunimoNoteMenu menu)
        {
            Monitor.Log("[JunimoNote DIAG] === COMPONENT DUMP ===", LogLevel.Debug);
            Monitor.Log($"[JunimoNote DIAG] Menu position: ({menu.xPositionOnScreen},{menu.yPositionOnScreen}) size: {menu.width}x{menu.height}", LogLevel.Debug);

            // Inventory slots
            if (menu.inventory?.inventory != null)
            {
                Monitor.Log($"[JunimoNote DIAG] Inventory slots: {menu.inventory.inventory.Count}", LogLevel.Debug);
                for (int i = 0; i < menu.inventory.inventory.Count; i++)
                {
                    var s = menu.inventory.inventory[i];
                    Item item = (i < Game1.player.Items.Count) ? Game1.player.Items[i] : null;
                    Monitor.Log($"[JunimoNote DIAG]   InvSlot[{i}] id={s.myID} bounds={s.bounds} L={s.leftNeighborID} R={s.rightNeighborID} U={s.upNeighborID} D={s.downNeighborID} item={item?.DisplayName ?? "empty"}", LogLevel.Debug);
                }
            }

            // Ingredient slots
            var ingredientSlots = GetIngredientSlots(menu);
            if (ingredientSlots != null)
            {
                Monitor.Log($"[JunimoNote DIAG] Ingredient slots: {ingredientSlots.Count}", LogLevel.Debug);
                for (int i = 0; i < ingredientSlots.Count; i++)
                {
                    var s = ingredientSlots[i];
                    Monitor.Log($"[JunimoNote DIAG]   IngSlot[{i}] id={s.myID} name='{s.name}' bounds={s.bounds} L={s.leftNeighborID} R={s.rightNeighborID} U={s.upNeighborID} D={s.downNeighborID}", LogLevel.Debug);
                }
            }

            // Ingredient list (required items)
            var ingredientList = GetIngredientList(menu);
            if (ingredientList != null)
            {
                Monitor.Log($"[JunimoNote DIAG] Ingredient list (required items): {ingredientList.Count}", LogLevel.Debug);
                for (int i = 0; i < ingredientList.Count; i++)
                {
                    var s = ingredientList[i];
                    Monitor.Log($"[JunimoNote DIAG]   IngList[{i}] id={s.myID} name='{s.name}' bounds={s.bounds} immutable={s.fullyImmutable}", LogLevel.Debug);
                }
            }

            // Purchase button
            var purchaseButton = GetPurchaseButton(menu);
            if (purchaseButton != null)
                Monitor.Log($"[JunimoNote DIAG] Purchase button: id={purchaseButton.myID} bounds={purchaseButton.bounds}", LogLevel.Debug);

            // Back button
            var backButton = GetBackButton(menu);
            if (backButton != null)
                Monitor.Log($"[JunimoNote DIAG] Back button: id={backButton.myID} bounds={backButton.bounds}", LogLevel.Debug);

            // allClickableComponents
            if (menu.allClickableComponents != null)
            {
                Monitor.Log($"[JunimoNote DIAG] allClickableComponents total: {menu.allClickableComponents.Count}", LogLevel.Debug);
                int nonInvCount = 0;
                foreach (var comp in menu.allClickableComponents)
                {
                    // Skip inventory slots (already logged above)
                    bool isInvSlot = false;
                    if (menu.inventory?.inventory != null)
                    {
                        foreach (var s in menu.inventory.inventory)
                        {
                            if (s == comp) { isInvSlot = true; break; }
                        }
                    }
                    if (!isInvSlot)
                    {
                        nonInvCount++;
                        Monitor.Log($"[JunimoNote DIAG]   Other[{nonInvCount}] id={comp.myID} name='{comp.name}' bounds={comp.bounds} immutable={comp.fullyImmutable} L={comp.leftNeighborID} R={comp.rightNeighborID} U={comp.upNeighborID} D={comp.downNeighborID}", LogLevel.Debug);
                    }
                }
            }

            // Current state
            var snapped = menu.currentlySnappedComponent;
            Monitor.Log($"[JunimoNote DIAG] Initial snapped: {FormatComp(snapped)}", LogLevel.Debug);
            Monitor.Log($"[JunimoNote DIAG] getMouseX={Game1.getMouseX()} getMouseY={Game1.getMouseY()}", LogLevel.Debug);
            Monitor.Log($"[JunimoNote DIAG] zoomLevel={Game1.options.zoomLevel} uiScale={Game1.options.uiScale}", LogLevel.Debug);
            Monitor.Log("[JunimoNote DIAG] === END DUMP ===", LogLevel.Debug);
        }

        private static void LogComponentAtPosition(JunimoNoteMenu menu, int x, int y, string label)
        {
            if (menu.allClickableComponents != null)
            {
                foreach (var comp in menu.allClickableComponents)
                {
                    if (comp.bounds.Contains(x, y))
                    {
                        Monitor.Log($"[JunimoNote DIAG]   {label} ({x},{y}) hits: id={comp.myID} name='{comp.name}' bounds={comp.bounds} immutable={comp.fullyImmutable}", LogLevel.Debug);
                        return;
                    }
                }
            }
            Monitor.Log($"[JunimoNote DIAG]   {label} ({x},{y}) hits: NOTHING (no component contains this point)", LogLevel.Debug);
        }

        private static string FormatComp(ClickableComponent c)
        {
            if (c == null) return "null";
            return $"id={c.myID} name='{c.name}' bounds={c.bounds}";
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

        private static ClickableTextureComponent GetPurchaseButton(JunimoNoteMenu menu)
        {
            try { return _purchaseButtonField?.GetValue(menu) as ClickableTextureComponent; }
            catch { return null; }
        }

        private static ClickableTextureComponent GetBackButton(JunimoNoteMenu menu)
        {
            try { return _backButtonField?.GetValue(menu) as ClickableTextureComponent; }
            catch { return null; }
        }
    }
}
