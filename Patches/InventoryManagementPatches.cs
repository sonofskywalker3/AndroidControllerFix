using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Handles console-style inventory management via controller.
    /// Makes the A button work like Nintendo Switch:
    /// - A on item = pick up to cursor (visually attached, draggable)
    /// - A on another slot = swap/place item
    /// - A on empty slot = place item
    ///
    /// Y button for single-stack pickup:
    /// - Y on stack = pick up 1 item from stack
    /// - Hold Y = continue picking up 1 at a time
    /// </summary>
    internal static class InventoryManagementPatches
    {
        private static IMonitor Monitor;

        // Track whether we're currently "holding" an item
        private static bool IsHoldingItem = false;

        // The source slot we picked up from (for visual feedback)
        private static int SourceSlotId = -1;

        // For Y button hold detection
        private static bool IsYButtonHeld = false;
        private static int YButtonHoldTicks = 0;
        private const int YButtonHoldDelay = 15; // Ticks before repeat (about 250ms at 60fps)
        private const int YButtonRepeatRate = 8;  // Ticks between repeats (about 133ms at 60fps)

        // For controller hover tooltip
        private static int LastSnappedComponentId = -1;

        // Track previous Y button state for edge detection
        private static bool WasYButtonDown = false;

        // Track the slot we're picking from with Y button hold
        private static int YButtonHoldSlotId = -1;

        // Track previous A button state for blocking hold behavior
        private static bool WasAButtonDown = false;

        // Cached A-button state from OnUpdateTicked — avoids redundant GamePad.GetState() in draw postfix
        private static bool CachedAButtonDown = false;

        // When HandleAButton declines to process a non-inventory slot (equipment, sort, trash),
        // this flag tells the prefix patches in InventoryPagePatches to let the A press through
        // to the game's own handler. Cleared each tick in OnUpdateTicked.
        public static bool AllowGameAPress = false;

        // Cached reflection fields for hover/tooltip (avoids per-tick AccessTools.Field lookups)
        private static FieldInfo InvPage_HoverTextField;
        private static FieldInfo InvPage_HoverTitleField;
        private static FieldInfo InvPage_HoveredItemField;
        private static FieldInfo InvMenu_HoveredItemField;

        /// <summary>Apply Harmony patches for cursor item rendering.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection lookups for hover/tooltip fields
            InvPage_HoverTextField = AccessTools.Field(typeof(InventoryPage), "hoverText");
            InvPage_HoverTitleField = AccessTools.Field(typeof(InventoryPage), "hoverTitle");
            InvPage_HoveredItemField = AccessTools.Field(typeof(InventoryPage), "hoveredItem");
            InvMenu_HoveredItemField = AccessTools.Field(typeof(InventoryMenu), "hoveredItem");

            try
            {
                // Patch InventoryPage.draw to render our held item on cursor
                harmony.Patch(
                    original: AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.draw), new[] { typeof(SpriteBatch) }),
                    postfix: new HarmonyMethod(typeof(InventoryManagementPatches), nameof(InventoryPage_Draw_Postfix))
                );

                // Note: A button blocking is handled in InventoryPagePatches to avoid duplicate patches

                Monitor.Log("InventoryManagement patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply InventoryManagement patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Postfix for InventoryPage.draw to render the held item and tooltips.
        /// </summary>
        private static void InventoryPage_Draw_Postfix(InventoryPage __instance, SpriteBatch b)
        {
            if (!ModEntry.Config.EnableConsoleInventory)
                return;

            // Draw held item at cursor position (bottom-right of the slot, like console)
            if (IsHoldingItem && Game1.player.CursorSlotItem != null)
            {
                try
                {
                    // Get the current snapped component to determine cursor position
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null)
                    {
                        // Position item at bottom-right corner of the slot
                        // This matches console behavior where the item follows the cursor
                        int itemX = snapped.bounds.X + snapped.bounds.Width - 16;
                        int itemY = snapped.bounds.Y + snapped.bounds.Height - 16;

                        // Draw the item slightly larger for visibility
                        Game1.player.CursorSlotItem.drawInMenu(
                            b,
                            new Vector2(itemX, itemY),
                            0.75f,  // Slightly smaller scale to not overlap too much
                            1f,     // Transparency
                            0.9f,   // Layer depth (draw on top)
                            StackDrawType.Draw,
                            Color.White,
                            true    // Draw shadow
                        );
                    }
                }
                catch (Exception ex)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor?.Log($"InventoryManagement: Draw held item error: {ex.Message}", LogLevel.Debug);
                }
            }

            // Draw tooltip for hovered item (console-style hover tooltips)
            // Show when:
            // - NOT holding an item and A button is not pressed, OR
            // - Holding bait/tackle and hovering over a fishing rod (to see rod info)
            try
            {
                if (!CachedAButtonDown)
                {
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null && snapped.myID >= 0 && snapped.myID < Game1.player.Items.Count)
                    {
                        Item hoveredItem = Game1.player.Items[snapped.myID];
                        if (hoveredItem != null)
                        {
                            bool shouldDrawTooltip = false;

                            if (!IsHoldingItem)
                            {
                                // Not holding anything - always show tooltip
                                shouldDrawTooltip = true;
                            }
                            else if (hoveredItem is FishingRod)
                            {
                                // Holding something and hovering over fishing rod
                                // Only show tooltip if holding bait or tackle
                                Item cursorItem = Game1.player.CursorSlotItem;
                                if (cursorItem != null && (cursorItem.Category == -21 || cursorItem.Category == -22))
                                {
                                    // -21 = Bait, -22 = Tackle
                                    shouldDrawTooltip = true;
                                }
                            }

                            if (shouldDrawTooltip)
                            {
                                // Draw tooltip using game's built-in method
                                IClickableMenu.drawToolTip(
                                    b,
                                    hoveredItem.getDescription(),
                                    hoveredItem.DisplayName,
                                    hoveredItem,
                                    false,  // heldItem
                                    -1,     // currencySymbol
                                    0,      // extraItemToShowIndex
                                    null,   // extraItemToShowAmount
                                    -1,     // moneyAmountToDisplayAtBottom
                                    null,   // boldTitleText
                                    -1      // healAmountToDisplay
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Draw tooltip error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Called every tick while in inventory menu.
        /// Handles Y button hold-to-repeat for single stack pickup.
        /// Also handles controller hover tooltips.
        /// </summary>
        public static void OnUpdateTicked()
        {
            // Poll Y button state directly for hold detection
            // This is more reliable than SMAPI events on Android
            GamePadState gpState = GamePad.GetState(PlayerIndex.One);
            bool isYButtonDown = gpState.Buttons.Y == ButtonState.Pressed;

            // Also check remapped button (if using Xbox layout, Y might be remapped)
            // For now, check raw Y button state

            if (isYButtonDown && !WasYButtonDown)
            {
                // Y button just pressed - do initial pickup and start hold tracking
                IsYButtonHeld = true;
                YButtonHoldTicks = 0;

                // Store which slot we're on for held pickup
                if (Game1.activeClickableMenu is GameMenu gm && gm.currentTab == GameMenu.inventoryTab)
                {
                    var invPage = gm.pages[GameMenu.inventoryTab] as InventoryPage;
                    if (invPage?.currentlySnappedComponent != null)
                    {
                        YButtonHoldSlotId = invPage.currentlySnappedComponent.myID;

                        // Do the initial single-item pickup
                        if (YButtonHoldSlotId >= 0 && YButtonHoldSlotId < Game1.player.Items.Count)
                        {
                            Item item = Game1.player.Items[YButtonHoldSlotId];
                            if (item != null)
                            {
                                // Check if fishing rod is hovered - let fishing rod patches handle that
                                if (item is not FishingRod)
                                {
                                    PickupSingleItem(invPage, YButtonHoldSlotId, item);
                                }
                            }
                        }
                    }
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Y button pressed (poll), slot={YButtonHoldSlotId}", LogLevel.Debug);
            }
            else if (!isYButtonDown && WasYButtonDown)
            {
                // Y button just released
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: Y button released (poll) after {YButtonHoldTicks} ticks", LogLevel.Debug);

                IsYButtonHeld = false;
                YButtonHoldTicks = 0;
                YButtonHoldSlotId = -1;
            }

            WasYButtonDown = isYButtonDown;

            // Handle Y button hold for continuous single-stack pickup
            if (IsYButtonHeld && isYButtonDown)
            {
                YButtonHoldTicks++;

                if (ModEntry.Config.VerboseLogging && YButtonHoldTicks % 30 == 0)
                {
                    Monitor?.Log($"InventoryManagement: Y held for {YButtonHoldTicks} ticks, slot={YButtonHoldSlotId}", LogLevel.Debug);
                }

                // After initial delay, repeat at regular intervals
                if (YButtonHoldTicks > YButtonHoldDelay &&
                    (YButtonHoldTicks - YButtonHoldDelay) % YButtonRepeatRate == 0)
                {
                    // Try to pick up another single item from the SAME slot we started on
                    if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor?.Log($"InventoryManagement: Y hold repeat at tick {YButtonHoldTicks}", LogLevel.Debug);
                        TryPickupSingleFromSlot(gameMenu, YButtonHoldSlotId);
                    }
                }
            }

            // Block A button hold tooltip behavior (Android-specific)
            bool isAButtonDown = gpState.Buttons.A == ButtonState.Pressed;
            CachedAButtonDown = isAButtonDown;
            if (ModEntry.Config.EnableConsoleInventory && isAButtonDown)
            {
                // Clear any tooltip/hover state when A is held to prevent Android tooltip popup
                if (Game1.activeClickableMenu is GameMenu gm && gm.currentTab == GameMenu.inventoryTab)
                {
                    var invPage = gm.pages[GameMenu.inventoryTab] as InventoryPage;
                    if (invPage != null)
                    {
                        ClearHoverState(invPage);
                    }
                }
            }
            WasAButtonDown = isAButtonDown;

            // Handle controller hover tooltips - trigger when snapped component changes
            // But only when A button is NOT held
            if (ModEntry.Config.EnableConsoleInventory && !isAButtonDown)
            {
                TriggerHoverTooltip();
            }

            // Sync holding state: if we think we're holding but CursorSlotItem is gone,
            // the game consumed it (e.g. equipped to an equipment slot, trashed it).
            if (IsHoldingItem && Game1.player.CursorSlotItem == null)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log("InventoryManagement: CursorSlotItem cleared externally, syncing hold state", LogLevel.Debug);
                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            // Clear the pass-through flag. It was set during HandleAButton (via OnButtonsChanged)
            // and consumed by prefix patches during this tick's input processing.
            AllowGameAPress = false;
        }

        /// <summary>
        /// Clear the hover/tooltip state to prevent tooltips from appearing.
        /// </summary>
        private static void ClearHoverState(InventoryPage inventoryPage)
        {
            try
            {
                // Clear hover text and title using cached fields
                InvPage_HoverTextField?.SetValue(inventoryPage, "");
                InvPage_HoverTitleField?.SetValue(inventoryPage, "");
                InvPage_HoveredItemField?.SetValue(inventoryPage, null);

                // Also clear from inventory menu
                if (inventoryPage.inventory != null)
                {
                    InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, null);
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: ClearHoverState error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Try to pick up a single item from a specific slot (for hold-repeat).
        /// </summary>
        private static void TryPickupSingleFromSlot(GameMenu gameMenu, int slotId)
        {
            try
            {
                if (slotId < 0 || slotId >= Game1.player.Items.Count)
                    return;

                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return;

                Item item = Game1.player.Items[slotId];
                if (item == null)
                    return;

                PickupSingleItem(inventoryPage, slotId, item);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: TryPickupSingleFromSlot error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Trigger hover tooltip when controller cursor moves to a new slot.
        /// This makes tooltips appear on hover like on console, not just on press-and-hold.
        /// </summary>
        private static void TriggerHoverTooltip()
        {
            try
            {
                if (!(Game1.activeClickableMenu is GameMenu gameMenu) || gameMenu.currentTab != GameMenu.inventoryTab)
                    return;

                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return;

                var snapped = inventoryPage.currentlySnappedComponent;
                if (snapped == null)
                {
                    LastSnappedComponentId = -1;
                    return;
                }

                // Check if we moved to a different component
                if (snapped.myID != LastSnappedComponentId)
                {
                    LastSnappedComponentId = snapped.myID;

                    // Calculate the center of the snapped component
                    int hoverX = snapped.bounds.X + snapped.bounds.Width / 2;
                    int hoverY = snapped.bounds.Y + snapped.bounds.Height / 2;

                    // Call performHoverAction to trigger tooltip display
                    inventoryPage.performHoverAction(hoverX, hoverY);

                    // Also try to directly set hoveredItem for inventory slots
                    if (snapped.myID >= 0 && snapped.myID < Game1.player.Items.Count)
                    {
                        Item item = Game1.player.Items[snapped.myID];
                        if (item != null && inventoryPage.inventory != null)
                        {
                            // Set hover state using cached fields
                            InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, item);
                            InvPage_HoverTextField?.SetValue(inventoryPage, item.getDescription());
                            InvPage_HoverTitleField?.SetValue(inventoryPage, item.DisplayName);
                            InvPage_HoveredItemField?.SetValue(inventoryPage, item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log($"InventoryManagement: TriggerHoverTooltip error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Handle A button press for console-style inventory management.
        /// Returns true if the input was handled and should be suppressed.
        /// </summary>
        public static bool HandleAButton(GameMenu gameMenu, IMonitor monitor)
        {
            Monitor = monitor;

            if (!ModEntry.Config.EnableConsoleInventory)
                return false;

            try
            {
                var inventoryPage = gameMenu.pages[GameMenu.inventoryTab] as InventoryPage;
                if (inventoryPage == null)
                    return false;

                var snapped = inventoryPage.currentlySnappedComponent;
                if (snapped == null)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log("InventoryManagement: No snapped component", LogLevel.Debug);
                    return false;
                }

                int slotId = snapped.myID;

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: A pressed, slot={slotId}, holding={IsHoldingItem}", LogLevel.Debug);

                // Check if this is a valid inventory slot (0-35 for main inventory)
                bool isInventorySlot = slotId >= 0 && slotId < Game1.player.Items.Count;

                if (IsHoldingItem)
                {
                    // We're holding an item - place it
                    return PlaceItem(inventoryPage, slotId, isInventorySlot);
                }
                else
                {
                    // Not holding - try to pick up item
                    if (!isInventorySlot)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Slot {slotId} is not inventory slot, passing through to game", LogLevel.Debug);
                        AllowGameAPress = true;
                        return false;
                    }

                    Item item = Game1.player.Items[slotId];
                    if (item == null)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Slot {slotId} is empty, nothing to pick up", LogLevel.Debug);
                        return false;
                    }

                    return PickUpItem(inventoryPage, slotId, item);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement HandleAButton error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Pick up a single item from a stack (Y button behavior).
        /// </summary>
        private static bool PickupSingleItem(InventoryPage inventoryPage, int slotId, Item item)
        {
            try
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: Y pickup single from slot {slotId}, item={item.Name}, stack={item.Stack}", LogLevel.Debug);

                Item cursorItem = Game1.player.CursorSlotItem;

                // If already holding something
                if (cursorItem != null)
                {
                    // Can only add if same item type and stackable
                    if (cursorItem.canStackWith(item))
                    {
                        // Check if cursor item can hold more
                        if (cursorItem.Stack < cursorItem.maximumStackSize())
                        {
                            cursorItem.Stack++;

                            // Reduce source stack
                            item.Stack--;
                            if (item.Stack <= 0)
                            {
                                Game1.player.Items[slotId] = null;
                            }

                            Game1.playSound("dwop");
                            Monitor.Log($"InventoryManagement: Added 1 to cursor stack (now {cursorItem.Stack})", LogLevel.Debug);
                            return true;
                        }
                        else
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"InventoryManagement: Cursor stack full", LogLevel.Debug);
                            Game1.playSound("cancel");
                            return true;
                        }
                    }
                    else
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Cannot stack different items", LogLevel.Debug);
                        return false;
                    }
                }
                else
                {
                    // Not holding anything - pick up 1 from stack
                    if (item.Stack > 1)
                    {
                        // Create a copy with stack of 1
                        Item singleItem = item.getOne();
                        singleItem.Stack = 1;

                        // Put on cursor
                        Game1.player.CursorSlotItem = singleItem;

                        // Reduce source stack
                        item.Stack--;

                        IsHoldingItem = true;
                        SourceSlotId = slotId;

                        Game1.playSound("dwop");
                        Monitor.Log($"InventoryManagement: Picked up 1 {singleItem.Name}, source stack now {item.Stack}", LogLevel.Info);
                    }
                    else
                    {
                        // Only 1 item - pick up entire thing (same as A button)
                        Game1.player.CursorSlotItem = item;
                        Game1.player.Items[slotId] = null;

                        IsHoldingItem = true;
                        SourceSlotId = slotId;

                        Game1.playSound("dwop");
                        Monitor.Log($"InventoryManagement: Picked up last {item.Name}", LogLevel.Info);
                    }

                    // Clear selection to remove red box from source
                    ClearInventorySelection(inventoryPage);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PickupSingleItem error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Pick up an entire item stack (A button).
        /// </summary>
        private static bool PickUpItem(InventoryPage inventoryPage, int slotId, Item item)
        {
            try
            {
                Monitor.Log($"InventoryManagement: Picking up {item.Name} (x{item.Stack}) from slot {slotId}", LogLevel.Debug);

                // Move item from inventory to cursor
                Game1.player.CursorSlotItem = item;
                Game1.player.Items[slotId] = null;

                IsHoldingItem = true;
                SourceSlotId = slotId;

                // Clear selection to remove red box from source slot
                ClearInventorySelection(inventoryPage);

                Game1.playSound("pickUpItem");
                Monitor.Log($"InventoryManagement: Now holding {item.Name}", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PickUpItem error: {ex.Message}", LogLevel.Error);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Place the held item at the target slot.
        /// </summary>
        private static bool PlaceItem(InventoryPage inventoryPage, int targetSlotId, bool isInventorySlot)
        {
            try
            {
                Item heldItem = Game1.player.CursorSlotItem;

                if (heldItem == null)
                {
                    Monitor.Log($"InventoryManagement: No item on cursor to place", LogLevel.Debug);
                    CancelHold();
                    return false;
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: Placing {heldItem.Name} at slot {targetSlotId}", LogLevel.Debug);

                // Handle non-inventory slots (equipment slots, trash, etc.)
                // The game's receiveLeftClick uses mouse coordinates to determine what was
                // clicked, but with controller snap navigation the mouse isn't at the snapped
                // component. We call receiveLeftClick ourselves with the correct coordinates.
                if (!isInventorySlot)
                {
                    var snapped = inventoryPage.currentlySnappedComponent;
                    if (snapped != null)
                    {
                        int clickX = snapped.bounds.X + snapped.bounds.Width / 2;
                        int clickY = snapped.bounds.Y + snapped.bounds.Height / 2;

                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log($"InventoryManagement: Target slot {targetSlotId} is not inventory, calling receiveLeftClick at ({clickX},{clickY})", LogLevel.Debug);

                        // Set flag so our prefix patches allow this receiveLeftClick through
                        AllowGameAPress = true;
                        inventoryPage.receiveLeftClick(clickX, clickY, true);

                        // Sync holding state — the game may have consumed CursorSlotItem
                        // (equipped item, trashed it, etc.)
                        if (Game1.player.CursorSlotItem == null)
                        {
                            IsHoldingItem = false;
                            SourceSlotId = -1;
                            Monitor.Log($"InventoryManagement: Game consumed held item at slot {targetSlotId}", LogLevel.Info);
                        }
                        else if (Game1.player.CursorSlotItem != heldItem)
                        {
                            // Game swapped the item (e.g. replacing equipped item)
                            Monitor.Log($"InventoryManagement: Game swapped held item, now holding {Game1.player.CursorSlotItem.Name}", LogLevel.Info);
                        }
                    }
                    return true;
                }

                Item targetItem = Game1.player.Items[targetSlotId];

                if (targetItem == null)
                {
                    // Empty slot - place item
                    Game1.player.Items[targetSlotId] = heldItem;
                    Game1.player.CursorSlotItem = null;

                    IsHoldingItem = false;
                    SourceSlotId = -1;

                    Game1.playSound("stoneStep");
                    Monitor.Log($"InventoryManagement: Placed {heldItem.Name} in empty slot {targetSlotId}", LogLevel.Info);
                }
                else if (targetItem.canStackWith(heldItem))
                {
                    // Same item type - try to stack
                    int spaceAvailable = targetItem.maximumStackSize() - targetItem.Stack;
                    int toAdd = Math.Min(spaceAvailable, heldItem.Stack);

                    if (toAdd > 0)
                    {
                        targetItem.Stack += toAdd;
                        heldItem.Stack -= toAdd;

                        if (heldItem.Stack <= 0)
                        {
                            Game1.player.CursorSlotItem = null;
                            IsHoldingItem = false;
                            SourceSlotId = -1;
                        }

                        Game1.playSound("stoneStep");
                        Monitor.Log($"InventoryManagement: Stacked {toAdd}x {heldItem.Name} (target now {targetItem.Stack})", LogLevel.Info);
                    }
                    else
                    {
                        // Stack is full - swap instead
                        Game1.player.Items[targetSlotId] = heldItem;
                        Game1.player.CursorSlotItem = targetItem;
                        // Still holding (the swapped item)
                        Game1.playSound("stoneStep");
                        Monitor.Log($"InventoryManagement: Stack full, swapped with {targetItem.Name}", LogLevel.Info);
                    }
                }
                else
                {
                    // Different items - swap
                    Game1.player.Items[targetSlotId] = heldItem;
                    Game1.player.CursorSlotItem = targetItem;

                    // Still holding the swapped item
                    Game1.playSound("stoneStep");
                    Monitor.Log($"InventoryManagement: Swapped, now holding {targetItem.Name}", LogLevel.Info);
                }

                return true;
            }
            catch (Exception ex)
            {
                Monitor.Log($"InventoryManagement PlaceItem error: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.StackTrace, LogLevel.Debug);
                CancelHold();
                return false;
            }
        }

        /// <summary>
        /// Clear the inventory selection highlight (red box).
        /// </summary>
        private static void ClearInventorySelection(InventoryPage inventoryPage)
        {
            try
            {
                if (inventoryPage.inventory != null)
                {
                    InvMenu_HoveredItemField?.SetValue(inventoryPage.inventory, null);
                }

                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("InventoryManagement: Cleared selection highlight", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"InventoryManagement: ClearSelection error: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Cancel any active hold state (e.g., when leaving menu).
        /// </summary>
        public static void CancelHold()
        {
            if (IsHoldingItem)
            {
                if (ModEntry.Config.VerboseLogging)
                    Monitor?.Log("InventoryManagement: Cancelling hold state", LogLevel.Debug);

                // If we still have a cursor item, put it back somewhere safe
                if (Game1.player.CursorSlotItem != null)
                {
                    Item item = Game1.player.CursorSlotItem;

                    // Try to put back in original slot first
                    if (SourceSlotId >= 0 && SourceSlotId < Game1.player.Items.Count &&
                        Game1.player.Items[SourceSlotId] == null)
                    {
                        Game1.player.Items[SourceSlotId] = item;
                        Monitor?.Log($"InventoryManagement: Returned {item.Name} to source slot {SourceSlotId}", LogLevel.Debug);
                    }
                    // Try to add back to inventory
                    else if (!Game1.player.addItemToInventoryBool(item))
                    {
                        // Inventory full - drop item
                        Game1.createItemDebris(item, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                        Monitor?.Log($"InventoryManagement: Dropped {item.Name} (inventory full)", LogLevel.Warn);
                    }

                    Game1.player.CursorSlotItem = null;
                }

                IsHoldingItem = false;
                SourceSlotId = -1;
            }

            // Also reset Y button state
            IsYButtonHeld = false;
            YButtonHoldTicks = 0;
        }

        /// <summary>
        /// Check if we're currently holding an item.
        /// </summary>
        public static bool IsCurrentlyHolding()
        {
            return IsHoldingItem && Game1.player.CursorSlotItem != null;
        }

        /// <summary>
        /// Called when leaving the inventory menu - clean up state.
        /// </summary>
        public static void OnMenuClosed()
        {
            CancelHold();
        }

        /// <summary>
        /// Called by FishingRodPatches when it clears CursorSlotItem (e.g., attaching bait to rod).
        /// This keeps our IsHoldingItem state in sync.
        /// </summary>
        public static void OnCursorItemCleared()
        {
            if (IsHoldingItem)
            {
                Monitor?.Log("InventoryManagement: Cursor cleared by external code, syncing state", LogLevel.Debug);
                IsHoldingItem = false;
                SourceSlotId = -1;
            }
        }

        /// <summary>
        /// Called by FishingRodPatches when it puts an item on the cursor (e.g., detaching bait from rod).
        /// This keeps our IsHoldingItem state in sync.
        /// </summary>
        public static void SetHoldingItem(bool holding)
        {
            Monitor?.Log($"InventoryManagement: SetHoldingItem({holding}) called by external code", LogLevel.Debug);
            IsHoldingItem = holding;
            if (!holding)
            {
                SourceSlotId = -1;
            }
        }
    }
}
