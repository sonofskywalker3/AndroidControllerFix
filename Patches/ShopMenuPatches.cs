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
    /// <summary>Harmony patches for ShopMenu to fix controller purchasing.</summary>
    internal static class ShopMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch receiveGamePadButton to intercept A button for purchasing
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveGamePadButton)),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(ReceiveGamePadButton_Postfix))
                );

                // Patch update to handle held button for bulk purchasing
                harmony.Patch(
                    original: AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.update)),
                    postfix: new HarmonyMethod(typeof(ShopMenuPatches), nameof(Update_Postfix))
                );

                Monitor.Log("ShopMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply ShopMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Postfix for receiveGamePadButton to handle A button purchase.</summary>
        private static void ReceiveGamePadButton_Postfix(ShopMenu __instance, Buttons b)
        {
            try
            {
                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                // Always log button presses to confirm patch is working
                Monitor.Log($"ShopMenu postfix: button={b} (remapped={remapped})", LogLevel.Debug);

                // Only handle A button (confirm/purchase) after remapping
                if (remapped != Buttons.A)
                    return;

                Monitor.Log("A button in shop - attempting purchase fix", LogLevel.Info);

                if (!ModEntry.Config?.EnableShopPurchaseFix ?? true)
                {
                    Monitor.Log("Shop purchase fix is disabled in config", LogLevel.Debug);
                    return;
                }

                // Check if we're in sell mode via inventoryVisible field.
                // Diagnostic v2.7.8 confirmed: inventoryVisible=False on buy tab, True on sell tab.
                // currentTab stays 0 in both modes — not useful.
                var invVisibleField = AccessTools.Field(typeof(ShopMenu), "inventoryVisible");
                if (invVisibleField != null)
                {
                    bool inventoryVisible = (bool)invVisibleField.GetValue(__instance);
                    if (inventoryVisible)
                    {
                        Monitor.Log("inventoryVisible=True — on sell tab, skipping purchase", LogLevel.Trace);
                        return;
                    }
                }

                // Use hoveredItem to find the selected item.
                // On Android, forSaleButtons all have myID=-500 so we can't match by ID.
                // hoveredItem is set correctly by the game's navigation on the buy tab.
                ISalable selectedItem = null;
                var hoveredField = AccessTools.Field(typeof(ShopMenu), "hoveredItem");
                if (hoveredField != null)
                {
                    selectedItem = hoveredField.GetValue(__instance) as ISalable;
                }

                if (selectedItem == null)
                {
                    Monitor.Log("No hoveredItem — skipping purchase", LogLevel.Trace);
                    return;
                }

                Monitor.Log($"Selected item: {selectedItem.DisplayName}", LogLevel.Debug);

                // Get price info
                if (!__instance.itemPriceAndStock.TryGetValue(selectedItem, out var priceAndStock))
                {
                    Monitor.Log($"No price info for {selectedItem.DisplayName}", LogLevel.Warn);
                    return;
                }

                int unitPrice = priceAndStock.Price;
                int stock = priceAndStock.Stock;
                int playerMoney = ShopMenu.getPlayerCurrencyAmount(Game1.player, __instance.currency);

                // Get the quantity to buy (set by LT/RT)
                int quantity = 1;
                var quantityField = AccessTools.Field(typeof(ShopMenu), "quantityToBuy");
                if (quantityField != null)
                {
                    quantity = Math.Max(1, (int)quantityField.GetValue(__instance));
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
                    int affordableQty = playerMoney / unitPrice;
                    if (affordableQty <= 0)
                    {
                        Game1.playSound("cancel");
                        Monitor.Log("Cannot afford any", LogLevel.Debug);
                        return;
                    }
                    quantity = affordableQty;
                    totalCost = unitPrice * quantity;
                    Monitor.Log($"Reduced quantity to {quantity} (affordable)", LogLevel.Debug);
                }

                // Manual purchase
                Monitor.Log($"Purchasing {quantity}x {selectedItem.DisplayName} for {totalCost}...", LogLevel.Debug);

                // Check inventory space
                if (selectedItem is Item item && !Game1.player.couldInventoryAcceptThisItem(item))
                {
                    Game1.playSound("cancel");
                    Monitor.Log("Inventory full", LogLevel.Debug);
                    return;
                }

                // Deduct money
                ShopMenu.chargePlayer(Game1.player, __instance.currency, totalCost);
                Monitor.Log($"Charged player {totalCost}", LogLevel.Debug);

                // Add item(s) to inventory
                if (selectedItem is Item purchaseItem)
                {
                    var newItem = purchaseItem.getOne();
                    newItem.Stack = quantity;
                    Game1.player.addItemToInventoryBool(newItem);
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
                        // Update the stock count in itemPriceAndStock
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
                if (quantityField != null)
                {
                    quantityField.SetValue(__instance, 1);
                }

                Game1.playSound("purchaseClick");
                Monitor.Log($"Purchase complete! Bought {quantity}x {selectedItem.DisplayName}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in shop purchase postfix: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }
        }

        /// <summary>Postfix for update to handle held A button for continuous purchasing.</summary>
        private static void Update_Postfix(ShopMenu __instance, GameTime time)
        {
            // This could be extended to support held-button bulk purchasing
            // For MVP, single purchases are sufficient
        }
    }
}
