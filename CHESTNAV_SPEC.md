# ChestNav Fix Specification — v2.9.7

## Overview

Fix controller snap navigation in the ItemGrabMenu (chest menu) on Android. The current code (v2.9.5) is completely broken — it includes invisible components in the sidebar chain, misses both sort buttons entirely, and can't handle duplicate component IDs.

This spec was validated via an interactive HTML simulator (`chest_layout_debug.html`) and confirmed correct by the user.

## The Problem

Android's ItemGrabMenu has these issues with snap navigation:

1. **Duplicate myID=106**: Both `ItemsToGrabMenu.organizeButton` (sort chest) and `inventory.organizeButton` (sort inventory) share ID 106. Neither appears in `allClickableComponents`.
2. **Duplicate myID=105**: Both `ItemsToGrabMenu.trashCan` (chest trash) and `inventory.trashCan` (player trash) share ID 105. Neither appears in `allClickableComponents`.
3. **Ghost IDs**: Chest rightmost grid slots point right to IDs 54015/54016 which don't exist.
4. **Color picker swatches**: 21 DiscreteColorPicker components (IDs 4343-4363) exist in `allClickableComponents` even when invisible. These must be completely ignored.
5. **Invisible buttons**: `okButton` (4857), `ItemsToGrabMenu.trashCan` (105), and `trashCan` field (5948) are invisible and should never be snap targets.

## Complete Component Map (from v2.9.6 diagnostic dump)

### Visible Sidebar Buttons (6 total)

| Button | myID | Source Field | Bounds | Visible |
|--------|------|-------------|--------|---------|
| Sort Chest | 106 | `ItemsToGrabMenu.organizeButton` | X:1342 Y:86 64x64 | YES |
| Fill Stacks | 12952 | `menu.fillStacksButton` | X:1342 Y:195 64x64 | YES |
| Color Toggle | 27346 | `menu.colorPickerToggleButton` | X:1342 Y:305 64x64 | YES |
| Sort Inventory | 106 | `menu.inventory.organizeButton` | X:1351 Y:471 64x64 | YES |
| Trash (player) | 105 | `menu.inventory.trashCan` | X:1351 Y:673 64x150 | YES |
| Close X | -500 | `menu.upperRightCloseButton` | X:1401 Y:0 80x80 | YES |

### Invisible Buttons (NEVER snap to these)

| Button | myID | Source Field | Why Invisible |
|--------|------|-------------|---------------|
| OK Button | 4857 | `menu.okButton` | Hidden behind other UI |
| Trash (chest inv) | 105 | `ItemsToGrabMenu.trashCan` | Overlaps color toggle area, not rendered |
| Trash (menu) | 5948 | `menu.trashCan` | Superseded by inventory trash |

### Grid Layout

