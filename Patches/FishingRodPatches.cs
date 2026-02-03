using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Handles fishing rod bait/tackle management via controller.
    /// Android-adapted behavior:
    /// - A button on bait/tackle = select it (tracked internally)
    /// - Y button on rod = attach selected bait/tackle, or detach if nothing selected
    /// </summary>
    internal static class FishingRodPatches
    {
        private static IMonitor Monitor;

        // Item categories
        private const int BaitCategory = -21;
        private const int TackleCategory = -22;

        // Track the selected bait/tackle slot (since Android's A button doesn't use CursorSlotItem)
        private static int SelectedBaitTackleSlot = -1;

        /// <summary>Apply Harmony patches (currently none needed - logic is in TryHandleBaitTackle).</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            Monitor.Log("FishingRod patches applied successfully.", LogLevel.Trace);
        }

        /// <summary>
        /// Called when A button is pressed in inventory. Tracks if bait/tackle was selected.
        /// </summary>
        public static void OnAButtonPressed(GameMenu gameMenu, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null) return;

                var pageSnapped = inventoryPage.currentlySnappedComponent;
                if (pageSnapped == null) return;

                int slotId = pageSnapped.myID;
                if (slotId < 0 || slotId >= Game1.player.Items.Count) return;

                Item item = Game1.player.Items[slotId];
                if (item == null) return;

                // Check if it's bait or tackle
                if (item.Category == BaitCategory || item.Category == TackleCategory)
                {
                    SelectedBaitTackleSlot = slotId;
                    Monitor.Log($"FishingRod: Selected {item.Name} at slot {slotId} for attachment", LogLevel.Debug);
                }
                else
                {
                    // Selected something else - clear bait/tackle selection
                    if (SelectedBaitTackleSlot >= 0)
                    {
                        Monitor.Log($"FishingRod: Cleared bait/tackle selection (selected non-bait item)", LogLevel.Debug);
                        SelectedBaitTackleSlot = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"FishingRod OnAButton error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Try to handle Y button press for fishing rod bait/tackle management.
        /// Called from ModEntry.OnButtonsChanged when Y is pressed in inventory.
        /// </summary>
        public static bool TryHandleBaitTackle(GameMenu gameMenu, IModHelper helper, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Get the inventory page
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("FishingRod: Could not get InventoryPage", LogLevel.Debug);
                    return false;
                }

                // Get the currently snapped component to find hovered slot
                var pageSnapped = inventoryPage.currentlySnappedComponent;

                if (ModEntry.Config.VerboseLogging)
                {
                    Monitor.Log($"FishingRod: page.snapped={pageSnapped?.myID.ToString() ?? "null"}, selectedBaitSlot={SelectedBaitTackleSlot}", LogLevel.Debug);
                }

                int slotId = -1;
                if (pageSnapped != null && pageSnapped.myID >= 0 && pageSnapped.myID < Game1.player.Items.Count)
                    slotId = pageSnapped.myID;

                if (slotId < 0)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"FishingRod: No valid slot ID found", LogLevel.Debug);
                    return false;
                }

                Item hoveredItem = Game1.player.Items[slotId];

                // Check if hovering over a fishing rod
                if (hoveredItem is not FishingRod rod)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"FishingRod: Slot {slotId} is not a fishing rod: {hoveredItem?.Name ?? "null"}", LogLevel.Debug);
                    return false; // Not a fishing rod
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"FishingRod: Y pressed on rod '{rod.Name}'", LogLevel.Debug);

                // Check if we have a selected bait/tackle slot
                if (SelectedBaitTackleSlot >= 0 && SelectedBaitTackleSlot < Game1.player.Items.Count)
                {
                    Item selectedItem = Game1.player.Items[SelectedBaitTackleSlot];

                    if (selectedItem != null && (selectedItem.Category == BaitCategory || selectedItem.Category == TackleCategory))
                    {
                        Monitor.Log($"FishingRod: Attaching selected {selectedItem.Name} from slot {SelectedBaitTackleSlot}", LogLevel.Debug);
                        bool result = HandleAttachFromSlot(rod, selectedItem, SelectedBaitTackleSlot);

                        // Clear selection after attach attempt
                        SelectedBaitTackleSlot = -1;
                        return result;
                    }
                    else
                    {
                        // Selection is stale (item moved or gone)
                        Monitor.Log($"FishingRod: Selected slot {SelectedBaitTackleSlot} no longer contains bait/tackle", LogLevel.Debug);
                        SelectedBaitTackleSlot = -1;
                    }
                }

                // No selected bait/tackle - try to detach from rod
                return HandleDetachItem(rod);
            }
            catch (Exception ex)
            {
                Monitor.Log($"FishingRod error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Handle attaching an item from a specific inventory slot to the fishing rod.
        /// </summary>
        private static bool HandleAttachFromSlot(FishingRod rod, Item item, int sourceSlot)
        {
            // Check if item is bait
            if (item.Category == BaitCategory)
            {
                if (!rod.CanUseBait())
                {
                    Monitor.Log($"FishingRod: Rod cannot use bait", LogLevel.Debug);
                    Game1.playSound("cancel");
                    return true;
                }

                return AttachBaitFromSlot(rod, item, sourceSlot);
            }

            // Check if item is tackle
            if (item.Category == TackleCategory)
            {
                if (!rod.CanUseTackle())
                {
                    Monitor.Log($"FishingRod: Rod cannot use tackle", LogLevel.Debug);
                    Game1.playSound("cancel");
                    return true;
                }

                return AttachTackleFromSlot(rod, item, sourceSlot);
            }

            return false;
        }

        /// <summary>
        /// Attach bait from inventory slot to rod's bait slot.
        /// </summary>
        private static bool AttachBaitFromSlot(FishingRod rod, Item baitItem, int sourceSlot)
        {
            SObject currentBait = rod.attachments[0];

            if (currentBait == null)
            {
                // Empty slot - attach directly
                rod.attachments[0] = (SObject)baitItem;
                Game1.player.Items[sourceSlot] = null;
                Game1.playSound("button1");
                Monitor.Log($"FishingRod: Attached {baitItem.Stack}x {baitItem.Name} to bait slot", LogLevel.Info);
            }
            else if (currentBait.canStackWith(baitItem))
            {
                // Same bait type - stack
                int spaceAvailable = currentBait.maximumStackSize() - currentBait.Stack;
                int toAdd = Math.Min(spaceAvailable, baitItem.Stack);

                if (toAdd > 0)
                {
                    currentBait.Stack += toAdd;
                    baitItem.Stack -= toAdd;

                    if (baitItem.Stack <= 0)
                    {
                        Game1.player.Items[sourceSlot] = null;
                    }

                    Game1.playSound("button1");
                    Monitor.Log($"FishingRod: Stacked {toAdd}x bait (now {currentBait.Stack})", LogLevel.Info);
                }
                else
                {
                    Game1.playSound("cancel");
                    Monitor.Log($"FishingRod: Bait slot full, cannot stack more", LogLevel.Debug);
                }
            }
            else
            {
                // Different bait - swap
                rod.attachments[0] = (SObject)baitItem;
                Game1.player.Items[sourceSlot] = currentBait;
                Game1.playSound("button1");
                Monitor.Log($"FishingRod: Swapped bait - attached {baitItem.Name}, removed {currentBait.Name} to slot {sourceSlot}", LogLevel.Info);
            }

            return true;
        }

        /// <summary>
        /// Attach tackle from inventory slot to rod's tackle slot.
        /// </summary>
        private static bool AttachTackleFromSlot(FishingRod rod, Item tackleItem, int sourceSlot)
        {
            SObject currentTackle = rod.attachments[1];

            if (currentTackle == null)
            {
                // Empty slot - attach directly
                rod.attachments[1] = (SObject)tackleItem;
                Game1.player.Items[sourceSlot] = null;
                Game1.playSound("button1");
                Monitor.Log($"FishingRod: Attached {tackleItem.Name} to tackle slot", LogLevel.Info);
            }
            else
            {
                // Tackle doesn't stack - swap
                rod.attachments[1] = (SObject)tackleItem;
                Game1.player.Items[sourceSlot] = currentTackle;
                Game1.playSound("button1");
                Monitor.Log($"FishingRod: Swapped tackle - attached {tackleItem.Name}, removed {currentTackle.Name} to slot {sourceSlot}", LogLevel.Info);
            }

            return true;
        }

        /// <summary>
        /// Handle detaching bait/tackle from the fishing rod.
        /// Priority: Bait first, then tackle.
        /// </summary>
        private static bool HandleDetachItem(FishingRod rod)
        {
            // Try to detach bait first
            SObject bait = rod.attachments[0];
            if (bait != null)
            {
                rod.attachments[0] = null;

                if (TryPutInInventory(bait))
                {
                    Game1.playSound("button1");
                    Monitor.Log($"FishingRod: Removed {bait.Stack}x {bait.Name} to inventory", LogLevel.Info);
                    return true;
                }

                // Failed - put it back
                rod.attachments[0] = bait;
                Game1.playSound("cancel");
                Monitor.Log($"FishingRod: Could not remove bait - no space", LogLevel.Warn);
                return true;
            }

            // Try to detach tackle
            SObject tackle = rod.attachments[1];
            if (tackle != null)
            {
                rod.attachments[1] = null;

                if (TryPutInInventory(tackle))
                {
                    Game1.playSound("button1");
                    Monitor.Log($"FishingRod: Removed {tackle.Name} to inventory", LogLevel.Info);
                    return true;
                }

                // Failed - put it back
                rod.attachments[1] = tackle;
                Game1.playSound("cancel");
                Monitor.Log($"FishingRod: Could not remove tackle - no space", LogLevel.Warn);
                return true;
            }

            // No attachments to remove
            if (ModEntry.Config.VerboseLogging)
                Monitor.Log($"FishingRod: No attachments to remove", LogLevel.Debug);
            return false;
        }

        /// <summary>
        /// Try to put an item in an empty inventory slot.
        /// </summary>
        private static bool TryPutInInventory(Item item)
        {
            try
            {
                for (int i = 0; i < Game1.player.Items.Count; i++)
                {
                    if (Game1.player.Items[i] == null)
                    {
                        Game1.player.Items[i] = item;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"FishingRod: Failed to put in inventory: {ex.Message}", LogLevel.Debug);
            }
            return false;
        }

        /// <summary>
        /// Clear the selected bait/tackle slot (called when leaving inventory menu).
        /// </summary>
        public static void ClearSelection()
        {
            SelectedBaitTackleSlot = -1;
        }
    }
}
