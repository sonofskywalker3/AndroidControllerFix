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

        // DIAGNOSTIC v3.2.3: Track CursorSlotItem state across method calls
        private static string DiagBeforeCursorItem = "(none)";

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

                // DIAGNOSTIC v3.2.3: Instrument receiveLeftClick + releaseLeftClick
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveLeftClick)),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReceiveLeftClick_DiagCapture)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReceiveLeftClick_DiagResult))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), "releaseLeftClick"),
                    prefix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReleaseLeftClick_DiagPre)),
                    postfix: new HarmonyMethod(typeof(InventoryPagePatches), nameof(ReleaseLeftClick_DiagPost))
                );

                Monitor.Log("InventoryPage patches applied successfully (with v3.2.3 diagnostics).", LogLevel.Trace);
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

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"GameMenu (inventory) button: {b} (remapped={remapped})", LogLevel.Debug);

                // Block A button when console inventory is enabled - we handle it ourselves
                // EXCEPT when AllowGameAPress is set (non-inventory slot like equipment, sort, trash)
                if (ModEntry.Config.EnableConsoleInventory)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        if (InventoryManagementPatches.AllowGameAPress)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Allowing A button through for non-inventory slot (GameMenu)", LogLevel.Debug);
                            return true;
                        }
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Blocking A button in GameMenu inventory (console inventory mode)", LogLevel.Debug);
                        return false;
                    }
                }

                // CRITICAL: Always block raw X button in inventory to prevent Android deletion bug
                // The game's Android code uses raw X for deletion, regardless of our remapping
                if (b == Buttons.X && ModEntry.Config.EnableConsoleChests)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Blocking raw X button in GameMenu inventory to prevent deletion", LogLevel.Debug);
                    return false; // Block original method to prevent item deletion
                }

                // X button (after remapping) = Sort inventory (and block the original)
                if (remapped == Buttons.X && ModEntry.Config.EnableConsoleChests)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"{b} remapped to X in GameMenu inventory - sorting (blocking original)", LogLevel.Debug);
                    SortPlayerInventory();
                    return false; // Block original method
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in GameMenu controller handler: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
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

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryPage button: {b} (remapped={remapped})", LogLevel.Debug);

                // Block A button when console inventory is enabled - we handle it ourselves
                // EXCEPT when AllowGameAPress is set (non-inventory slot like equipment, sort, trash)
                if (ModEntry.Config.EnableConsoleInventory)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        if (InventoryManagementPatches.AllowGameAPress)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Allowing A button through for non-inventory slot (InventoryPage)", LogLevel.Debug);
                            return true;
                        }
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Blocking A button in InventoryPage (console inventory mode)", LogLevel.Debug);
                        return false;
                    }
                }

                // CRITICAL: Always block raw X button on InventoryPage - this is where deletion happens on Android
                // Must block regardless of remapping to prevent the deletion bug
                if (b == Buttons.X)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"Blocking raw X button in InventoryPage to prevent deletion", LogLevel.Debug);
                    return false; // Block original method to prevent item deletion
                }

                // Also block remapped X button (e.g., physical Y on Xbox layout that remaps to X)
                if (remapped == Buttons.X)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"{b} remapped to X in InventoryPage - BLOCKING", LogLevel.Debug);
                    return false; // Block original method
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in InventoryPage controller handler: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
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
                // EXCEPT when AllowGameAPress is set (non-inventory slot like equipment, sort, trash)
                if (ModEntry.Config.EnableConsoleInventory)
                {
                    if (b == Buttons.A || remapped == Buttons.A)
                    {
                        if (InventoryManagementPatches.AllowGameAPress)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"Allowing A button through for non-inventory slot (InventoryMenu)", LogLevel.Debug);
                            return true;
                        }
                        if (ModEntry.Config.VerboseLogging)
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
            if (ModEntry.Config.EnableConsoleInventory)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    // Let the game handle non-inventory slots (equipment, sort, trash)
                    if (InventoryManagementPatches.AllowGameAPress)
                        return true;

                    // v3.2.5: Allow leftClickHeld through when holding an item on an
                    // equipment/trash slot. The game's touch-drag equip path needs
                    // leftClickHeld to fire (it may set internal state for releaseLeftClick
                    // or contain the equip logic itself).
                    if (InventoryManagementPatches.IsCurrentlyHolding())
                    {
                        var snapped = __instance.currentlySnappedComponent;
                        if (snapped != null)
                        {
                            bool isInventorySlot = snapped.myID >= 0 && snapped.myID < Game1.player.Items.Count;
                            if (!isInventorySlot)
                            {
                                Monitor.Log($"[DIAG] leftClickHeld ALLOWED for equip: ({x},{y}) slot={snapped.myID} cursor={Game1.player.CursorSlotItem?.Name}", LogLevel.Info);
                                return true;
                            }
                        }
                    }

                    if (ModEntry.Config.VerboseLogging)
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
            if (ModEntry.Config.EnableConsoleInventory)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    // Let the game handle non-inventory slots (equipment, sort, trash)
                    if (InventoryManagementPatches.AllowGameAPress)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Allowing receiveLeftClick through for non-inventory slot", LogLevel.Debug);
                        return true;
                    }
                    if (ModEntry.Config.VerboseLogging)
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
            if (ModEntry.Config.EnableConsoleInventory && Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
            {
                GamePadState gpState = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
                if (gpState.Buttons.A == ButtonState.Pressed)
                {
                    // Let the game handle non-inventory slots (equipment, sort, trash)
                    if (InventoryManagementPatches.AllowGameAPress)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"Allowing InventoryMenu.receiveLeftClick through for non-inventory slot", LogLevel.Debug);
                        return true;
                    }
                    if (ModEntry.Config.VerboseLogging)
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
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("SortPlayerInventory called", LogLevel.Debug);

                var inventory = Game1.player.Items;
                if (inventory == null || inventory.Count == 0)
                {
                    if (ModEntry.Config.VerboseLogging)
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
                    if (ModEntry.Config.VerboseLogging)
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

        // ===== DIAGNOSTIC v3.2.3: Equipment/trash equip investigation =====

        /// <summary>Capture CursorSlotItem BEFORE receiveLeftClick runs (high priority prefix).</summary>
        private static void ReceiveLeftClick_DiagCapture(int x, int y)
        {
            DiagBeforeCursorItem = Game1.player.CursorSlotItem?.Name ?? "(none)";
        }

        /// <summary>Log results AFTER receiveLeftClick runs — mouse pos, component bounds, cursor state.</summary>
        private static void ReceiveLeftClick_DiagResult(InventoryPage __instance, int x, int y, bool playSound)
        {
            try
            {
                string after = Game1.player.CursorSlotItem?.Name ?? "(none)";
                int mX = Game1.getMouseX();
                int mY = Game1.getMouseY();
                var ms = Mouse.GetState();

                Monitor.Log($"[DIAG] rcvLeftClick: params=({x},{y}) mouse=({mX},{mY}) rawMouse=({ms.X},{ms.Y})", LogLevel.Info);
                Monitor.Log($"[DIAG]   cursor: {DiagBeforeCursorItem} -> {after}", LogLevel.Info);

                // When holding an item, dump all non-inventory component bounds so we can see what the game sees
                if (DiagBeforeCursorItem != "(none)" && __instance.allClickableComponents != null)
                {
                    foreach (var comp in __instance.allClickableComponents)
                    {
                        if (comp.myID >= 100)
                            Monitor.Log($"[DIAG]   comp ID={comp.myID} name='{comp.name}' bounds={comp.bounds} hit={comp.containsPoint(x, y)}", LogLevel.Info);
                    }

                    // Check trashCan directly (may be a separate field, not in allClickableComponents)
                    var trashCan = AccessTools.Field(typeof(InventoryPage), "trashCan")?.GetValue(__instance) as ClickableTextureComponent;
                    if (trashCan != null)
                        Monitor.Log($"[DIAG]   trashCan bounds={trashCan.bounds} hit={trashCan.containsPoint(x, y)} myID={trashCan.myID}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[DIAG] rcvLeftClick error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Log BEFORE releaseLeftClick — does touch-equip go through this path?</summary>
        private static void ReleaseLeftClick_DiagPre(IClickableMenu __instance, int x, int y)
        {
            if (__instance is not InventoryPage) return;
            try
            {
                string cursor = Game1.player.CursorSlotItem?.Name ?? "(none)";
                Monitor.Log($"[DIAG] releaseLeftClick PRE: ({x},{y}) cursor={cursor}", LogLevel.Info);
            }
            catch { }
        }

        /// <summary>Log AFTER releaseLeftClick — did CursorSlotItem get consumed?</summary>
        private static void ReleaseLeftClick_DiagPost(IClickableMenu __instance, int x, int y)
        {
            if (__instance is not InventoryPage) return;
            try
            {
                string cursor = Game1.player.CursorSlotItem?.Name ?? "(none)";
                Monitor.Log($"[DIAG] releaseLeftClick POST: ({x},{y}) cursor={cursor}", LogLevel.Info);
            }
            catch { }
        }
    }
}
