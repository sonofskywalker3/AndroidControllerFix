using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Console-style shipping bin controls.
    /// No touch UI, no held items, no drop zones.
    /// Just navigate inventory and press A to ship.
    /// </summary>
    internal static class ShippingBinPatches
    {
        private static IMonitor Monitor;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Single patch: intercept gamepad buttons for shipping bin
                harmony.Patch(
                    original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(ShippingBinPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                Monitor.Log("ShippingBin patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply ShippingBin patches: {ex.Message}", LogLevel.Error);
            }
        }

        private static bool hasLoggedComponents = false;

        /// <summary>
        /// Console-style shipping bin controls:
        /// - A button = ship entire stack from selected slot
        /// - Y button = ship one item from selected slot
        /// </summary>
        private static bool ReceiveGamePadButton_Prefix(ItemGrabMenu __instance, Buttons b)
        {
            try
            {
                // Only handle shipping bin menus
                if (!__instance.shippingBin)
                    return true;

                if (!ModEntry.Config.EnableShippingBinFix)
                    return true;

                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                // Log all components once to understand menu structure
                if (!hasLoggedComponents && ModEntry.Config.VerboseLogging)
                {
                    hasLoggedComponents = true;
                    Monitor.Log("ShippingBin menu components:", LogLevel.Debug);
                    if (__instance.allClickableComponents != null)
                    {
                        foreach (var comp in __instance.allClickableComponents)
                        {
                            Monitor.Log($"  Component: myID={comp.myID}, name={comp.name}, bounds={comp.bounds}", LogLevel.Debug);
                        }
                    }
                }

                // Get currently selected inventory slot
                var snappedComponent = __instance.currentlySnappedComponent;
                if (snappedComponent == null)
                    return true;

                int slotId = snappedComponent.myID;

                // Only handle inventory slots (typically 0-35 for player inventory)
                // Inventory slots are in the lower range, other UI elements have higher IDs
                if (slotId < 0 || slotId >= Game1.player.Items.Count)
                    return true;

                // A button (after remapping) = ship entire stack
                if (remapped == Buttons.A)
                {
                    Item item = Game1.player.Items[slotId];
                    if (item == null)
                        return true; // Empty slot, let default behavior

                    if (ShipFromSlot(__instance, slotId, item.Stack))
                    {
                        return false; // We handled it
                    }
                    return true;
                }

                // Y button (after remapping) = ship one item
                if (remapped == Buttons.Y)
                {
                    Item item = Game1.player.Items[slotId];
                    if (item == null)
                        return true; // Empty slot

                    if (ShipFromSlot(__instance, slotId, 1))
                    {
                        return false; // We handled it
                    }
                    return true;
                }

                return true; // Let other buttons pass through
            }
            catch (Exception ex)
            {
                Monitor.Log($"ShippingBin error: {ex.Message}", LogLevel.Error);
                return true;
            }
        }

        /// <summary>
        /// Ship items directly from an inventory slot by simulating the game's normal shipping flow.
        /// </summary>
        /// <param name="menu">The ItemGrabMenu.</param>
        /// <param name="slotIndex">The inventory slot index.</param>
        /// <param name="amount">How many to ship (use item.Stack for all).</param>
        /// <returns>True if shipped successfully.</returns>
        private static bool ShipFromSlot(ItemGrabMenu menu, int slotIndex, int amount)
        {
            try
            {
                Item item = Game1.player.Items[slotIndex];
                if (item == null)
                    return false;

                // Check if item can be shipped
                if (!item.canBeShipped())
                {
                    Monitor.Log($"ShippingBin: {item.Name} cannot be shipped", LogLevel.Debug);
                    Game1.playSound("cancel");
                    return true; // We handled it (with error feedback)
                }

                // Clamp amount to available stack
                int toShip = Math.Min(amount, item.Stack);
                if (toShip <= 0)
                    return false;

                // Create the item to ship
                Item shippedItem = item.getOne();
                shippedItem.Stack = toShip;

                // Remove from player inventory first
                item.Stack -= toShip;
                if (item.Stack <= 0)
                {
                    Game1.player.Items[slotIndex] = null;
                }

                // Use the game's ItemGrabMenu.tryToAddItem behavior
                // This is what gets called when you drop an item in the menu
                try
                {
                    // Try the menu's behavior function - this is what touch uses
                    if (menu.behaviorFunction != null)
                    {
                        menu.behaviorFunction(shippedItem, Game1.player);
                        Monitor.Log($"ShippingBin: Called behaviorFunction", LogLevel.Debug);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Monitor.Log($"ShippingBin: behaviorFunction failed: {ex.Message}", LogLevel.Debug);
                }

                // Fallback: add directly and update display
                IList<Item> shippingBin = Game1.getFarm()?.getShippingBin(Game1.player);
                if (shippingBin != null)
                {
                    shippingBin.Add(shippedItem);
                    menu.setSourceItem(shippedItem);
                    Game1.playSound("Ship");
                    Monitor.Log($"ShippingBin: Shipped {toShip}x via fallback", LogLevel.Debug);
                    return true;
                }

                Monitor.Log("ShippingBin: Could not get shipping bin", LogLevel.Warn);
                return false;
            }
            catch (Exception ex)
            {
                Monitor.Log($"ShippingBin ShipFromSlot error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}
