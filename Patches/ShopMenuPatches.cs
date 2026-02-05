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
                            ISalable hoveredSalable = HoveredItemField?.GetValue(__instance) as ISalable;
                            if (hoveredSalable != null && hoveredSalable is Item sellItem)
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
                        ISalable hoveredSalable = HoveredItemField?.GetValue(__instance) as ISalable;
                        if (hoveredSalable == null || !(hoveredSalable is Item sellItem))
                        {
                            Monitor.Log("Sell tab: no item under cursor, passing to vanilla", LogLevel.Trace);
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
                    Monitor.Log("No hoveredItem — passing A to vanilla", LogLevel.Trace);
                    return true;
                }

                // Verify it's actually a for-sale item
                if (!__instance.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                {
                    Monitor.Log($"No price info for {selectedItem.DisplayName} — passing A to vanilla", LogLevel.Trace);
                    return true;
                }

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

                Monitor.Log($"Item: {selectedItem.DisplayName}, Unit price: {unitPrice}, Quantity: {quantity}, Total: {totalCost}, Player has: {playerMoney}", LogLevel.Debug);

                // Limit to what player can afford
                if (playerMoney < totalCost)
                {
                    int affordableQty = unitPrice > 0 ? playerMoney / unitPrice : 0;
                    if (affordableQty <= 0)
                    {
                        Game1.playSound("cancel");
                        Monitor.Log("Cannot afford any", LogLevel.Debug);
                        return false; // Block vanilla A handler
                    }
                    quantity = affordableQty;
                    totalCost = unitPrice * quantity;
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
                        Monitor.Log($"Not enough trade items ({tradeItem}): need {tradeItemCost}, have {playerTradeItems}", LogLevel.Debug);
                        return false;
                    }

                    if (quantity > maxByTradeItems)
                    {
                        quantity = maxByTradeItems;
                        totalCost = unitPrice * quantity;
                        Monitor.Log($"Reduced quantity to {quantity} (limited by trade items)", LogLevel.Debug);
                    }

                    totalTradeItems = tradeItemCost * quantity;
                }

                // Check inventory space
                if (selectedItem is Item item && !Game1.player.couldInventoryAcceptThisItem(item))
                {
                    Game1.playSound("cancel");
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
                    Monitor.Log($"Consumed {totalTradeItems}x {tradeItem}", LogLevel.Debug);
                }

                // Call actionWhenPurchased — handles recipes, tool upgrades, trash can upgrades, etc.
                string shopId = __instance.ShopId;
                bool handled = selectedItem.actionWhenPurchased(shopId);

                if (handled)
                {
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
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                return true; // On error, let vanilla handle it
            }
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
                Monitor.Log($"Sell tab: {sellItem.DisplayName} cannot be sold (price={sellPrice})", LogLevel.Debug);
                return false;
            }

            Game1.player.Money += sellPrice;

            if (sellItem.Stack > 1)
            {
                sellItem.Stack -= 1;
                Monitor.Log($"Sold 1x {sellItem.DisplayName} for {sellPrice}g ({sellItem.Stack} remaining)", LogLevel.Debug);
            }
            else
            {
                // Last item in stack — remove from inventory
                int idx = Game1.player.Items.IndexOf(sellItem);
                if (idx >= 0)
                    Game1.player.Items[idx] = null;
                HoveredItemField?.SetValue(shop, null);
                Monitor.Log($"Sold last {sellItem.DisplayName} for {sellPrice}g", LogLevel.Debug);
            }

            Game1.playSound("purchaseClick");
            return true;
        }

        /// <summary>Postfix for update to handle held A button for continuous purchasing.</summary>
        private static void Update_Postfix(ShopMenu __instance, GameTime time)
        {
            // Y button sell hold-to-repeat
            if (_yHeldOnSellTab)
            {
                var gpState = GamePad.GetState(PlayerIndex.One);
                bool yStillHeld = gpState.IsButtonDown(_yHoldRawButton);
                bool stillOnSellTab = InvVisibleField != null && (bool)InvVisibleField.GetValue(__instance);

                if (yStillHeld && stillOnSellTab)
                {
                    _yHoldTicks++;

                    if (_yHoldTicks > SellHoldDelay &&
                        (_yHoldTicks - SellHoldDelay) % SellRepeatRate == 0)
                    {
                        ISalable hoveredSalable = HoveredItemField?.GetValue(__instance) as ISalable;
                        if (hoveredSalable is Item sellItem && sellItem == _yHoldTargetItem)
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

                ISalable hoveredSalable = HoveredItemField?.GetValue(__instance) as ISalable;
                if (hoveredSalable == null || !(hoveredSalable is Item sellItem))
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

                string description = sellItem.getDescription() ?? "";
                if (sellItem.Stack > 1)
                {
                    int totalValue = sellPrice * sellItem.Stack;
                    description += $"\n\nStack of {sellItem.Stack}: {totalValue}g total";
                }

                IClickableMenu.drawToolTip(
                    b,
                    description,
                    sellItem.DisplayName,
                    sellItem,
                    false,      // heldItem
                    0,          // currencySymbol (0 = gold)
                    0,          // extraItemToShowIndex
                    null,       // extraItemToShowAmount
                    sellPrice,  // moneyAmountToDisplayAtBottom
                    null,       // boldTitleText
                    -1          // healAmountToDisplay
                );
            }
            catch
            {
                // Silently ignore tooltip draw errors — never crash the draw loop
            }
        }
    }
}
