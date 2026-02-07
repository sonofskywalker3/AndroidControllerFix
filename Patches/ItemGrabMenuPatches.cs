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

        /// <summary>Side button objects discovered by FixSnapNavigation, used by A-button handler.
        /// Stored by reference because some buttons share duplicate myID values.</summary>
        private static HashSet<ClickableComponent> _sideButtonObjects = new HashSet<ClickableComponent>();

        /// <summary>Reference to Close X button, handled specially (simulate B to close).</summary>
        private static ClickableComponent _closeXButton = null;

        /// <summary>Reference to color toggle button, handled specially (direct toggle).</summary>
        private static ClickableComponent _colorToggleButton = null;

        /// <summary>Flag to let a synthetic B press bypass our prefix.</summary>
        private static bool _bypassPrefix = false;

        // Unique IDs assigned to buttons with duplicate or sentinel myIDs
        private const int ID_SORT_CHEST = 54106;    // was 106
        private const int ID_SORT_INV = 54206;       // was 106
        private const int ID_TRASH_PLAYER = 54105;   // was 105
        private const int ID_CLOSE_X = 54500;        // was -500 (sentinel, not resolvable by getComponentWithID)

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

                _sideButtonObjects.Clear();
                bool verbose = ModEntry.Config.VerboseLogging;

                // =====================================================
                // Step 1: Get sidebar buttons by OBJECT REFERENCE
                // Do NOT scan allClickableComponents — the buttons we
                // need (organizeButton, trashCan) aren't in that list,
                // and IDs 105/106 are duplicated across chest/player.
                // =====================================================
                // InventoryMenu.organizeButton and .trashCan aren't publicly accessible
                // in this build — use Harmony reflection to grab them by field name.
                var organizeField = AccessTools.Field(typeof(InventoryMenu), "organizeButton");
                var trashCanField = AccessTools.Field(typeof(InventoryMenu), "trashCan");

                var sortChest = menu.ItemsToGrabMenu != null && organizeField != null
                    ? organizeField.GetValue(menu.ItemsToGrabMenu) as ClickableComponent
                    : null;                                              // ID 106, Y~86
                var fillStacks = menu.fillStacksButton;                  // ID 12952
                var colorToggle = menu.colorPickerToggleButton;          // ID 27346
                var sortInv = menu.inventory != null && organizeField != null
                    ? organizeField.GetValue(menu.inventory) as ClickableComponent
                    : null;                                              // ID 106, Y~471
                var trashPlayer = menu.inventory != null && trashCanField != null
                    ? trashCanField.GetValue(menu.inventory) as ClickableComponent
                    : null;                                              // ID 105, Y~673
                var closeX = menu.upperRightCloseButton;                 // ID -500

                if (verbose)
                {
                    Monitor.Log("[ChestNav] === v2.9.7 SIDEBAR BUTTONS ===", LogLevel.Debug);
                    LogComponent("sortChest", sortChest);
                    LogComponent("fillStacks", fillStacks);
                    LogComponent("colorToggle", colorToggle);
                    LogComponent("sortInv", sortInv);
                    LogComponent("trashPlayer", trashPlayer);
                    LogComponent("closeX", closeX);
                }

                // =====================================================
                // Step 2: Assign unique IDs to resolve duplicates
                // =====================================================
                if (sortChest != null) sortChest.myID = ID_SORT_CHEST;
                if (sortInv != null) sortInv.myID = ID_SORT_INV;
                if (trashPlayer != null) trashPlayer.myID = ID_TRASH_PLAYER;
                if (closeX != null) closeX.myID = ID_CLOSE_X;

                if (verbose)
                    Monitor.Log($"[ChestNav] Reassigned IDs: sortChest={ID_SORT_CHEST}, sortInv={ID_SORT_INV}, trashPlayer={ID_TRASH_PLAYER}, closeX={ID_CLOSE_X}", LogLevel.Debug);

                // =====================================================
                // Step 2b: Register buttons in allClickableComponents
                // The game's getComponentWithID() searches this list.
                // sortChest, sortInv, and trashPlayer aren't in it by
                // default, so snap navigation can't find them by ID.
                // fillStacks and colorToggle are already present.
                // =====================================================
                if (menu.allClickableComponents != null)
                {
                    // Add missing buttons (avoid duplicates by checking if already present)
                    void EnsureRegistered(ClickableComponent comp, string label)
                    {
                        if (comp != null && !menu.allClickableComponents.Contains(comp))
                        {
                            menu.allClickableComponents.Add(comp);
                            if (verbose)
                                Monitor.Log($"[ChestNav] Registered {label} (ID={comp.myID}) in allClickableComponents", LogLevel.Debug);
                        }
                    }
                    EnsureRegistered(sortChest, "sortChest");
                    EnsureRegistered(sortInv, "sortInv");
                    EnsureRegistered(trashPlayer, "trashPlayer");
                    EnsureRegistered(closeX, "closeX");
                }

                // =====================================================
                // Step 3: Wire sidebar vertical chain
                // Sort Chest ↔ Fill Stacks ↔ Color Toggle ↔ Sort Inv ↔ Trash Player
                // Close X sits above Sort Chest
                // =====================================================

                // Detect grid layout for neighbor references
                int chestCols = 0;
                if (menu.ItemsToGrabMenu?.inventory != null)
                    chestCols = DetectGridColumns(menu.ItemsToGrabMenu.inventory);
                int playerCols = 0;
                if (menu.inventory?.inventory != null)
                    playerCols = DetectGridColumns(menu.inventory.inventory);

                // Helper: get rightmost slot in a given row
                ClickableComponent GetRightmost(IList<ClickableComponent> grid, int cols, int row)
                {
                    if (grid == null || cols <= 0) return null;
                    int idx = (row + 1) * cols - 1;
                    return idx < grid.Count ? grid[idx] : null;
                }

                var chestRow0Right = GetRightmost(menu.ItemsToGrabMenu?.inventory, chestCols, 0); // 53921
                var chestRow1Right = GetRightmost(menu.ItemsToGrabMenu?.inventory, chestCols, 1); // 53933
                var chestRow2Right = GetRightmost(menu.ItemsToGrabMenu?.inventory, chestCols, 2); // 53945
                var playerRow0Right = GetRightmost(menu.inventory?.inventory, playerCols, 0);      // 11
                var playerRow1Right = GetRightmost(menu.inventory?.inventory, playerCols, 1);      // 23
                var playerRow2Right = GetRightmost(menu.inventory?.inventory, playerCols, 2);      // 35

                // --- Sort Chest ---
                if (sortChest != null)
                {
                    sortChest.leftNeighborID = chestRow0Right?.myID ?? sortChest.leftNeighborID;
                    sortChest.rightNeighborID = closeX?.myID ?? sortChest.rightNeighborID;
                    sortChest.upNeighborID = chestRow0Right?.myID ?? sortChest.upNeighborID;
                    sortChest.downNeighborID = fillStacks?.myID ?? sortChest.downNeighborID;
                    _sideButtonObjects.Add(sortChest);
                }

                // --- Fill Stacks ---
                if (fillStacks != null)
                {
                    fillStacks.leftNeighborID = chestRow1Right?.myID ?? fillStacks.leftNeighborID;
                    fillStacks.upNeighborID = sortChest?.myID ?? fillStacks.upNeighborID;
                    fillStacks.downNeighborID = colorToggle?.myID ?? fillStacks.downNeighborID;
                    _sideButtonObjects.Add(fillStacks);
                }

                // --- Color Toggle ---
                if (colorToggle != null)
                {
                    colorToggle.leftNeighborID = chestRow2Right?.myID ?? colorToggle.leftNeighborID;
                    colorToggle.upNeighborID = fillStacks?.myID ?? colorToggle.upNeighborID;
                    colorToggle.downNeighborID = sortInv?.myID ?? colorToggle.downNeighborID;
                    _sideButtonObjects.Add(colorToggle);
                }

                // --- Sort Inventory ---
                if (sortInv != null)
                {
                    sortInv.leftNeighborID = playerRow0Right?.myID ?? sortInv.leftNeighborID;
                    sortInv.upNeighborID = colorToggle?.myID ?? sortInv.upNeighborID;
                    sortInv.downNeighborID = trashPlayer?.myID ?? sortInv.downNeighborID;
                    _sideButtonObjects.Add(sortInv);
                }

                // --- Trash Player ---
                if (trashPlayer != null)
                {
                    trashPlayer.leftNeighborID = playerRow1Right?.myID ?? trashPlayer.leftNeighborID;
                    trashPlayer.upNeighborID = sortInv?.myID ?? trashPlayer.upNeighborID;
                    trashPlayer.downNeighborID = playerRow2Right?.myID ?? trashPlayer.downNeighborID;
                    _sideButtonObjects.Add(trashPlayer);
                }

                // --- Close X ---
                if (closeX != null)
                {
                    closeX.leftNeighborID = sortChest?.myID ?? closeX.leftNeighborID;
                    closeX.downNeighborID = sortChest?.myID ?? closeX.downNeighborID;
                    _sideButtonObjects.Add(closeX);
                }

                // Store references for special A-button handling
                _closeXButton = closeX;
                _colorToggleButton = colorToggle;

                // =====================================================
                // Step 4: Wire grid rightmost → sidebar (RIGHT)
                // Do NOT touch -7777 sentinel values on up/down.
                // =====================================================
                if (chestRow0Right != null && sortChest != null)
                    chestRow0Right.rightNeighborID = sortChest.myID;
                if (chestRow1Right != null && fillStacks != null)
                    chestRow1Right.rightNeighborID = fillStacks.myID;
                if (chestRow2Right != null && colorToggle != null)
                    chestRow2Right.rightNeighborID = colorToggle.myID;
                if (playerRow0Right != null && sortInv != null)
                    playerRow0Right.rightNeighborID = sortInv.myID;
                if (playerRow1Right != null && trashPlayer != null)
                    playerRow1Right.rightNeighborID = trashPlayer.myID;
                if (playerRow2Right != null && trashPlayer != null)
                    playerRow2Right.rightNeighborID = trashPlayer.myID;

                // =====================================================
                // Step 5: Verbose logging of final wiring
                // =====================================================
                if (verbose)
                {
                    Monitor.Log("[ChestNav] === FINAL WIRING ===", LogLevel.Debug);
                    void LogWiring(string label, ClickableComponent c)
                    {
                        if (c != null)
                            Monitor.Log($"[ChestNav]   {label}: myID={c.myID}, L={c.leftNeighborID}, R={c.rightNeighborID}, U={c.upNeighborID}, D={c.downNeighborID}", LogLevel.Debug);
                    }
                    LogWiring("Sort Chest", sortChest);
                    LogWiring("Fill Stacks", fillStacks);
                    LogWiring("Color Toggle", colorToggle);
                    LogWiring("Sort Inv", sortInv);
                    LogWiring("Trash Player", trashPlayer);
                    LogWiring("Close X", closeX);
                    LogWiring("Chest R0 rightmost", chestRow0Right);
                    LogWiring("Chest R1 rightmost", chestRow1Right);
                    LogWiring("Chest R2 rightmost", chestRow2Right);
                    LogWiring("Player R0 rightmost", playerRow0Right);
                    LogWiring("Player R1 rightmost", playerRow1Right);
                    LogWiring("Player R2 rightmost", playerRow2Right);
                }

                Monitor.Log($"[ChestNav] v2.9.7 navigation wired — {_sideButtonObjects.Count} sidebar buttons registered", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[ChestNav] Error in FixSnapNavigation: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
            }
        }

        /// <summary>Log a named component's details (or null).</summary>
        private static void LogComponent(string name, ClickableComponent comp)
        {
            if (comp == null)
            {
                Monitor.Log($"[ChestNav]   {name} = NULL", LogLevel.Debug);
            }
            else
            {
                string hover = (comp is ClickableTextureComponent ctc && ctc.hoverText != null) ? $", hover=\"{ctc.hoverText}\"" : "";
                Monitor.Log($"[ChestNav]   {name}: myID={comp.myID}, name=\"{comp.name ?? "(null)"}\", bounds={comp.bounds}, right={comp.rightNeighborID}, left={comp.leftNeighborID}, up={comp.upNeighborID}, down={comp.downNeighborID}{hover}", LogLevel.Debug);
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

                // Allow synthetic B presses to pass through to the original method
                if (_bypassPrefix)
                {
                    _bypassPrefix = false;
                    return true;
                }

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
                    if (snapped != null && _sideButtonObjects.Contains(snapped))
                    {
                        // --- Close X: simulate B press instead of receiveLeftClick ---
                        // receiveLeftClick at the X button closes the menu, but A also
                        // fires as "interact" in the overworld, immediately reopening
                        // the chest. Sending B through the original handler closes the
                        // menu via the normal path, and B doesn't trigger interact.
                        if (snapped == _closeXButton)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("[ChestNav] A on Close X — simulating B press to close", LogLevel.Debug);
                            _bypassPrefix = true;
                            __instance.receiveGamePadButton(Buttons.B);
                            return false;
                        }

                        // --- Color Toggle: direct toggle instead of receiveLeftClick ---
                        // receiveLeftClick sets chestColorPicker.visible=True internally
                        // but doesn't produce visual button feedback or actually render
                        // the picker on Android. Use the game's actual toggle path.
                        if (snapped == _colorToggleButton && __instance.chestColorPicker != null)
                        {
                            if (ModEntry.Config.VerboseLogging)
                            {
                                bool visBefore = __instance.chestColorPicker.visible;
                                Monitor.Log($"[ChestNav] A on Color Toggle — direct toggle, picker visible={visBefore}", LogLevel.Debug);
                            }

                            // Toggle the picker visibility (same as the game does)
                            __instance.chestColorPicker.visible = !__instance.chestColorPicker.visible;
                            Game1.playSound("drumkit6");

                            if (ModEntry.Config.VerboseLogging)
                            {
                                bool visAfter = __instance.chestColorPicker.visible;
                                Monitor.Log($"[ChestNav] Color picker now visible={visAfter}, type={__instance.chestColorPicker.GetType().Name}, bounds=({__instance.chestColorPicker.xPositionOnScreen},{__instance.chestColorPicker.yPositionOnScreen},{__instance.chestColorPicker.width},{__instance.chestColorPicker.height})", LogLevel.Debug);
                            }
                            return false;
                        }

                        // --- All other side buttons: receiveLeftClick at button center ---
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