- **Chest grid**: 36 slots, 12 cols x 3 rows, IDs 53910-53945
  - Slot positions: X = 44 + col*103, Y = 66 + row*110, size 103x110
  - Rightmost column: 53921 (row 0), 53933 (row 1), 53945 (row 2)
  - Default right neighbor on rightmost: 54016/54015 (GHOST — doesn't exist)

- **Player grid**: 36 slots, 12 cols x 3 rows, IDs 0-35
  - Slot positions: X = 63 + col*103, Y = 471 + row*110, size 103x110
  - Rightmost column: 11 (row 0), 23 (row 1), 35 (row 2)
  - Default right neighbor: 106 (row 0), 105 (row 1, 2) — correct IDs but ambiguous due to duplicates

### Color Picker Swatches (IGNORE)

- 21 components, IDs 4343-4363, all in `allClickableComponents`
- All at Y:106, 80x72 each, spanning X:48 to X:1728
- `DiscreteColorPicker` bounds: (28, 86, 1214, 282), visible=False by default
- These must be **completely excluded** from sidebar navigation

### Special Sentinel Values

- `-7777`: Used as up/down neighbor on grid slots to mean "navigate to the other grid section" (chest↔player). The game handles this internally. Do NOT overwrite these.
- `-500`: Used for offscreen/invisible placeholders.
- `9000`: Used as leftNeighborID on organize buttons — probably an internal sentinel.

## Required Navigation Wiring

### Grid Rightmost → Sidebar

| Grid Slot | RIGHT → | Button |
|-----------|---------|--------|
| 53921 (chest row 0) | → | Sort Chest |
| 53933 (chest row 1) | → | Fill Stacks |
| 53945 (chest row 2) | → | Color Toggle |
| 11 (player row 0) | → | Sort Inventory |
| 23 (player row 1) | → | Trash (player) |
| 35 (player row 2) | → | Trash (player) |

### Sidebar → Grid (LEFT)

| Button | LEFT → | Grid Slot |
|--------|--------|-----------|
| Sort Chest | → | 53921 (chest row 0) |
| Fill Stacks | → | 53933 (chest row 1) |
| Color Toggle | → | 53945 (chest row 2) |
| Sort Inventory | → | 11 (player row 0) |
| Trash (player) | → | 23 (player row 1) |
| Close X | → | Sort Chest |

### Sidebar Vertical Chain (UP/DOWN)

```
Sort Chest ←→ Fill Stacks ←→ Color Toggle ←→ Sort Inventory ←→ Trash (player)
```

| Button | UP → | DOWN → |
|--------|------|--------|
| Sort Chest | 53921 (chest row 0 rightmost) | Fill Stacks |
| Fill Stacks | Sort Chest | Color Toggle |
| Color Toggle | Fill Stacks | Sort Inventory |
| Sort Inventory | Color Toggle | Trash (player) |
| Trash (player) | Sort Inventory | 35 (player row 2 rightmost) |
| Close X | (nothing) | Sort Chest |

### Sort Chest Special

| Direction | Target |
|-----------|--------|
| RIGHT | Close X |

## Implementation Approach

### 1. Get buttons by OBJECT REFERENCE, not ID scan

Since IDs 105 and 106 are duplicated, do NOT scan `allClickableComponents`. Instead, grab the actual objects from field references:

```csharp
var sortChest = menu.ItemsToGrabMenu?.organizeButton;      // ID 106, Y:86
var fillStacks = menu.fillStacksButton;                      // ID 12952
var colorToggle = menu.colorPickerToggleButton;              // ID 27346
var sortInv = menu.inventory?.organizeButton;                // ID 106, Y:471
var trashPlayer = menu.inventory?.trashCan;                  // ID 105, Y:673
var closeX = menu.upperRightCloseButton;                     // ID -500
```

Note: `menu.inventory` is the PLAYER InventoryMenu. `menu.ItemsToGrabMenu` is the CHEST InventoryMenu. Both `InventoryMenu` types have `organizeButton` and `trashCan` fields accessible via reflection or direct cast since they inherit from common base types.

### 2. Assign unique IDs to resolve collisions

Before wiring neighbors, reassign unique IDs so the game's ID-based neighbor resolution works:

```csharp
// Pick IDs that don't collide with anything (53xxx, 4xxx, 0-35, 105, 106, 12952, 27346, 5948, 4857)
if (sortChest != null) sortChest.myID = 54106;   // was 106
if (sortInv != null) sortInv.myID = 54206;        // was 106
if (trashPlayer != null) trashPlayer.myID = 54105; // was 105
```

Update all neighbor references to use the new IDs.

### 3. Wire all neighbors per the spec tables above

Set `rightNeighborID`, `leftNeighborID`, `upNeighborID`, `downNeighborID` on every button and every rightmost grid slot per the tables in this spec.

### 4. A-button handler

The `ReceiveGamePadButton_Prefix` must intercept A on sidebar buttons and call `receiveLeftClick` at the button center. On Android, A fires `receiveLeftClick` at the mouse cursor position (touch position), not the snapped component position.

Store the sidebar button **objects** (not IDs in a HashSet) so we can compare `currentlySnappedComponent` by reference:

```csharp
private static HashSet<ClickableComponent> _sideButtonObjects = new();

// In FixSnapNavigation:
_sideButtonObjects.Clear();
if (sortChest != null) _sideButtonObjects.Add(sortChest);
if (fillStacks != null) _sideButtonObjects.Add(fillStacks);
// ... etc for all 6 visible buttons

// In ReceiveGamePadButton_Prefix:
if (remapped == Buttons.A && snapped != null && _sideButtonObjects.Contains(snapped))
{
    __instance.receiveLeftClick(snapped.bounds.Center.X, snapped.bounds.Center.Y);
    return false;
}
```

### 5. Color Toggle special behavior

When A is pressed on the color toggle button, the game opens/closes the `DiscreteColorPicker`. After the click fires, the snap cursor should move to the first color swatch (ID 4343) if the picker opened. The wiring code should run on each menu update (or use a postfix on the toggle) to handle swatch navigation when the picker is visible.

**For v2.9.7, color picker swatch navigation is OUT OF SCOPE.** Just make the 6 sidebar buttons + grid navigation work correctly. Color swatch navigation can be a follow-up patch.

### 6. Things to NOT do

- Do NOT scan `allClickableComponents` for sidebar buttons — the buttons we need aren't there
- Do NOT include `okButton` (4857), `menu.trashCan` (5948), or `ItemsToGrabMenu.trashCan` (105 at Y:268) in the sidebar chain — these are invisible
- Do NOT include any component from the DiscreteColorPicker (IDs 4343-4363)
- Do NOT overwrite `-7777` sentinel values on grid slots — the game uses these for cross-section navigation
- Do NOT wire buttons by ID matching against `allClickableComponents` — use object references

## Files to Modify

- `Patches/ItemGrabMenuPatches.cs` — the `FixSnapNavigation` method (replace the diagnostic dump with the real fix), the `_sideButtonIds` HashSet (change to `_sideButtonObjects` HashSet<ClickableComponent>), and the A-button check in `ReceiveGamePadButton_Prefix`
- `manifest.json` — bump version to 2.9.7

## Verification

After building, test with VerboseLogging ON. The log should show:
1. Each button found with its new unique ID
2. Each rightmost grid slot's right neighbor updated
3. Each sidebar button's full neighbor wiring
4. A-button clicks landing on the correct button coordinates

Interactive test:
1. Open chest → cursor should start on a chest slot
2. RIGHT to rightmost → RIGHT again → lands on Sort Chest
3. DOWN → Fill Stacks → DOWN → Color Toggle → DOWN → Sort Inventory → DOWN → Trash
4. LEFT from any sidebar button → back to grid rightmost slot
5. A on Sort Chest → chest sorts
6. A on Fill Stacks → items transfer
7. A on Trash → item deleted (if holding one)
