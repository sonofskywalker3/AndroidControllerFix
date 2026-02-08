using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
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

        /// <summary>Whether the color picker is currently open and we're navigating swatches.</summary>
        private static bool _colorPickerOpen = false;

        /// <summary>Game tick when B closed the color picker. exitThisMenu blocks one call
        /// on this exact tick to prevent the game's separate B-close path from also closing
        /// the chest within the same frame.</summary>
        private static int _pickerClosedTick = -100;

        /// <summary>Saved color toggle neighbors to restore when picker closes.</summary>
        private static int _colorToggleSavedUp = -1;
        private static int _colorToggleSavedDown = -1;

        /// <summary>Original swatch bounds saved before relocating to visual grid positions.
        /// Key is myID, value is the original bounds rectangle.</summary>
        private static Dictionary<int, Microsoft.Xna.Framework.Rectangle> _savedSwatchBounds = new Dictionary<int, Microsoft.Xna.Framework.Rectangle>();

        /// <summary>Cached visual grid stride/origin from probing the picker's nearest-color hit-test.
        /// Computed once on first picker open, reused on subsequent opens.</summary>
        private static int _probedStrideX = -1;
        private static int _probedStrideY = -1;
        private static int _probedCenterX0 = -1;
        private static int _probedCenterY0 = -1;

        // Y button hold-to-repeat for single-item transfer
        private static bool _yTransferHeld;
        private static int _yTransferTicks;
        private static Buttons _yTransferRawButton;
        private static int _yTransferSlotIndex;
        private static bool _yTransferFromChest; // true = chest→player, false = player→chest
        private const int TransferHoldDelay = 20;   // ~333ms at 60fps before repeat starts
        private const int TransferRepeatRate = 3;    // ~50ms at 60fps between repeats

        // Cached reflection fields for InventoryMenu sidebar buttons
        private static FieldInfo _organizeButtonField;
        private static FieldInfo _trashCanField;

        // Unique IDs assigned to buttons with duplicate or sentinel myIDs
        private const int ID_SORT_CHEST = 54106;    // was 106
        private const int ID_SORT_INV = 54206;       // was 106
        private const int ID_TRASH_PLAYER = 54105;   // was 105
        private const int ID_CLOSE_X = 54500;        // was -500 (sentinel, not resolvable by getComponentWithID)

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Cache reflection lookups for sidebar buttons
            _organizeButtonField = AccessTools.Field(typeof(InventoryMenu), "organizeButton");
            _trashCanField = AccessTools.Field(typeof(InventoryMenu), "trashCan");

            try
            {
                // Patch receiveGamePadButton to intercept chest management buttons
                // Use PREFIX to block the original method (prevents Android X-delete bug)
                harmony.Patch(
                    original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveGamePadButton)),
                    prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ReceiveGamePadButton_Prefix))
                );

                // Patch exitThisMenu to block same-frame exit when B closes the color picker.
                // The game's update loop has a separate B-close path that reads already-cached
                // gamepad state, so GetState suppression alone can't prevent it.
                harmony.Patch(
                    original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.exitThisMenu)),
                    prefix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(ExitThisMenu_Prefix))
                );

                // Patch update for Y-button hold-to-repeat in chest transfer
                harmony.Patch(
                    original: AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.update)),
                    postfix: new HarmonyMethod(typeof(ItemGrabMenuPatches), nameof(Update_Postfix))
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
                _colorPickerOpen = false;
                bool verbose = ModEntry.Config.VerboseLogging;

                // =====================================================
                // Step 1: Get sidebar buttons by OBJECT REFERENCE
                // Do NOT scan allClickableComponents — the buttons we
                // need (organizeButton, trashCan) aren't in that list,
                // and IDs 105/106 are duplicated across chest/player.
                // =====================================================
                var sortChest = menu.ItemsToGrabMenu != null && _organizeButtonField != null
                    ? _organizeButtonField.GetValue(menu.ItemsToGrabMenu) as ClickableComponent
                    : null;                                              // ID 106, Y~86
                var fillStacks = menu.fillStacksButton;                  // ID 12952
                var colorToggle = menu.colorPickerToggleButton;          // ID 27346
                var sortInv = menu.inventory != null && _organizeButtonField != null
                    ? _organizeButtonField.GetValue(menu.inventory) as ClickableComponent
                    : null;                                              // ID 106, Y~471
                var trashPlayer = menu.inventory != null && _trashCanField != null
                    ? _trashCanField.GetValue(menu.inventory) as ClickableComponent
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
                    sortChest.upNeighborID = closeX?.myID ?? sortChest.upNeighborID;
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

        /// <summary>Find a color swatch component by ID in allClickableComponents.</summary>
        private static ClickableComponent FindSwatchById(ItemGrabMenu menu, int id)
        {
            if (menu.allClickableComponents == null) return null;
            foreach (var comp in menu.allClickableComponents)
            {
                if (comp.myID == id) return comp;
            }
            return null;
        }

        /// <summary>Wire or unwire color picker swatches for snap navigation.
        /// When opening: arrange swatches as 7x3 grid with proper neighbors,
        /// block navigation to inventory/sidebar, add swatches to _sideButtonObjects.
        /// When closing: remove swatches, restore color toggle wiring, snap back to toggle.</summary>
        private static void WireColorSwatches(ItemGrabMenu menu, bool opening)
        {
            bool verbose = ModEntry.Config.VerboseLogging;

            // Collect swatch components (IDs 4343-4363), sorted by X position
            var swatches = new List<ClickableComponent>();
            if (menu.allClickableComponents != null)
            {
                foreach (var comp in menu.allClickableComponents)
                {
                    if (comp.myID >= 4343 && comp.myID <= 4363)
                        swatches.Add(comp);
                }
            }
            swatches.Sort((a, b) => a.bounds.X.CompareTo(b.bounds.X));

            if (verbose)
                Monitor.Log($"[ChestNav] WireColorSwatches: {(opening ? "OPEN" : "CLOSE")}, {swatches.Count} swatches found", LogLevel.Debug);

            if (swatches.Count == 0)
                return;

            if (opening)
            {
                // Save color toggle's current neighbors before we modify them
                if (_colorToggleButton != null)
                {
                    _colorToggleSavedUp = _colorToggleButton.upNeighborID;
                    _colorToggleSavedDown = _colorToggleButton.downNeighborID;
                }

                // Arrange as 7 columns x 3 rows grid
                // Swatches are sorted by X — first 7 are row 0, next 7 row 1, last 7 row 2
                // All swatches share the same Y in their component bounds (flat row at Y:106),
                // even though the game RENDERS them as a 7x3 visual grid. We must relocate
                // each swatch's bounds to its visual grid position so snapCursorToCurrentSnappedComponent
                // positions the cursor correctly when the game's own navigation moves between swatches.
                int cols = 7;
                int rows = swatches.Count / cols; // should be 3
                if (rows < 1) rows = 1;

                // The DiscreteColorPicker renders a 7x3 grid, but the component bounds
                // are wrong (flat row at Y:106). We probe the picker's nearest-color
                // hit-test to find the exact visual stride and grid origin. The probe
                // runs once and caches the results for subsequent opens.
                var picker = menu.chestColorPicker;

                if (_probedStrideX < 0)
                {
                    try
                    {
                        var colorField = picker.GetType().GetField("colorSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (colorField != null)
                        {
                            int savedColor = (int)colorField.GetValue(picker);
                            int pX = picker.xPositionOnScreen;
                            int pY = picker.yPositionOnScreen;
                            int probeY = pY + 50; // safely in row 0

                            // Find col 0→1 and col 1→2 midpoints
                            int colMid01 = -1, colMid12 = -1;
                            for (int px = pX; px <= pX + 300; px++)
                            {
                                colorField.SetValue(picker, -1);
                                picker.receiveLeftClick(px, probeY, false);
                                int sel = (int)colorField.GetValue(picker);
                                if (sel == 1 && colMid01 < 0) colMid01 = px;
                                if (sel == 2 && colMid12 < 0) { colMid12 = px; break; }
                            }

                            // Find row 0→1 and row 1→2 midpoints
                            int rowMid01 = -1, rowMid12 = -1;
                            int probeX = pX + 50; // safely in col 0
                            for (int py = pY; py <= pY + 300; py++)
                            {
                                colorField.SetValue(picker, -1);
                                picker.receiveLeftClick(probeX, py, false);
                                int sel = (int)colorField.GetValue(picker);
                                if (sel == 7 && rowMid01 < 0) rowMid01 = py;
                                if (sel == 14 && rowMid12 < 0) { rowMid12 = py; break; }
                            }

                            if (colMid01 > 0 && colMid12 > 0 && rowMid01 > 0 && rowMid12 > 0)
                            {
                                _probedStrideX = colMid12 - colMid01;
                                _probedStrideY = rowMid12 - rowMid01;
                                _probedCenterX0 = colMid01 - _probedStrideX / 2;
                                _probedCenterY0 = rowMid01 - _probedStrideY / 2;
                                if (verbose)
                                    Monitor.Log($"[ChestNav] Probed swatch grid: stride=({_probedStrideX},{_probedStrideY}), firstCenter=({_probedCenterX0},{_probedCenterY0})", LogLevel.Debug);
                            }
                            else
                            {
                                Monitor.Log("[ChestNav] Swatch probe failed, using fallback stride", LogLevel.Warn);
                            }

                            // Restore the original color by clicking at its grid position.
                            // This restores both colorSelection AND the chest's visual color,
                            // regardless of how the picker stores the chest reference internally.
                            if (savedColor >= 0 && _probedStrideX > 0 && _probedStrideY > 0)
                            {
                                int restoreCol = savedColor % 7;
                                int restoreRow = savedColor / 7;
                                int restoreX = _probedCenterX0 + restoreCol * _probedStrideX;
                                int restoreY = _probedCenterY0 + restoreRow * _probedStrideY;
                                picker.receiveLeftClick(restoreX, restoreY, false);
                                if (verbose)
                                    Monitor.Log($"[ChestNav] Restored color {savedColor} via click at ({restoreX},{restoreY})", LogLevel.Debug);
                            }
                            else
                            {
                                // Fallback: at least restore colorSelection via reflection
                                colorField.SetValue(picker, savedColor);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"[ChestNav] Swatch probe error: {ex.Message}", LogLevel.Warn);
                    }
                }

                // Use probed values, or fall back to original swatch bounds stride
                int strideX = _probedStrideX > 0 ? _probedStrideX : swatches[0].bounds.Width;
                int strideY = _probedStrideY > 0 ? _probedStrideY : swatches[0].bounds.Height;
                int gridOriginX = _probedCenterX0 > 0 ? _probedCenterX0 - strideX / 2 : swatches[0].bounds.X;
                int gridOriginY = _probedCenterY0 > 0 ? _probedCenterY0 - strideY / 2 : swatches[0].bounds.Y;

                _savedSwatchBounds.Clear();

                for (int i = 0; i < swatches.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    var s = swatches[i];

                    // Save original bounds for restoration
                    _savedSwatchBounds[s.myID] = s.bounds;

                    // Relocate bounds to visual grid position
                    s.bounds = new Rectangle(
                        gridOriginX + col * strideX,
                        gridOriginY + row * strideY,
                        strideX,
                        strideY
                    );

                    // Left/right within row — edges do NOT wrap or escape
                    s.leftNeighborID = (col > 0) ? swatches[i - 1].myID : -1;
                    s.rightNeighborID = (col < cols - 1 && i + 1 < swatches.Count) ? swatches[i + 1].myID : -1;

                    // Up/down between rows — edges do NOT wrap or escape
                    s.upNeighborID = (row > 0) ? swatches[i - cols].myID : -1;
                    s.downNeighborID = (row < rows - 1 && i + cols < swatches.Count) ? swatches[i + cols].myID : -1;

                    _sideButtonObjects.Add(s);
                }

                // Make color toggle NOT navigable while picker is open
                if (_colorToggleButton != null)
                {
                    _colorToggleButton.upNeighborID = -1;
                    _colorToggleButton.downNeighborID = -1;
                }

                _colorPickerOpen = true;

                if (verbose)
                {
                    Monitor.Log($"[ChestNav] Wired {swatches.Count} swatches as {cols}x{rows} grid, bounds relocated (stride {strideX}x{strideY} from {gridOriginX},{gridOriginY})", LogLevel.Debug);
                    for (int i = 0; i < swatches.Count; i++)
                    {
                        var s = swatches[i];
                        Monitor.Log($"[ChestNav]   swatch[{i}] ID={s.myID} bounds=({s.bounds.X},{s.bounds.Y},{s.bounds.Width},{s.bounds.Height}) L={s.leftNeighborID} R={s.rightNeighborID} U={s.upNeighborID} D={s.downNeighborID}", LogLevel.Debug);
                    }
                }
            }
            else
            {
                // Remove swatches from side button objects and restore original bounds
                foreach (var s in swatches)
                {
                    _sideButtonObjects.Remove(s);
                    if (_savedSwatchBounds.TryGetValue(s.myID, out var originalBounds))
                        s.bounds = originalBounds;
                }
                _savedSwatchBounds.Clear();

                // Restore color toggle's saved neighbors
                if (_colorToggleButton != null)
                {
                    _colorToggleButton.upNeighborID = _colorToggleSavedUp;
                    _colorToggleButton.downNeighborID = _colorToggleSavedDown;
                }

                _colorPickerOpen = false;

                if (verbose)
                    Monitor.Log("[ChestNav] Unwired color swatches, restored toggle neighbors and bounds", LogLevel.Debug);
            }
        }

        /// <summary>Prefix for exitThisMenu to block same-frame exit when B closes the color picker.
        /// The game's update loop processes B through a separate code path that reads gamepad state
        /// cached at the start of the frame (before our receiveGamePadButton prefix could suppress it).
        /// This blocks one exitThisMenu call on the exact tick the picker was closed.</summary>
        private static bool ExitThisMenu_Prefix(IClickableMenu __instance)
        {
            if (__instance is ItemGrabMenu && Game1.ticks == _pickerClosedTick)
            {
                _pickerClosedTick = -100; // one-shot: only block one call
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("[ChestNav] Blocked exitThisMenu — B was closing picker, not chest", LogLevel.Debug);
                return false;
            }
            return true;
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
                    Monitor.Log($"ItemGrabMenu button: {b} (remapped={remapped}), pickerOpen={_colorPickerOpen}", LogLevel.Debug);

                // =====================================================
                // Color picker open: intercept ALL navigation + B
                // =====================================================
                if (_colorPickerOpen && ModEntry.Config.EnableConsoleChests)
                {
                    // B closes the picker (intercept BEFORE original method which would close the chest)
                    if (remapped == Buttons.B)
                    {
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("[ChestNav] B while picker open — closing picker", LogLevel.Debug);

                        __instance.chestColorPicker.visible = false;
                        Game1.playSound("drumkit6");
                        WireColorSwatches(__instance, false);

                        // Block exitThisMenu for this tick — the game's update loop has
                        // a separate B-close path that uses already-cached gamepad state
                        _pickerClosedTick = Game1.ticks;

                        // Snap cursor back to color toggle button
                        if (_colorToggleButton != null)
                        {
                            __instance.currentlySnappedComponent = _colorToggleButton;
                            __instance.snapCursorToCurrentSnappedComponent();
                        }
                        return false;
                    }

                    // A on a swatch selects that color.
                    // Click the DiscreteColorPicker directly using its own coordinate
                    // system. The picker's receiveLeftClick does hit-testing based on its
                    // position and a 7-column grid layout. We compute the click point from
                    // the picker's screen position and the swatch's grid row/col.
                    if (remapped == Buttons.A)
                    {
                        var snapped = __instance.currentlySnappedComponent;
                        if (snapped != null && snapped.myID >= 4343 && snapped.myID <= 4363)
                        {
                            // Click at the visual center of the swatch (bounds.Center).
                            // The bounds were relocated to match the visual grid, and the
                            // picker uses nearest-color hit-testing, so the visual center
                            // will always select the correct color.
                            int cx = snapped.bounds.Center.X;
                            int cy = snapped.bounds.Center.Y;
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"[ChestNav] A on color swatch {snapped.myID} — click at ({cx},{cy})", LogLevel.Debug);
                            __instance.receiveLeftClick(cx, cy);
                        }
                        return false;
                    }

                    // D-pad/thumbstick navigation: let the game's own nav handle movement.
                    // Swatch bounds have been relocated to visual 7x3 grid positions, so
                    // snapCursorToCurrentSnappedComponent() will position the cursor correctly.
                    // The game's separate nav code (outside receiveGamePadButton) moves the
                    // cursor using our wired neighbor IDs. We just block the original
                    // ItemGrabMenu.receiveGamePadButton to prevent any undesired side effects.
                    // Edge neighbors are -1 so the game's nav stays within the grid.
                    if (remapped == Buttons.DPadUp || remapped == Buttons.DPadDown ||
                        remapped == Buttons.DPadLeft || remapped == Buttons.DPadRight ||
                        remapped == Buttons.LeftThumbstickUp || remapped == Buttons.LeftThumbstickDown ||
                        remapped == Buttons.LeftThumbstickLeft || remapped == Buttons.LeftThumbstickRight)
                    {
                        if (ModEntry.Config.VerboseLogging)
                        {
                            var snapped = __instance.currentlySnappedComponent;
                            Monitor.Log($"[ChestNav] Picker nav: snapped={snapped?.myID ?? -1}, bounds=({snapped?.bounds.X},{snapped?.bounds.Y})", LogLevel.Debug);
                        }
                        return false;
                    }

                    // Block all other buttons while picker is open (X, Y, etc.)
                    // Only let Start and shoulder buttons through for menu-level actions
                    if (remapped != Buttons.Start && remapped != Buttons.LeftShoulder &&
                        remapped != Buttons.RightShoulder && remapped != Buttons.LeftTrigger &&
                        remapped != Buttons.RightTrigger)
                    {
                        return false;
                    }
                }

                // =====================================================
                // Normal chest navigation (picker closed)
                // =====================================================

                // A button on side buttons — simulate click at button position.
                // On Android, A fires receiveLeftClick at the mouse position which doesn't
                // track snap navigation. We intercept and click at the correct coordinates.
                if (remapped == Buttons.A && ModEntry.Config.EnableConsoleChests)
                {
                    var snapped = __instance.currentlySnappedComponent;
                    if (snapped != null && _sideButtonObjects.Contains(snapped))
                    {
                        // --- Close X: simulate B press to close the chest ---
                        // Suppress A at GetState level until released so the overworld
                        // never sees A and can't reopen the chest via "interact."
                        if (snapped == _closeXButton)
                        {
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log("[ChestNav] A on Close X — closing via B + A suppress-until-release", LogLevel.Debug);
                            GameplayButtonPatches.SuppressAUntilRelease = true;
                            GameplayButtonPatches.InvalidateCache();
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
                            bool opening = !__instance.chestColorPicker.visible;
                            if (ModEntry.Config.VerboseLogging)
                                Monitor.Log($"[ChestNav] A on Color Toggle — {(opening ? "opening" : "closing")} picker", LogLevel.Debug);

                            // Toggle the picker visibility
                            __instance.chestColorPicker.visible = opening;
                            Game1.playSound("drumkit6");

                            // Wire or unwire swatch navigation
                            WireColorSwatches(__instance, opening);

                            // Snap to first swatch when opening
                            if (opening)
                            {
                                var firstSwatch = FindSwatchById(__instance, 4343);
                                if (firstSwatch != null)
                                {
                                    __instance.currentlySnappedComponent = firstSwatch;
                                    __instance.snapCursorToCurrentSnappedComponent();
                                    if (ModEntry.Config.VerboseLogging)
                                        Monitor.Log($"[ChestNav] Snapped to first swatch {firstSwatch.myID} at ({firstSwatch.bounds.Center.X},{firstSwatch.bounds.Center.Y})", LogLevel.Debug);
                                }
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

                // A/Y on grid slots — console-style chest item transfer
                // A = transfer full stack, Y = transfer one item
                if ((remapped == Buttons.A || remapped == Buttons.Y) && ModEntry.Config.EnableConsoleChests)
                {
                    // Don't interfere if player has item on cursor
                    if (Game1.player.CursorSlotItem == null)
                    {
                        var snapped = __instance.currentlySnappedComponent;
                        if (snapped != null)
                        {
                            int chestSlotIndex = __instance.ItemsToGrabMenu?.inventory?.IndexOf(snapped) ?? -1;
                            int playerSlotIndex = __instance.inventory?.inventory?.IndexOf(snapped) ?? -1;

                            if (chestSlotIndex >= 0)
                            {
                                if (remapped == Buttons.A)
                                    TransferFromChest(__instance, chestSlotIndex);
                                else
                                {
                                    TransferOneFromChest(__instance, chestSlotIndex);
                                    _yTransferHeld = true;
                                    _yTransferTicks = 0;
                                    _yTransferRawButton = b;
                                    _yTransferSlotIndex = chestSlotIndex;
                                    _yTransferFromChest = true;
                                }
                                return false;
                            }
                            else if (playerSlotIndex >= 0)
                            {
                                if (remapped == Buttons.A)
                                    TransferToChest(__instance, playerSlotIndex);
                                else
                                {
                                    TransferOneToChest(__instance, playerSlotIndex);
                                    _yTransferHeld = true;
                                    _yTransferTicks = 0;
                                    _yTransferRawButton = b;
                                    _yTransferSlotIndex = playerSlotIndex;
                                    _yTransferFromChest = false;
                                }
                                return false;
                            }
                        }
                    }
                }

                // RB snaps cursor to Fill Stacks button (console-style shortcut)
                if (remapped == Buttons.RightShoulder && ModEntry.Config.EnableConsoleChests && !_colorPickerOpen)
                {
                    if (__instance.fillStacksButton != null)
                    {
                        __instance.currentlySnappedComponent = __instance.fillStacksButton;
                        __instance.snapCursorToCurrentSnappedComponent();
                        if (ModEntry.Config.VerboseLogging)
                            Monitor.Log("[ChestTransfer] RB — snapped to Fill Stacks button", LogLevel.Debug);
                        return false;
                    }
                }

                // X button (after remapping) = Sort chest (and block the original to prevent deletion)
                if (remapped == Buttons.X && ModEntry.Config.EnableConsoleChests)
                {
                    if (ModEntry.Config.VerboseLogging)
                        Monitor.Log($"{b} remapped to X - sorting chest (blocking original)", LogLevel.Debug);
                    OrganizeChest(__instance);
                    return false; // Block original method to prevent item deletion
                }

                // Y button (after remapping) = Add to existing stacks
                if (remapped == Buttons.Y && ModEntry.Config.EnableConsoleChests)
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

        /// <summary>Postfix for update — Y-button hold-to-repeat for single-item transfer.</summary>
        private static void Update_Postfix(ItemGrabMenu __instance, GameTime time)
        {
            if (!_yTransferHeld || !ModEntry.Config.EnableConsoleChests)
                return;

            if (__instance.shippingBin)
            {
                _yTransferHeld = false;
                return;
            }

            var gpState = GamePad.GetState(PlayerIndex.One);
            bool yStillHeld = gpState.IsButtonDown(_yTransferRawButton);

            if (yStillHeld)
            {
                _yTransferTicks++;

                if (_yTransferTicks > TransferHoldDelay &&
                    (_yTransferTicks - TransferHoldDelay) % TransferRepeatRate == 0)
                {
                    if (_yTransferFromChest)
                    {
                        var chestInv = __instance.ItemsToGrabMenu?.actualInventory;
                        if (chestInv != null && _yTransferSlotIndex < chestInv.Count && chestInv[_yTransferSlotIndex] != null)
                            TransferOneFromChest(__instance, _yTransferSlotIndex);
                        else
                            _yTransferHeld = false;
                    }
                    else
                    {
                        if (_yTransferSlotIndex < Game1.player.Items.Count && Game1.player.Items[_yTransferSlotIndex] != null)
                            TransferOneToChest(__instance, _yTransferSlotIndex);
                        else
                            _yTransferHeld = false;
                    }
                }
            }
            else
            {
                _yTransferHeld = false;
                _yTransferTicks = 0;
            }
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

        /// <summary>Transfer full stack from chest to player inventory.</summary>
        private static void TransferFromChest(ItemGrabMenu menu, int slotIndex)
        {
            var chestInv = menu.ItemsToGrabMenu?.actualInventory;
            if (chestInv == null || slotIndex >= chestInv.Count) return;

            Item item = chestInv[slotIndex];
            if (item == null)
            {
                Game1.playSound("cancel");
                return;
            }

            int stackBefore = item.Stack;
            Item leftover = Game1.player.addItemToInventory(item);

            if (leftover == null || leftover.Stack <= 0)
            {
                chestInv[slotIndex] = null;
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Took {stackBefore}x {item.DisplayName} from chest", LogLevel.Debug);
            }
            else if (leftover.Stack < stackBefore)
            {
                // Partial — leftover stays in chest (same reference, already updated)
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Partial: took {stackBefore - leftover.Stack}x {item.DisplayName}, {leftover.Stack} remain in chest", LogLevel.Debug);
            }
            else
            {
                // Nothing transferred — player inventory full
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Inventory full, cannot take {item.DisplayName}", LogLevel.Debug);
            }
        }

        /// <summary>Transfer one item from chest to player inventory.</summary>
        private static void TransferOneFromChest(ItemGrabMenu menu, int slotIndex)
        {
            var chestInv = menu.ItemsToGrabMenu?.actualInventory;
            if (chestInv == null || slotIndex >= chestInv.Count) return;

            Item item = chestInv[slotIndex];
            if (item == null)
            {
                Game1.playSound("cancel");
                return;
            }

            Item one = item.getOne();
            Item leftover = Game1.player.addItemToInventory(one);

            if (leftover == null || leftover.Stack <= 0)
            {
                item.Stack--;
                if (item.Stack <= 0)
                    chestInv[slotIndex] = null;
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Took 1x {one.DisplayName} from chest ({item?.Stack ?? 0} remain)", LogLevel.Debug);
            }
            else
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Inventory full, cannot take 1x {one.DisplayName}", LogLevel.Debug);
            }
        }

        /// <summary>Transfer full stack from player inventory to chest.</summary>
        private static void TransferToChest(ItemGrabMenu menu, int slotIndex)
        {
            var chestInv = menu.ItemsToGrabMenu?.actualInventory;
            if (chestInv == null) return;

            if (slotIndex >= Game1.player.Items.Count) return;
            Item item = Game1.player.Items[slotIndex];
            if (item == null)
            {
                Game1.playSound("cancel");
                return;
            }

            int stackBefore = item.Stack;

            // Try stacking onto existing chest items first
            for (int i = 0; i < chestInv.Count && item.Stack > 0; i++)
            {
                if (chestInv[i] != null && chestInv[i].canStackWith(item))
                {
                    int space = chestInv[i].maximumStackSize() - chestInv[i].Stack;
                    if (space > 0)
                    {
                        int toMove = Math.Min(space, item.Stack);
                        chestInv[i].Stack += toMove;
                        item.Stack -= toMove;
                    }
                }
            }

            // Then try an empty slot for the remainder
            if (item.Stack > 0)
            {
                for (int i = 0; i < chestInv.Count; i++)
                {
                    if (chestInv[i] == null)
                    {
                        chestInv[i] = item;
                        item = null;
                        break;
                    }
                }
            }

            if (item == null || item.Stack <= 0)
            {
                Game1.player.Items[slotIndex] = null;
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Put {stackBefore}x into chest from player", LogLevel.Debug);
            }
            else if (item.Stack < stackBefore)
            {
                // Partial — some stacked but chest has no empty slots for remainder
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Partial: put {stackBefore - item.Stack}x into chest, {item.Stack} remain in player", LogLevel.Debug);
            }
            else
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("[ChestTransfer] Chest full, cannot place item", LogLevel.Debug);
            }
        }

        /// <summary>Transfer one item from player inventory to chest.</summary>
        private static void TransferOneToChest(ItemGrabMenu menu, int slotIndex)
        {
            var chestInv = menu.ItemsToGrabMenu?.actualInventory;
            if (chestInv == null) return;

            if (slotIndex >= Game1.player.Items.Count) return;
            Item item = Game1.player.Items[slotIndex];
            if (item == null)
            {
                Game1.playSound("cancel");
                return;
            }

            Item one = item.getOne();
            bool placed = false;

            // Try stacking first
            for (int i = 0; i < chestInv.Count; i++)
            {
                if (chestInv[i] != null && chestInv[i].canStackWith(one) &&
                    chestInv[i].Stack < chestInv[i].maximumStackSize())
                {
                    chestInv[i].Stack++;
                    placed = true;
                    break;
                }
            }

            // Then try empty slot
            if (!placed)
            {
                for (int i = 0; i < chestInv.Count; i++)
                {
                    if (chestInv[i] == null)
                    {
                        chestInv[i] = one;
                        placed = true;
                        break;
                    }
                }
            }

            if (placed)
            {
                item.Stack--;
                if (item.Stack <= 0)
                    Game1.player.Items[slotIndex] = null;
                Game1.playSound("stoneStep");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log($"[ChestTransfer] Put 1x {one.DisplayName} into chest ({item?.Stack ?? 0} remain in player)", LogLevel.Debug);
            }
            else
            {
                Game1.playSound("cancel");
                if (ModEntry.Config.VerboseLogging)
                    Monitor.Log("[ChestTransfer] Chest full, cannot place 1 item", LogLevel.Debug);
            }
        }
    }
}
