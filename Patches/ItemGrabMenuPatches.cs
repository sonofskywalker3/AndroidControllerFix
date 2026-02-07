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
    /// <summary>Harmony patches for ItemGrabMenu to fix chest controller support.</summary>
    internal static class ItemGrabMenuPatches
    {
        private static IMonitor Monitor;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Patch receiveGamePadButton to intercept chest management buttons
                // Use PREFIX to block the original method (prevents Android X-delete bug)
                harmony.Patch(
                    original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                Monitor.Log("ItemGrabMenu patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply ItemGrabMenu patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Fix snap navigation in ItemGrabMenu so trash can, organize, color picker,
        /// and fill stacks buttons are reachable via controller.
        /// Called from ModEntry.OnMenuChanged when an ItemGrabMenu opens.
        /// </summary>
        public static void FixSnapNavigation(ItemGrabMenu menu)
        {
            try
            {
                // Skip shipping bins — they have their own handler
                if (menu.shippingBin)
                    return;

                // Collect side buttons that exist on this menu (order: top to bottom visually)
                var sideButtons = new List<ClickableComponent>();

                if (menu.organizeButton != null)
                    sideButtons.Add(menu.organizeButton);
                if (menu.colorPickerToggleButton != null)
                    sideButtons.Add(menu.colorPickerToggleButton);
                if (menu.fillStacksButton != null)
                    sideButtons.Add(menu.fillStacksButton);
                if (menu.specialButton != null)
                    sideButtons.Add(menu.specialButton);
                if (menu.trashCan != null)
                    sideButtons.Add(menu.trashCan);

                // Sort by Y position (top to bottom) to get correct vertical order
                sideButtons.Sort((a, b) => a.bounds.Y.CompareTo(b.bounds.Y));

                // Log all components for debugging
                if (ModEntry.Config.VerboseLogging)
                {
                    Monitor.Log($"[ChestNav] ItemGrabMenu opened. Side buttons found: {sideButtons.Count}", LogLevel.Debug);
                    foreach (var btn in sideButtons)
                        Monitor.Log($"[ChestNav]   Button: myID={btn.myID}, name={btn.name}, bounds={btn.bounds}", LogLevel.Debug);

                    // Log player inventory slots
                    if (menu.inventory?.inventory != null)
                    {
                        Monitor.Log($"[ChestNav] Player inventory slots: {menu.inventory.inventory.Count}", LogLevel.Debug);
                        foreach (var slot in menu.inventory.inventory)
                            Monitor.Log($"[ChestNav]   Slot: myID={slot.myID}, bounds={slot.bounds}, right={slot.rightNeighborID}, left={slot.leftNeighborID}, up={slot.upNeighborID}, down={slot.downNeighborID}", LogLevel.Debug);
                    }

                    // Log chest slots
                    if (menu.ItemsToGrabMenu?.inventory != null)
                    {
                        Monitor.Log($"[ChestNav] Chest slots: {menu.ItemsToGrabMenu.inventory.Count}", LogLevel.Debug);
                        foreach (var slot in menu.ItemsToGrabMenu.inventory)
                            Monitor.Log($"[ChestNav]   Slot: myID={slot.myID}, bounds={slot.bounds}, right={slot.rightNeighborID}, left={slot.leftNeighborID}, up={slot.upNeighborID}, down={slot.downNeighborID}", LogLevel.Debug);
                    }
                }

                if (sideButtons.Count == 0)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("[ChestNav] No side buttons found, skipping navigation fix", LogLevel.Debug);
                    return;
                }

                // Chain side buttons vertically (up/down between each other)
                for (int i = 0; i < sideButtons.Count; i++)
                {
                    if (i > 0)
                        sideButtons[i].upNeighborID = sideButtons[i - 1].myID;
                    if (i < sideButtons.Count - 1)
                        sideButtons[i].downNeighborID = sideButtons[i + 1].myID;
                }

                // Determine grid width from player inventory
                // Standard inventory is 12 columns, but detect it from slot positions
                int playerCols = DetectGridColumns(menu.inventory?.inventory);
                int chestCols = DetectGridColumns(menu.ItemsToGrabMenu?.inventory);

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestNav] Detected grid columns — player: {playerCols}, chest: {chestCols}", LogLevel.Debug);

                // Wire rightmost player inventory slots to the nearest side button
                if (menu.inventory?.inventory != null && playerCols > 0)
                {
                    WireRightmostSlotsToButtons(menu.inventory.inventory, playerCols, sideButtons);
                }

                // Wire rightmost chest slots to the nearest side button
                if (menu.ItemsToGrabMenu?.inventory != null && chestCols > 0)
                {
                    WireRightmostSlotsToButtons(menu.ItemsToGrabMenu.inventory, chestCols, sideButtons);
                }

                Monitor.Log($"[ChestNav] Snap navigation fixed — {sideButtons.Count} side buttons wired", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[ChestNav] Error fixing snap navigation: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }
        }

        /// <summary>Detect number of columns in a slot grid by counting slots on the first row (same Y position).</summary>
        private static int DetectGridColumns(IList<ClickableComponent> slots)
        {
            if (slots == null || slots.Count == 0)
                return 0;

            int firstRowY = slots[0].bounds.Y;
            int cols = 0;
            foreach (var slot in slots)
            {
                if (slot.bounds.Y == firstRowY)
                    cols++;
                else
                    break;
            }
            return cols;
        }

        /// <summary>
        /// Wire the rightmost column of a slot grid to the side buttons.
        /// Each rightmost slot's rightNeighborID points to the vertically nearest side button.
        /// Each connected side button's leftNeighborID points back to the nearest rightmost slot.
        /// </summary>
        private static void WireRightmostSlotsToButtons(IList<ClickableComponent> slots, int cols, List<ClickableComponent> sideButtons)
        {
            // Find the rightmost slot in each row
            var rightmostSlots = new List<ClickableComponent>();
            for (int i = cols - 1; i < slots.Count; i += cols)
            {
                rightmostSlots.Add(slots[i]);
            }

            foreach (var slot in rightmostSlots)
            {
                // Find the side button closest vertically to this slot
                ClickableComponent nearest = null;
                int nearestDist = int.MaxValue;
                foreach (var btn in sideButtons)
                {
                    int dist = Math.Abs(slot.bounds.Center.Y - btn.bounds.Center.Y);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = btn;
                    }
                }

                if (nearest != null)
                {
                    slot.rightNeighborID = nearest.myID;

                    // Also set the button's leftNeighborID to point back
                    // Use the closest rightmost slot for each button
                    int existingDist = int.MaxValue;
                    if (nearest.leftNeighborID >= 0)
                    {
                        // Check if the existing left neighbor is closer than us
                        foreach (var s in rightmostSlots)
                        {
                            if (s.myID == nearest.leftNeighborID)
                            {
                                existingDist = Math.Abs(s.bounds.Center.Y - nearest.bounds.Center.Y);
                                break;
                            }
                        }
                    }

                    int ourDist = Math.Abs(slot.bounds.Center.Y - nearest.bounds.Center.Y);
                    if (nearest.leftNeighborID < 0 || ourDist < existingDist)
                    {
                        nearest.leftNeighborID = slot.myID;
                    }
                }
            }

            if (ModEntry.Config.VerboseLogging)
            {
                foreach (var slot in rightmostSlots)
                    Monitor.Log($"[ChestNav]   Wired slot {slot.myID} -> right={slot.rightNeighborID}", LogLevel.Debug);
            }
        }

        /// <summary>Prefix for receiveGamePadButton to handle chest management.</summary>
        /// <returns>False to block the original method (prevents Android X-delete bug), true to let it run.</returns>
        private static bool ReceiveGamePadButton_Prefix(ItemGrabMenu __instance, Buttons b)
        {
            try
            {
                // Skip shipping bins - they have their own handler in ShippingBinPatches
                if (__instance.shippingBin)
                    return true;

                // Remap button based on configured button style
                Buttons remapped = ButtonRemapper.Remap(b);

                // Log all button presses in chest menu for debugging
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"ItemGrabMenu button: {b} (remapped={remapped})", LogLevel.Debug);

                // X button (after remapping) = Sort chest (and block the original to prevent deletion)
                if (remapped == Buttons.X && ModEntry.Config.EnableSortFix)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"{b} remapped to X - sorting chest (blocking original)", LogLevel.Debug);
                    OrganizeChest(__instance);
                    return false; // Block original method to prevent item deletion
                }

                // Y button (after remapping) = Add to existing stacks
                if (remapped == Buttons.Y && ModEntry.Config.EnableAddToStacksFix)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"{b} remapped to Y - adding to stacks (blocking original)", LogLevel.Debug);
                    AddToExistingStacks(__instance);
                    return false; // Block original method
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error in chest controller handler: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }

            return true; // Let original method run for other buttons
        }

        /// <summary>Organize the chest contents.</summary>
        private static void OrganizeChest(ItemGrabMenu menu)
        {
            try
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("OrganizeChest called", LogLevel.Debug);

                // Try ItemGrabMenu.organizeItemsInList as static method
                var chestInventory = GetChestInventory(menu);
                if (chestInventory != null)
                {
                    try
                    {
                        ItemGrabMenu.organizeItemsInList(chestInventory);
                        Game1.playSound("Ship");
                        Monitor.Log("Sorted chest via organizeItemsInList", LogLevel.Info);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"organizeItemsInList failed: {ex.Message}", LogLevel.Debug);
                    }

                    // Fallback: manual sort
                    var sortedItems = chestInventory
                        .Where(i => i != null)
                        .OrderBy(i => i.Category)
                        .ThenBy(i => i.DisplayName)
                        .ToList();

                    for (int i = 0; i < chestInventory.Count; i++)
                    {
                        chestInventory[i] = i < sortedItems.Count ? sortedItems[i] : null;
                    }

                    Game1.playSound("Ship");
                    Monitor.Log("Sorted chest manually", LogLevel.Info);
                }
                else
                {
                    Monitor.Log("Could not get chest inventory", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error organizing chest: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Add matching items from inventory to existing stacks in chest.</summary>
        private static void AddToExistingStacks(ItemGrabMenu menu)
        {
            try
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("AddToExistingStacks called", LogLevel.Debug);

                // Try calling FillOutStacks method directly
                try
                {
                    var fillMethod = AccessTools.Method(typeof(ItemGrabMenu), "FillOutStacks");
                    if (fillMethod != null)
                    {
                        fillMethod.Invoke(menu, null);
                        Game1.playSound("Ship");
                        Monitor.Log("Called FillOutStacks directly", LogLevel.Info);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"FillOutStacks failed: {ex.Message}", LogLevel.Debug);
                }

                // Fallback: manually add to stacks
                var chestInventory = GetChestInventory(menu);
                if (chestInventory == null)
                {
                    Monitor.Log("Could not get chest inventory", LogLevel.Warn);
                    return;
                }

                bool anyAdded = false;

                for (int i = Game1.player.Items.Count - 1; i >= 0; i--)
                {
                    var playerItem = Game1.player.Items[i];
                    if (playerItem == null)
                        continue;

                    for (int j = 0; j < chestInventory.Count; j++)
                    {
                        var chestItem = chestInventory[j];
                        if (chestItem == null)
                            continue;

                        if (chestItem.canStackWith(playerItem))
                        {
                            int spaceInStack = chestItem.maximumStackSize() - chestItem.Stack;
                            if (spaceInStack > 0)
                            {
                                int toTransfer = Math.Min(spaceInStack, playerItem.Stack);
                                chestItem.Stack += toTransfer;
                                playerItem.Stack -= toTransfer;

                                if (playerItem.Stack <= 0)
                                {
                                    Game1.player.Items[i] = null;
                                }

                                anyAdded = true;
                            }
                        }
                    }
                }

                if (anyAdded)
                {
                    Game1.playSound("Ship");
                    Monitor.Log("Added items to existing stacks", LogLevel.Info);
                }
                else
                {
                    Game1.playSound("cancel");
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("No matching stacks found", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error adding to stacks: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Get the inventory list from the chest associated with this menu.</summary>
        private static IList<Item> GetChestInventory(ItemGrabMenu menu)
        {
            try
            {
                // Try ItemsToGrabMenu.actualInventory
                if (menu.ItemsToGrabMenu?.actualInventory != null)
                {
                    return menu.ItemsToGrabMenu.actualInventory;
                }

                // Try context (chest object)
                if (menu.context is StardewValley.Objects.Chest chest)
                {
                    return chest.Items;
                }

                return null;
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"Error getting chest inventory: {ex.Message}", LogLevel.Debug);
                return null;
            }
        }
    }

    /// <summary>Extension methods for SButton.</summary>
    internal static class SButtonExtensions
    {
        /// <summary>Try to get the controller button equivalent.</summary>
        public static bool TryGetController(this SButton button, out Buttons controllerButton)
        {
            switch (button)
            {
                case SButton.ControllerA:
                    controllerButton = Buttons.A;
                    return true;
                case SButton.ControllerB:
                    controllerButton = Buttons.B;
                    return true;
                case SButton.ControllerX:
                    controllerButton = Buttons.X;
                    return true;
                case SButton.ControllerY:
                    controllerButton = Buttons.Y;
                    return true;
                case SButton.ControllerBack:
                    controllerButton = Buttons.Back;
                    return true;
                case SButton.ControllerStart:
                    controllerButton = Buttons.Start;
                    return true;
                case SButton.LeftShoulder:
                    controllerButton = Buttons.LeftShoulder;
                    return true;
                case SButton.RightShoulder:
                    controllerButton = Buttons.RightShoulder;
                    return true;
                case SButton.LeftTrigger:
                    controllerButton = Buttons.LeftTrigger;
                    return true;
                case SButton.RightTrigger:
                    controllerButton = Buttons.RightTrigger;
                    return true;
                case SButton.LeftStick:
                    controllerButton = Buttons.LeftStick;
                    return true;
                case SButton.RightStick:
                    controllerButton = Buttons.RightStick;
                    return true;
                case SButton.DPadUp:
                    controllerButton = Buttons.DPadUp;
                    return true;
                case SButton.DPadDown:
                    controllerButton = Buttons.DPadDown;
                    return true;
                case SButton.DPadLeft:
                    controllerButton = Buttons.DPadLeft;
                    return true;
                case SButton.DPadRight:
                    controllerButton = Buttons.DPadRight;
                    return true;
                default:
                    controllerButton = default;
                    return false;
            }
        }
    }
}
