using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>Harmony patches for InventoryPage to fix controller support.</summary>
    internal static class InventoryPagePatches
    {
        private static IMonitor Monitor;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch GameMenu's receiveGamePadButton to intercept X for inventory sorting
                // Use PREFIX to block the original method (which deletes items on Android)
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(GameMenu_ReceiveGamePadButton_Prefix))
                );

                // Also patch InventoryPage directly - the deletion might happen here
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(InventoryPage_ReceiveGamePadButton_Prefix))
                );

                Monitor.Log("InventoryPage patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply InventoryPage patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Prefix for GameMenu.receiveGamePadButton to handle X button for sorting.</summary>
        /// <returns>False to block the original method (prevents Android X-delete bug), true to let it run.</returns>
        private static bool GameMenu_ReceiveGamePadButton_Prefix(GameMenu __instance, Buttons b)
        {
            try
            {
                // Only handle when on inventory tab
                if (__instance.currentTab != GameMenu.inventoryTab)
                    return true; // Let original method run for other tabs

                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                Monitor.Log($"GameMenu (inventory) button: {b} (remapped={remapped})", LogLevel.Debug);

                // CRITICAL: Always block raw X button in inventory to prevent Android deletion bug
                // The game's Android code uses raw X for deletion, regardless of our remapping
                if (b == Buttons.X && ModEntry.Config.EnableSortFix)
                {
                    Monitor.Log($"Blocking raw X button in GameMenu inventory to prevent deletion", LogLevel.Debug);
                    return false; // Block original method to prevent item deletion
                }

                // X button (after remapping) = Sort inventory (and block the original)
                if (remapped == Buttons.X && ModEntry.Config.EnableSortFix)
                {
                    Monitor.Log($"{b} remapped to X in GameMenu inventory - sorting (blocking original)", LogLevel.Debug);
                    SortPlayerInventory();
                    return false; // Block original method
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in GameMenu controller handler: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }

            return true; // Let original method run for other buttons
        }

        /// <summary>Prefix for InventoryPage.receiveGamePadButton to block X button deletion.</summary>
        /// <returns>False to block X button (prevents Android deletion bug), true to let it run.</returns>
        private static bool InventoryPage_ReceiveGamePadButton_Prefix(InventoryPage __instance, Buttons b)
        {
            try
            {
                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                Monitor.Log($"InventoryPage button: {b} (remapped={remapped})", LogLevel.Debug);

                // CRITICAL: Always block raw X button on InventoryPage - this is where deletion happens on Android
                // Must block regardless of remapping to prevent the deletion bug
                if (b == Buttons.X)
                {
                    Monitor.Log($"Blocking raw X button in InventoryPage to prevent deletion", LogLevel.Debug);
                    return false; // Block original method to prevent item deletion
                }

                // Also block remapped X button (e.g., physical Y on Xbox layout that remaps to X)
                if (remapped == Buttons.X)
                {
                    Monitor.Log($"{b} remapped to X in InventoryPage - BLOCKING", LogLevel.Debug);
                    return false; // Block original method
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in InventoryPage controller handler: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }

            return true; // Let original method run for other buttons
        }

        /// <summary>Sort the player's inventory.</summary>
        public static void SortPlayerInventory()
        {
            try
            {
                Monitor.Log("SortPlayerInventory called", LogLevel.Debug);

                var inventory = Game1.player.Items;
                if (inventory == null || inventory.Count == 0)
                {
                    Monitor.Log("No inventory to sort", LogLevel.Debug);
                    return;
                }

                // Try using the game's built-in organize method
                try
                {
                    ItemGrabMenu.organizeItemsInList(inventory);
                    Game1.playSound("Ship");
                    Monitor.Log("Sorted inventory via organizeItemsInList", LogLevel.Info);
                    return;
                }
                catch (Exception ex)
                {
                    Monitor.Log($"organizeItemsInList failed: {ex.Message}", LogLevel.Debug);
                }

                // Fallback: manual sort
                // Keep first 12 slots (toolbar) in place, sort the rest
                var toolbarItems = inventory.Take(12).ToList();
                var backpackItems = inventory.Skip(12).ToList();

                var sortedBackpack = backpackItems
                    .Where(i => i != null)
                    .OrderBy(i => i.Category)
                    .ThenBy(i => i.DisplayName)
                    .ToList();

                // Rebuild inventory
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (i < 12)
                    {
                        // Keep toolbar as-is
                        inventory[i] = toolbarItems[i];
                    }
                    else
                    {
                        int backpackIndex = i - 12;
                        inventory[i] = backpackIndex < sortedBackpack.Count ? sortedBackpack[backpackIndex] : null;
                    }
                }

                Game1.playSound("Ship");
                Monitor.Log("Sorted inventory manually (backpack only)", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error sorting inventory: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
