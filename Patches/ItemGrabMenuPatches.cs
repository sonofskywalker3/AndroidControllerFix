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

        /// <summary>Side button IDs discovered by FixSnapNavigation, used by A-button handler.</summary>
        private static HashSet<int> _sideButtonIds = new HashSet<int>();

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
        ///
        /// Root cause: Android's ItemGrabMenu creates sidebar buttons with runtime IDs
        /// (e.g. 12952, 27346, 5948, 4857) but the grid slots' neighborIDs reference
        /// hardcoded "ghost" IDs (105, 106, 54015, 54016) that don't match any component.
        /// The organize button isn't even assigned to the organizeButton field.
        /// This method rewires all the broken neighbor references.
        /// </summary>
        public static void FixSnapNavigation(ItemGrabMenu menu)
        {
            try
            {
                // Skip shipping bins — they have their own handler
                if (menu.shippingBin)
                    return;

                // Collect side buttons from field references
                var sideButtons = new List<ClickableComponent>();
                if (menu.fillStacksButton != null)
                    sideButtons.Add(menu.fillStacksButton);
                if (menu.colorPickerToggleButton != null)
                    sideButtons.Add(menu.colorPickerToggleButton);
                if (menu.trashCan != null)
                    sideButtons.Add(menu.trashCan);
                if (menu.organizeButton != null)
                    sideButtons.Add(menu.organizeButton);
                if (menu.specialButton != null)
                    sideButtons.Add(menu.specialButton);

                // The organize button often has NO field reference on Android (organizeButton=null).
                // Scan allClickableComponents for any button on the right side (X > 1200)
                // that isn't already in our list and isn't a grid slot or color swatch.
                var knownIds = new HashSet<int>();
                foreach (var btn in sideButtons)
                    knownIds.Add(btn.myID);
                // Also exclude grid slots and color swatches
                if (menu.inventory?.inventory != null)
                    foreach (var s in menu.inventory.inventory)
                        knownIds.Add(s.myID);
                if (menu.ItemsToGrabMenu?.inventory != null)
                    foreach (var s in menu.ItemsToGrabMenu.inventory)
                        knownIds.Add(s.myID);

                if (menu.allClickableComponents != null)
                {
                    foreach (var comp in menu.allClickableComponents)
                    {
                        if (knownIds.Contains(comp.myID))
                            continue;
                        if (comp.myID < 0) // skip offscreen placeholders (-500, etc.)
                            continue;
                        // Color swatches are small (80x72) and at specific Y ranges — skip them
                        if (comp.bounds.Width <= 80 && comp.bounds.Height <= 80 && comp.bounds.X < 1200)
                            continue;
                        // Side buttons are on the right edge of the screen (X >= ~1290)
                        if (comp.bounds.X >= 1200)
                        {
                            sideButtons.Add(comp);
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"[ChestNav] Discovered unlisted sidebar button: myID={comp.myID}, bounds={comp.bounds}", LogLevel.Debug);
                        }
                    }
                }

                // Sort by Y position (top to bottom)
                sideButtons.Sort((a, b) => a.bounds.Y.CompareTo(b.bounds.Y));

                if (ModEntry.Config.VerboseLogging)
                {
                    Monitor.Log($"[ChestNav] Side buttons ({sideButtons.Count} total, sorted by Y):", LogLevel.Debug);
                    foreach (var btn in sideButtons)
                        Monitor.Log($"[ChestNav]   myID={btn.myID}, bounds={btn.bounds}", LogLevel.Debug);
                }

                if (sideButtons.Count == 0)
                    return;

                // === Step 1: Chain side buttons vertically ===
                for (int i = 0; i < sideButtons.Count; i++)
                {
                    sideButtons[i].upNeighborID = i > 0 ? sideButtons[i - 1].myID : -1;
                    sideButtons[i].downNeighborID = i < sideButtons.Count - 1 ? sideButtons[i + 1].myID : -1;
                }

                // === Step 2: Fix chest grid rightmost slots → sidebar buttons ===
                // The game sets these to ghost IDs like 54015/54016 that don't exist.
                if (menu.ItemsToGrabMenu?.inventory != null)
                {
                    int chestCols = DetectGridColumns(menu.ItemsToGrabMenu.inventory);
                    if (chestCols > 0)
                    {
                        for (int i = chestCols - 1; i < menu.ItemsToGrabMenu.inventory.Count; i += chestCols)
                        {
                            var slot = menu.ItemsToGrabMenu.inventory[i];
                            var nearest = FindNearestByY(sideButtons, slot.bounds.Center.Y);
                            if (nearest != null)
                            {
                                slot.rightNeighborID = nearest.myID;
                                // Wire back: if this slot is closer than the button's current left
                                if (nearest.leftNeighborID < 0 || !IsValidComponent(menu, nearest.leftNeighborID))
                                    nearest.leftNeighborID = slot.myID;
                            }
                        }
                    }
                }

                // === Step 3: Fix player inventory rightmost slots → sidebar buttons ===
                // The game sets these to ghost IDs 105/106 that don't exist.
                if (menu.inventory?.inventory != null)
                {
                    int playerCols = DetectGridColumns(menu.inventory.inventory);
                    if (playerCols > 0)
                    {
                        for (int i = playerCols - 1; i < menu.inventory.inventory.Count; i += playerCols)
                        {
                            var slot = menu.inventory.inventory[i];
                            var nearest = FindNearestByY(sideButtons, slot.bounds.Center.Y);
                            if (nearest != null)
                            {
                                slot.rightNeighborID = nearest.myID;
                                if (nearest.leftNeighborID < 0 || !IsValidComponent(menu, nearest.leftNeighborID))
                                    nearest.leftNeighborID = slot.myID;
                            }
                        }
                    }
                }

                // === Step 4: Wire top button up to chest, bottom button down to player inventory ===
                if (menu.ItemsToGrabMenu?.inventory != null && menu.ItemsToGrabMenu.inventory.Count > 0)
                {
                    var topButton = sideButtons[0];
                    if (topButton.upNeighborID <= 0)
                    {
                        var nearest = FindNearestByY(menu.ItemsToGrabMenu.inventory, topButton.bounds.Center.Y);
                        if (nearest != null)
                            topButton.upNeighborID = nearest.myID;
                    }
                }
                if (menu.inventory?.inventory != null && menu.inventory.inventory.Count > 0)
                {
                    var bottomButton = sideButtons[sideButtons.Count - 1];
                    if (bottomButton.downNeighborID <= 0)
                    {
                        var nearest = FindNearestByY(menu.inventory.inventory, bottomButton.bounds.Center.Y);
                        if (nearest != null)
                            bottomButton.downNeighborID = nearest.myID;
                    }
                }

                if (ModEntry.Config.VerboseLogging)
                {
                    Monitor.Log($"[ChestNav] Final wiring:", LogLevel.Debug);
                    foreach (var btn in sideButtons)
                        Monitor.Log($"[ChestNav]   myID={btn.myID}: left={btn.leftNeighborID}, right={btn.rightNeighborID}, up={btn.upNeighborID}, down={btn.downNeighborID}", LogLevel.Debug);
                }
                // Store side button IDs for the A-button handler
                _sideButtonIds.Clear();
                foreach (var btn in sideButtons)
                    _sideButtonIds.Add(btn.myID);

                Monitor.Log($"[ChestNav] Navigation fixed — {sideButtons.Count} side buttons wired", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[ChestNav] Error fixing snap navigation: {ex.Message}", LogLevel.Error);
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }
        }

        /// <summary>Find the component nearest to a given Y coordinate.</summary>
        private static ClickableComponent FindNearestByY(IList<ClickableComponent> components, int targetY)
        {
            ClickableComponent nearest = null;
            int nearestDist = int.MaxValue;
            foreach (var comp in components)
            {
                int dist = Math.Abs(comp.bounds.Center.Y - targetY);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = comp;
                }
            }
            return nearest;
        }

        /// <summary>Check if a component ID actually exists in the menu's allClickableComponents.</summary>
        private static bool IsValidComponent(ItemGrabMenu menu, int componentId)
        {
            if (componentId < 0 || menu.allClickableComponents == null)
                return false;
            foreach (var comp in menu.allClickableComponents)
            {
                if (comp.myID == componentId)
                    return true;
            }
            return false;
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

                // A button on side buttons — simulate click at button position.
                // On Android, A fires receiveLeftClick at the mouse position which doesn't
                // track snap navigation. We intercept and click at the correct coordinates.
                if (remapped == Buttons.A && ModEntry.Config.EnableChestNavFix)
                {
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null && _sideButtonIds.Contains(snapped.myID))
                    {
                        int cx = snapped.bounds.Center.X;
                        int cy = snapped.bounds.Center.Y;
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"[ChestNav] A on side button {snapped.myID} — click at ({cx},{cy})", LogLevel.Debug);
                        __instance.receiveLeftClick(cx, cy);
                        return false;
                    }
                }

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
