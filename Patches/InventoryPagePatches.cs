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

                // Patch InventoryMenu.receiveGamePadButton as well
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(InventoryMenu_ReceiveGamePadButton_Prefix))
                );

                // Patch leftClickHeld to block Android's hold-for-tooltip behavior
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.leftClickHeld)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(InventoryPage_LeftClickHeld_Prefix))
                );

                // Patch receiveLeftClick to block Android's A-button-as-click behavior
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(InventoryPage_ReceiveLeftClick_Prefix))
                );

                // Patch InventoryMenu.receiveLeftClick as well
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(InventoryMenu_ReceiveLeftClick_Prefix))
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

                // Block A button when console inventory is enabled - we handle it ourselves
                if (ModEntry.Config.EnableConsoleInventoryFix)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        Monitor.Log($"Blocking A button in GameMenu inventory (console inventory mode)", LogLevel.Debug);
                        return false;
                    }
                }

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

        /// <summary>Prefix for InventoryPage.receiveGamePadButton to block X and A buttons.</summary>
        /// <returns>False to block the button, true to let it run.</returns>
        private static bool InventoryPage_ReceiveGamePadButton_Prefix(InventoryPage __instance, Buttons b)
        {
            try
            {
                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                Monitor.Log($"InventoryPage button: {b} (remapped={remapped})", LogLevel.Debug);

                // Block A button when console inventory is enabled - we handle it ourselves
                // This prevents the default selection box and tooltip behavior
                if (ModEntry.Config.EnableConsoleInventoryFix)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        Monitor.Log($"Blocking A button in InventoryPage (console inventory mode)", LogLevel.Debug);
                        return false;
                    }
                }

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

        /// <summary>Prefix for InventoryMenu.receiveGamePadButton to block A button.</summary>
        private static bool InventoryMenu_ReceiveGamePadButton_Prefix(InventoryMenu __instance, Buttons b)
        {
            try
            {
                Buttons remapped = ButtonRemapper.Remap(b);

                // Block A button when console inventory is enabled
                if (ModEntry.Config.EnableConsoleInventoryFix)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        Monitor.Log($"Blocking A button in InventoryMenu (console inventory mode)", LogLevel.Debug);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in InventoryMenu controller handler: {ex.Message}", LogLevel.Error);
            }

            return true;
        }

        /// <summary>
        /// Prefix for InventoryPage.leftClickHeld to block Android's hold-for-tooltip behavior.
        /// On Android, holding A button simulates leftClickHeld which triggers tooltips.
        /// </summary>
        private static bool InventoryPage_LeftClickHeld_Prefix(InventoryPage __instance, int x, int y)
        {
            // Block leftClickHeld when console inventory is enabled and A button is held
            // This prevents the Android tooltip-on-hold behavior
            if (ModEntry.Config.EnableConsoleInventoryFix)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    Monitor.Log($"Blocking leftClickHeld while A is pressed (console inventory mode)", LogLevel.Debug);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Prefix for InventoryPage.receiveLeftClick to block Android's A-button-as-click behavior.
        /// On Android, the A button may trigger receiveLeftClick which causes selection.
        /// </summary>
        private static bool InventoryPage_ReceiveLeftClick_Prefix(InventoryPage __instance, int x, int y, bool playSound)
        {
            if (ModEntry.Config.EnableConsoleInventoryFix)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    Monitor.Log($"Blocking receiveLeftClick while A is pressed (console inventory mode)", LogLevel.Debug);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Prefix for InventoryMenu.receiveLeftClick to block Android's A-button-as-click behavior.
        /// </summary>
        private static bool InventoryMenu_ReceiveLeftClick_Prefix(InventoryMenu __instance, int x, int y, bool playSound)
        {
            // Only block in inventory page context, check if we're in GameMenu inventory tab
            if (ModEntry.Config.EnableConsoleInventoryFix && Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    Monitor.Log($"Blocking InventoryMenu.receiveLeftClick while A is pressed", LogLevel.Debug);
                    return false;
                }
            }
            return true;
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
