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

        // Cache reflection fields for performance
        private static FieldInfo InvVisibleField;
        private static FieldInfo HoveredItemField;
        private static FieldInfo QuantityToBuyField;

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

                // Let all non-A buttons pass through to vanilla
                if (remapped != Buttons.A)
                    return true;

                if (!ModEntry.Config?.EnableShopPurchaseFix ?? true)
                    return true; // Disabled — let vanilla handle A

                // Check sell mode via inventoryVisible field.
                // inventoryVisible=False on buy tab, True on sell tab.
                if (InvVisibleField != null)
                {
                    bool inventoryVisible = (bool)InvVisibleField.GetValue(__instance);
                    if (inventoryVisible)
                    {
                        Monitor.Log("inventoryVisible=True — on sell tab, passing A to vanilla", LogLevel.Trace);
                        return true; // Let vanilla handle selling
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

                // Check inventory space
                if (selectedItem is Item item && !Game1.player.couldInventoryAcceptThisItem(item))
                {
                    Game1.playSound("cancel");
                    Monitor.Log("Inventory full", LogLevel.Debug);
                    return false; // Block vanilla A handler
                }

                // Deduct money
                ShopMenu.chargePlayer(Game1.player, __instance.currency, totalCost);

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

        /// <summary>Postfix for update to handle held A button for continuous purchasing.</summary>
        private static void Update_Postfix(ShopMenu __instance, GameTime time)
        {
            // This could be extended to support held-button bulk purchasing
            // For MVP, single purchases are sufficient
        }
    }
}
