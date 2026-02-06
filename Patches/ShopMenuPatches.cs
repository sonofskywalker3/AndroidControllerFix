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
    /// <summary>Harmony patches for ShopMenu to fix controller purchasing.</summary>
    internal static class ShopMenuPatches
    {
        private static IMonitor Monitor;

        // Cache reflection fields for performance
        private static FieldInfo InvVisibleField;
        private static FieldInfo HoveredItemField;
        private static FieldInfo QuantityToBuyField;

        // Y button sell-one hold tracking
        private static bool _yHeldOnSellTab;
        private static int _yHoldTicks;
        private static Buttons _yHoldRawButton;
        private static Item _yHoldTargetItem;
        private const int SellHoldDelay = 20;   // ~333ms at 60fps before repeat starts
        private const int SellRepeatRate = 3;   // ~50ms at 60fps between repeats

        // LB/RB quantity hold tracking (for hold-to-repeat)
        private static bool _lbHeld;
        private static bool _rbHeld;
        private static int _lbHoldTicks;
        private static int _rbHoldTicks;
        private const int QuantityHoldDelay = 20;   // ~333ms at 60fps before repeat starts
        private const int QuantityRepeatRate = 3;   // ~50ms at 60fps between repeats

        // Track sell tab state for snap navigation fix (5b)
        private static bool _wasOnSellTab = false;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection lookups
            InvVisibleField = AccessTools.Field(typeof(ShopMenu), "inventoryVisible");
            HoveredItemField = AccessTools.Field(typeof(ShopMenu), "hoveredItem");
            QuantityToBuyField = AccessTools.Field(typeof(ShopMenu), "quantityToBuy");

            try
            {
                // PREFIX on receiveGamePadButton — must run BEFORE vanilla to read
                // hoveredItem before the game moves the selection on A press.
                // Returns false for A button to prevent vanilla from also handling it
                // (which causes the cursor to jump to a different item).
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                // Patch update to handle held button for bulk purchasing
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.update)),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(Update_Postfix))
                );

                // Patch draw to show sell price tooltip on sell tab
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.draw), new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ShopMenu_Draw_Postfix))
                );

                Monitor.Log("ShopMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply ShopMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix for receiveGamePadButton — handles A button purchase BEFORE vanilla code.
        /// Returns false for A button to skip vanilla handler (which moves selection).
        /// Returns true for all other buttons to let vanilla handle them normally.
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(ShopMenu __instance, Buttons b)
        {
            try
            {
                Buttons remapped = ButtonRemapper.Remap(b);

                // Only handle A and Y buttons — let everything else pass through
                if (remapped != Buttons.A && remapped != Buttons.Y)
                    return true;

                if (!ModEntry.Config?.EnableShopPurchaseFix ?? true)
                    return true; // Disabled — let vanilla handle it

                // Y button — sell one item on sell tab (hold Y to sell repeatedly)
                if (remapped == Buttons.Y)
                {
                    if (InvVisibleField != null)
                    {
                        bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                        if (inventoryVisible)
                        {
                            Item sellItem = GetSellTabSelectedItem(__instance);
                            if (sellItem != null)
                            {
                                bool sold = SellOneItem(__instance, sellItem);
                                if (sold)
                                {
                                    _yHeldOnSellTab = true;
                                    _yHoldTicks = 0;
                                    _yHoldRawButton = b;
                                    _yHoldTargetItem = sellItem;
                                }
                                return false; // Block vanilla Y when cursor is on an item
                            }
                        }
                    }
                    return true; // Not on sell tab or no item — let vanilla handle Y (tab switching)
                }

                // Check sell mode via inventoryVisible field.
                // inventoryVisible=False on buy tab, True on sell tab.
                if (InvVisibleField != null)
                {
                    bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                    if (inventoryVisible)
                    {
                        // Sell tab — A button sells the full stack (console behavior)
                        // Use snap navigation to find selected item (hoveredItem is not set
                        // on the sell tab with controller — performHoverAction uses mouse pos)
                        Item sellItem = GetSellTabSelectedItem(__instance);
                        if (sellItem == null)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("Sell tab: no item at snapped slot, passing to vanilla", LogLevel.Trace);
                            return true;
                        }

                        int sellPrice;
                        if (sellItem is StardewValley.Object obj)
                            sellPrice = obj.sellToStorePrice();
                        else
                        {
                            int sp = sellItem.salePrice();
                            sellPrice = sp > 0 ? sp / 2 : -1;
                        }

                        if (sellPrice <= 0)
                        {
                            Game1.playSound("cancel");
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Sell tab: {sellItem.DisplayName} cannot be sold (price={sellPrice})", LogLevel.Debug);
                            return false;
                        }

                        int stack = sellItem.Stack;
                        int totalPrice = sellPrice * stack;

                        // Credit player with gold (selling always gives gold regardless of shop currency)
                        Game1.player.Money += totalPrice;

                        // Remove item from player inventory
                        int idx = Game1.player.Items.IndexOf(sellItem);
                        if (idx >= 0)
                        {
                            Game1.player.Items[idx] = null;
                        }

                        // Clear hovered item to avoid stale reference
                        HoveredItemField?.SetValue(__instance, null);

                        Game1.playSound("purchaseClick");
                        Monitor.Log($"Sold {stack}x {sellItem.DisplayName} for {totalPrice}g ({sellPrice}g each)", LogLevel.Info);
                        return false;
                    }
                }

                // Read hoveredItem BEFORE vanilla code can change it
                ISalable selectedItem = HoveredItemField?.GetValue(__instance) as ISalable;

                if (selectedItem == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("No hoveredItem — passing A to vanilla", LogLevel.Trace);
                    return true;
                }

                // Verify it's actually a for-sale item
                if (!__instance.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"No price info for {selectedItem.DisplayName} — passing A to vanilla", LogLevel.Trace);
                    return true;
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Selected item: {selectedItem.DisplayName}", LogLevel.Debug);

                int unitPrice = priceAndStock.Price;
                int stock = priceAndStock.Stock;
                int playerMoney = ShopMenu.getPlayerCurrencyAmount(Game1.player, __instance.currency);
                string tradeItem = priceAndStock.TradeItem;
                int tradeItemCost = priceAndStock.TradeItemCount ?? 0;
                int totalTradeItems = 0;

                // Get the quantity to buy (set by LT/RT)
                int quantity = 1;
                if (QuantityToBuyField != null)
                {
                    quantity = Math.Max(1, (int)QuantityToBuyField.GetValue(__instance));
                }

                // Limit to available stock
                if (stock != int.MaxValue && stock > 0)
                {
                    quantity = Math.Min(quantity, stock);
                }

                int totalCost = unitPrice * quantity;

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Item: {selectedItem.DisplayName}, Unit price: {unitPrice}, Quantity: {quantity}, Total: {totalCost}, Player has: {playerMoney}", LogLevel.Debug);

                // Limit to what player can afford
                if (playerMoney < totalCost)
                {
                    int affordableQty = unitPrice > 0 ? playerMoney / unitPrice : 0;
                    if (affordableQty <= 0)
                    {
                        Game1.playSound("cancel");
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("Cannot afford any", LogLevel.Debug);
                        return false; // Block vanilla A handler
                    }
                    quantity = affordableQty;
                    totalCost = unitPrice * quantity;
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Reduced quantity to {quantity} (affordable)", LogLevel.Debug);
                }

                // Limit quantity by available trade items (Desert Trader, etc.)
                if (!string.IsNullOrEmpty(tradeItem) && tradeItemCost > 0)
                {
                    int playerTradeItems = 0;
                    foreach (Item invItem in Game1.player.Items)
                    {
                        if (invItem != null && (invItem.QualifiedItemId == tradeItem || invItem.ItemId == tradeItem))
                            playerTradeItems += invItem.Stack;
                    }

                    int maxByTradeItems = playerTradeItems / tradeItemCost;
                    if (maxByTradeItems <= 0)
                    {
                        Game1.playSound("cancel");
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Not enough trade items ({tradeItem}): need {tradeItemCost}, have {playerTradeItems}", LogLevel.Debug);
                        return false;
                    }

                    if (quantity > maxByTradeItems)
                    {
                        quantity = maxByTradeItems;
                        totalCost = unitPrice * quantity;
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Reduced quantity to {quantity} (limited by trade items)", LogLevel.Debug);
                    }

                    totalTradeItems = tradeItemCost * quantity;
                }

                // Check inventory space
                if (selectedItem is Item item && !Game1.player.couldInventoryAcceptThisItem(item))
                {
                    Game1.playSound("cancel");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("Inventory full", LogLevel.Debug);
                    return false; // Block vanilla A handler
                }

                // Deduct money
                ShopMenu.chargePlayer(Game1.player, __instance.currency, totalCost);

                // Consume trade items if required
                if (totalTradeItems > 0)
                {
                    int toConsume = totalTradeItems;
                    for (int i = 0; i < Game1.player.Items.Count && toConsume > 0; i++)
                    {
                        Item invItem = Game1.player.Items[i];
                        if (invItem != null && (invItem.QualifiedItemId == tradeItem || invItem.ItemId == tradeItem))
                        {
                            if (invItem.Stack <= toConsume)
                            {
                                toConsume -= invItem.Stack;
                                Game1.player.Items[i] = null;
                            }
                            else
                            {
                                invItem.Stack -= toConsume;
                                toConsume = 0;
                            }
                        }
                    }
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Consumed {totalTradeItems}x {tradeItem}", LogLevel.Debug);
                }

                // Call actionWhenPurchased — handles recipes, tool upgrades, trash can upgrades, etc.
                string shopId = __instance.ShopId;
                bool handled = selectedItem.actionWhenPurchased(shopId);

                if (handled)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"actionWhenPurchased handled {selectedItem.DisplayName} (shopId={shopId})", LogLevel.Debug);
                }
                else if (selectedItem is Item purchaseItem)
                {
                    // Item wasn't handled by special logic — add to inventory normally
                    var newItem = purchaseItem.getOne();
                    newItem.Stack = quantity;
                    if (!Game1.player.addItemToInventoryBool(newItem))
                    {
                        // Inventory full — refund money and trade items
                        ShopMenu.chargePlayer(Game1.player, __instance.currency, -totalCost);
                        if (totalTradeItems > 0)
                        {
                            var refundItem = ItemRegistry.Create(tradeItem, totalTradeItems);
                            Game1.player.addItemToInventoryBool(refundItem);
                        }
                        Game1.playSound("cancel");
                        Monitor.Log($"Inventory full — refunded {totalCost} money" + (totalTradeItems > 0 ? $" and {totalTradeItems}x {tradeItem}" : ""), LogLevel.Warn);
                        return false;
                    }
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Added {quantity}x {newItem.DisplayName} to inventory", LogLevel.Debug);
                }

                // Update stock if limited
                if (stock != int.MaxValue && stock > 0)
                {
                    int remaining = stock - quantity;
                    if (remaining <= 0)
                    {
                        __instance.forSale.Remove(selectedItem);
                        __instance.itemPriceAndStock.Remove(selectedItem);
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("Removed depleted item from shop", LogLevel.Debug);
                    }
                    else
                    {
                        var newStockInfo = new ItemStockInformation(
                            priceAndStock.Price,
                            remaining,
                            priceAndStock.TradeItem,
                            priceAndStock.TradeItemCount
                        );
                        __instance.itemPriceAndStock[selectedItem] = newStockInfo;
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Updated stock: {stock} -> {remaining}", LogLevel.Debug);
                    }
                }

                // Reset quantity selector to 1 after purchase
                if (QuantityToBuyField != null)
                {
                    QuantityToBuyField.SetValue(__instance, 1);
                }

                Game1.playSound("purchaseClick");
                Monitor.Log($"Purchase complete! Bought {quantity}x {selectedItem.DisplayName}", LogLevel.Info);

                return false; // Skip vanilla A handler — prevents cursor jump
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in shop purchase prefix: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log(ex.StackTrace, LogLevel.Debug);
                return true; // On error, let vanilla handle it
            }
        }

        /// <summary>
        /// Get the inventory item at the currently snapped component on the sell tab.
        /// On Android, hoveredItem is not set for controller users on the sell tab because
        /// performHoverAction uses mouse position which doesn't track snap navigation.
        /// Instead, we find the snapped component in the inventory slot list directly.
        /// </summary>
        private static Item GetSellTabSelectedItem(ShopMenu shop)
        {
            var snapped = shop.currentlySnappedComponent;
            if (snapped == null) return null;

            int slotIndex = shop.inventory.inventory.IndexOf(snapped);
            if (slotIndex < 0 || slotIndex >= shop.inventory.actualInventory.Count) return null;

            return shop.inventory.actualInventory[slotIndex];
        }

        /// <summary>Sell one unit of the given item. Returns true if sold successfully.</summary>
        private static bool SellOneItem(ShopMenu shop, Item sellItem)
        {
            int sellPrice;
            if (sellItem is StardewValley.Object obj)
                sellPrice = obj.sellToStorePrice();
            else
            {
                int sp = sellItem.salePrice();
                sellPrice = sp > 0 ? sp / 2 : -1;
            }

            if (sellPrice <= 0)
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Sell tab: {sellItem.DisplayName} cannot be sold (price={sellPrice})", LogLevel.Debug);
                return false;
            }

            Game1.player.Money += sellPrice;

            if (sellItem.Stack > 1)
            {
                sellItem.Stack -= 1;
                Monitor.Log($"Sold 1x {sellItem.DisplayName} for {sellPrice}g ({sellItem.Stack} remaining)", LogLevel.Info);
            }
            else
            {
                // Last item in stack — remove from inventory
                int idx = Game1.player.Items.IndexOf(sellItem);
                if (idx >= 0)
                    Game1.player.Items[idx] = null;
                HoveredItemField?.SetValue(shop, null);
                Monitor.Log($"Sold last {sellItem.DisplayName} for {sellPrice}g", LogLevel.Info);
            }

            Game1.playSound("purchaseClick");
            return true;
        }

        /// <summary>
        /// Adjust the buy quantity by the given delta, respecting all limits.
        /// Called from ModEntry for initial LB/RB press, and from Update_Postfix for hold-to-repeat.
        /// </summary>
        public static void AdjustQuantity(ShopMenu shop, int delta)
        {
            try
            {
                if (QuantityToBuyField == null || InvVisibleField == null)
                    return;

                // Don't adjust quantity when on sell tab
                if ((bool)InvVisibleField.GetValue(shop))
                    return;

                int currentQuantity = (int)QuantityToBuyField.GetValue(shop);

                // Calculate max quantity based on selected item
                int maxQuantity = GetMaxBuyQuantity(shop);

                int newQuantity = Math.Max(1, Math.Min(maxQuantity, currentQuantity + delta));
                if (newQuantity != currentQuantity)
                {
                    QuantityToBuyField.SetValue(shop, newQuantity);
                    Game1.playSound("smallSelect");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Shop quantity: {currentQuantity} -> {newQuantity} (max: {maxQuantity})", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error adjusting shop quantity: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Get the maximum quantity that can be purchased for the currently selected item.
        /// Considers: stock, player money, trade items, and item stack size.
        /// </summary>
        private static int GetMaxBuyQuantity(ShopMenu shop)
        {
            try
            {
                // Find the currently selected item via hoveredItem
                ISalable selectedItem = HoveredItemField?.GetValue(shop) as ISalable;

                if (selectedItem == null || !shop.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                    return 999; // Default max if we can't determine

                int unitPrice = priceAndStock.Price;
                int stock = priceAndStock.Stock;
                int playerMoney = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency);
                string tradeItem = priceAndStock.TradeItem;
                int tradeItemCost = priceAndStock.TradeItemCount ?? 0;

                // Max limited by stock
                int maxByStock = stock == int.MaxValue ? 999 : stock;

                // Max limited by money
                int maxByMoney = unitPrice > 0 ? playerMoney / unitPrice : 999;

                // Max limited by trade items (Desert Trader, etc.)
                int maxByTradeItems = 999;
                if (!string.IsNullOrEmpty(tradeItem) && tradeItemCost > 0)
                {
                    int playerTradeItems = 0;
                    foreach (Item invItem in Game1.player.Items)
                    {
                        if (invItem != null && (invItem.QualifiedItemId == tradeItem || invItem.ItemId == tradeItem))
                            playerTradeItems += invItem.Stack;
                    }
                    maxByTradeItems = playerTradeItems / tradeItemCost;
                }

                // Max limited by item stack size
                int maxByStackSize = selectedItem.maximumStackSize();
                if (maxByStackSize <= 0) maxByStackSize = 999;

                return Math.Max(1, Math.Min(Math.Min(Math.Min(maxByStock, maxByMoney), maxByTradeItems), maxByStackSize));
            }
            catch
            {
                return 999; // Default max on error
            }
        }

        /// <summary>Start tracking LB hold for repeat quantity adjustment.</summary>
        public static void StartLBHold()
        {
            _lbHeld = true;
            _lbHoldTicks = 0;
        }

        /// <summary>Start tracking RB hold for repeat quantity adjustment.</summary>
        public static void StartRBHold()
        {
            _rbHeld = true;
            _rbHoldTicks = 0;
        }

        /// <summary>Postfix for update to handle held A button for continuous purchasing.</summary>
        private static void Update_Postfix(ShopMenu __instance, GameTime time)
        {
            // Get gamepad state once for all hold-to-repeat checks
            var gpState = GamePad.GetState(PlayerIndex.One);

            // Y button sell hold-to-repeat
            if (_yHeldOnSellTab)
            {
                bool yStillHeld = gpState.IsButtonDown(_yHoldRawButton);
                bool stillOnSellTab = InvVisibleField != null && (bool)InvVisibleField.GetValue(__instance);

                if (yStillHeld && stillOnSellTab)
                {
                    _yHoldTicks++;

                    if (_yHoldTicks > SellHoldDelay &&
                        (_yHoldTicks - SellHoldDelay) % SellRepeatRate == 0)
                    {
                        Item sellItem = GetSellTabSelectedItem(__instance);
                        if (sellItem != null && sellItem == _yHoldTargetItem)
                        {
                            SellOneItem(__instance, sellItem);
                        }
                        else
                        {
                            // Item gone or changed — stop repeating
                            _yHeldOnSellTab = false;
                        }
                    }
                }
                else
                {
                    // Y released or left sell tab — stop repeating
                    _yHeldOnSellTab = false;
                    _yHoldTicks = 0;
                    _yHoldTargetItem = null;
                }
            }

            // LB/RB quantity hold-to-repeat
            bool onBuyTab = InvVisibleField == null || !(bool)InvVisibleField.GetValue(__instance);

            if (_lbHeld)
            {
                bool lbStillHeld = gpState.IsButtonDown(Buttons.LeftShoulder);
                if (lbStillHeld && onBuyTab)
                {
                    _lbHoldTicks++;
                    if (_lbHoldTicks > QuantityHoldDelay &&
                        (_lbHoldTicks - QuantityHoldDelay) % QuantityRepeatRate == 0)
                    {
                        // In bumper mode: -1, in non-bumper mode: -10
                        int delta = ModEntry.Config.UseBumpersInsteadOfTriggers ? -1 : -10;
                        AdjustQuantity(__instance, delta);
                    }
                }
                else
                {
                    _lbHeld = false;
                    _lbHoldTicks = 0;
                }
            }

            if (_rbHeld)
            {
                bool rbStillHeld = gpState.IsButtonDown(Buttons.RightShoulder);
                if (rbStillHeld && onBuyTab)
                {
                    _rbHoldTicks++;
                    if (_rbHoldTicks > QuantityHoldDelay &&
                        (_rbHoldTicks - QuantityHoldDelay) % QuantityRepeatRate == 0)
                    {
                        // In bumper mode: +1, in non-bumper mode: +10
                        int delta = ModEntry.Config.UseBumpersInsteadOfTriggers ? 1 : 10;
                        AdjustQuantity(__instance, delta);
                    }
                }
                else
                {
                    _rbHeld = false;
                    _rbHoldTicks = 0;
                }
            }

            // Reset buy quantity to 1 while on sell tab — prevents vanilla trigger input
            // from modifying quantityToBuy while the sell tab is active
            if (InvVisibleField != null && QuantityToBuyField != null)
            {
                bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                if (inventoryVisible)
                {
                    int qty = (int)QuantityToBuyField.GetValue(__instance);
                    if (qty != 1)
                    {
                        QuantityToBuyField.SetValue(__instance, 1);
                    }
                }

                // Fix 5b: When switching to sell tab (via touch or any method), ensure snap navigation
                // is set up on the inventory grid. Without this, touch-switching leaves controller
                // navigation broken because snapToDefaultClickableComponent isn't called.
                if (inventoryVisible && !_wasOnSellTab)
                {
                    // Just switched to sell tab — snap to first inventory slot
                    if (__instance.inventory?.inventory != null && __instance.inventory.inventory.Count > 0)
                    {
                        __instance.setCurrentlySnappedComponentTo(__instance.inventory.inventory[0].myID);
                        __instance.snapCursorToCurrentSnappedComponent();
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("Sell tab activated — snapped to first inventory slot", LogLevel.Debug);
                    }
                }
                _wasOnSellTab = inventoryVisible;
            }
        }

        /// <summary>
        /// Draw postfix — shows sell price tooltip when hovering items on the sell tab.
        /// Redraws the tooltip on top of vanilla's (which has no price) with the sell price
        /// added via moneyAmountToDisplayAtBottom, matching the buy tab's coin icon style.
        /// </summary>
        private static void ShopMenu_Draw_Postfix(ShopMenu __instance, SpriteBatch b)
        {
            try
            {
                if (!ModEntry.Config?.EnableShopPurchaseFix ?? true)
                    return;

                if (InvVisibleField == null)
                    return;

                bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                if (!inventoryVisible)
                    return;

                Item sellItem = GetSellTabSelectedItem(__instance);
                if (sellItem == null)
                    return;

                int sellPrice;
                if (sellItem is StardewValley.Object obj)
                    sellPrice = obj.sellToStorePrice();
                else
                {
                    int sp = sellItem.salePrice();
                    sellPrice = sp > 0 ? sp / 2 : -1;
                }

                if (sellPrice <= 0)
                    return;

                // Build compact sell price text
                string priceText;
                int total = sellPrice * sellItem.Stack;
                if (sellItem.Stack > 1)
                    priceText = $" {total}g ({sellPrice}g each)";
                else
                    priceText = $" {sellPrice}g";

                // Manually position a small tooltip box near the selected inventory slot
                // (drawToolTip/drawHoverText position at mouse cursor which is wrong on sell tab)
                var snapped = __instance.currentlySnappedComponent;
                if (snapped == null)
                    return;

                // Gold coin sprite: Game1.mouseCursors at (193, 373, 9, 10), drawn at 4x
                int coinScale = 4;
                int coinW = 9 * coinScale;  // 36px
                int coinH = 10 * coinScale; // 40px

                Vector2 textSize = Game1.smallFont.MeasureString(priceText);
                int contentW = coinW + (int)textSize.X;
                int contentH = Math.Max(coinH, (int)textSize.Y);
                int pad = 20;
                int boxW = contentW + pad * 2;
                int boxH = contentH + pad * 2;

                // Position to the right of the selected slot
                int boxX = snapped.bounds.Right + 8;
                int boxY = snapped.bounds.Center.Y - boxH / 2;

                // Keep on screen
                if (boxX + boxW > Game1.uiViewport.Width)
                    boxX = snapped.bounds.Left - boxW - 8;
                if (boxY < 0)
                    boxY = 0;
                if (boxY + boxH > Game1.uiViewport.Height)
                    boxY = Game1.uiViewport.Height - boxH;

                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    boxX, boxY, boxW, boxH, Color.White);

                // Draw gold coin icon
                int innerX = boxX + pad;
                int innerY = boxY + pad;
                b.Draw(Game1.mouseCursors,
                    new Vector2(innerX, innerY + (contentH - coinH) / 2),
                    new Rectangle(193, 373, 9, 10),
                    Color.White, 0f, Vector2.Zero, coinScale, SpriteEffects.None, 1f);

                // Draw price text to the right of the coin
                Utility.drawTextWithShadow(b, priceText, Game1.smallFont,
                    new Vector2(innerX + coinW, innerY + (contentH - (int)textSize.Y) / 2),
                    Game1.textColor);
            }
            catch
            {
                // Silently ignore tooltip draw errors — never crash the draw loop
            }
        }
    }
}
