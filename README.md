# Android Consolizer

A SMAPI mod that brings console-style controller support to Android Stardew Valley. Play with a controller like you would on Nintendo Switch - 12-slot toolbar rows, proper shop purchasing, chest management, and more.

## Current Version: 3.2.0 — The Robin Release

## Features

### Controller Layout Support
- **Switch/Odin**: A=right, B=bottom, X=top, Y=left
- **Xbox**: A=bottom, B=right, X=left, Y=top
- **PlayStation**: Cross=A, Circle=B, Square=X, Triangle=Y

### Control Style Options
- **Switch style**: Right button confirms, bottom button cancels
- **Xbox/PS style**: Bottom button confirms, right button cancels

### Toolbar Navigation (Console-Style)
- **12-slot toolbar rows** instead of Android's chaotic scrolling toolbar
- **LB/RB**: Switch between toolbar rows (up to 3 rows with full backpack)
- **LT/RT**: Move left/right within the current row
- Visual toolbar matches console layout

### Bumper Mode (For Controllers with Trigger Issues)
When "Use Bumpers Instead of Triggers" is enabled:
- **Toolbar**: D-Pad Up/Down switches rows, LB/RB moves within row
- **Shops**: LB/RB adjusts purchase quantity
- Enable this if your controller's triggers aren't detected (e.g., Xbox via Bluetooth)

### Shop Fixes
- **A button (buy tab)**: Purchase items from any shop
- **A button (sell tab)**: Sell entire stack
- **Y button (sell tab)**: Sell one item (hold Y for rapid sell)
- **LB/RB**: Adjust purchase quantity (+/-1 in bumper mode, +/-10 otherwise), hold to repeat
- **Right stick**: Jump 5 items at a time on buy tab (hold to repeat)
- **Y button icon**: Visual tab-switch hint on the inventory button (adapts to controller layout)
- Touch tab-switch button blocked when controller is connected (prevents accidental taps)
- Sell price tooltip with gold coin icon appears next to selected item on sell tab
- Supports trade-item shops (Desert Trader), tool upgrades, recipes, and all special purchases
- Respects available stock, player money, and trade item requirements

### Chest Controls (Console-Style)
- **A button**: Instantly transfer full stack between chest and inventory (no selection step)
- **Y button**: Transfer one item (hold Y for rapid single-item transfer)
- **X button**: Sort chest contents
- **RB**: Snap cursor to Fill Stacks button
- **Sidebar buttons**: Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X — all reachable via controller navigation
- **Color picker**: Full 7x3 swatch grid navigation when picker is open. B closes picker only (not the chest)
- **X button deletion bug fixed**: The vanilla Android bug where X deletes items is completely blocked

### Inventory Fixes
- **X button**: Sort your inventory

### Shipping Bin Fix (Console-Style)
- **A button**: Ship entire stack from selected inventory slot
- **Y button**: Ship one item from selected inventory slot
- "Last shipped" display updates properly
- No more drag-and-drop - just select and ship

### Console-Style Inventory Management
- **A button on item**: Pick up entire stack to cursor (like Nintendo Switch)
- **A button on slot**: Place or swap item (empty slot places, occupied slot swaps)
- **Y button on stack**: Pick up a single item from a stack
- **Hold Y**: Continuously pick up single items from the stack
- Held items render visually at the cursor slot
- Tooltips appear on hover when navigating with controller
- Fishing rod tooltips shown when holding bait or tackle

### Carpenter Menu (Robin's Build Menu)
- Prevents Robin's building menu from instantly closing when opened with controller
- **Full joystick control in farm view** — Build, Move, and Demolish all work with the controller
  - Left stick moves a visible cursor across the farm, panning the viewport at screen edges
  - **Build mode**: Building ghost follows your cursor in real time. Press A to confirm placement.
  - **Move mode**: Press A to select a building, move cursor to new location, press A to confirm.
  - **Demolish mode**: Press A on a building to highlight it (green), press A again to confirm demolition. Move cursor off the building to deselect without demolishing.
  - Touch still works normally alongside the joystick cursor

### Furniture Placement Fix
- Y button no longer rapid-toggles furniture between picked up and placed
- One press = one interaction (pickup OR placement, never both)
- Works for all furniture types including beds

### Fishing Rod Bait/Tackle Fix
- **A button on bait/tackle**: Pick up to cursor for attachment
- **Y button on fishing rod**: Attach held bait/tackle, or detach to cursor if nothing held
- Detached bait/tackle goes to cursor (console-style) instead of first empty slot
- Works with Fiberglass Rod (bait only) and Iridium Rod (bait + tackle)
- Swapping supported: attaching different bait/tackle swaps with existing

## Button Mappings

| Context | Button | Action |
|---------|--------|--------|
| **Gameplay** | LB | Switch to previous toolbar row |
| **Gameplay** | RB | Switch to next toolbar row |
| **Gameplay** | LT | Move left in toolbar row |
| **Gameplay** | RT | Move right in toolbar row |
| **Shop (buy)** | A | Purchase selected quantity |
| **Shop (buy)** | LB/RB | Adjust purchase quantity (hold to repeat) |
| **Shop (buy)** | Right stick | Jump 5 items up/down (hold to repeat) |
| **Shop** | Y | Switch between buy/sell tabs (icon shown on button) |
| **Shop (sell)** | A | Sell entire stack |
| **Shop (sell)** | Y | Sell one item (hold for rapid sell) |
| **Inventory** | X | Sort inventory |
| **Chest** | A | Transfer full stack (chest↔inventory) |
| **Chest** | Y | Transfer one item (hold for rapid transfer) |
| **Chest** | X | Sort chest contents |
| **Chest** | RB | Snap to Fill Stacks button |
| **Inventory** | A | Pick up / place / swap item |
| **Inventory** | Y | Pick up single from stack (hold for continuous) |
| **Shipping Bin** | A | Ship entire stack |
| **Shipping Bin** | Y | Ship one item |
| **Inventory** | A (on bait/tackle) | Pick up bait/tackle to cursor |
| **Inventory** | Y (on fishing rod) | Attach held bait/tackle or detach to cursor |
| **Building (farm view)** | Left stick | Move cursor / pan viewport at edges |
| **Building (build)** | A | Confirm building placement at cursor |
| **Building (move)** | A | Select building / confirm new placement |
| **Building (demolish)** | A | Select building (highlights green) / confirm demolition |

## Dependencies

**Required:**
- Stardew Valley 1.6.15+ (Android)
- [SMAPI 4.0.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6) by NRTnarathip

**Optional:**
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) - for in-game settings

## Tested Controllers

| Controller | Device | Status | Notes |
|------------|--------|--------|-------|
| **Built-in (Odin)** | AYN Odin Pro | ✅ Fully Working | All buttons and triggers work |
| **Xbox One Wireless (Bluetooth)** | AYN Odin Pro | ⚠️ Partial | Triggers (LT/RT) not detected — enable "Use Bumpers Instead of Triggers". All other buttons work. |
| **Xbox Series X\|S Wireless (Bluetooth)** | AYN Odin Pro | ⚠️ Partial | Triggers (LT/RT) not detected — enable "Use Bumpers Instead of Triggers". All other buttons work. |

### Known Issues: Xbox Controller on Android

Xbox Wireless Controllers (both Xbox One and Xbox Series X|S) connected via Bluetooth have a known issue on Android where the analog triggers (LT/RT) are not detected by Stardew Valley. Both controller models behave identically — this is due to Xbox controllers reporting triggers on different axes (`AXIS_GAS`/`AXIS_BRAKE`) than what the game's framework expects (`AXIS_LTRIGGER`/`AXIS_RTRIGGER`).

**Workaround:** Enable "Use Bumpers Instead of Triggers" in the mod settings. This remaps:
- Toolbar: D-Pad Up/Down for rows, LB/RB for moving within row
- Shops: LB/RB for adjusting purchase quantity

All other Xbox controller buttons (A/B/X/Y, bumpers, thumbsticks, D-Pad) work correctly.

If you test with other controllers, please report your results!

## Installation

1. Download the latest release ZIP
2. Extract to your Mods folder (or install via SMAPI Launcher)
3. Launch the game via SMAPI

## Configuration

Edit `config.json` or use Generic Mod Config Menu in-game:

```json
{
  "ControllerLayout": "Switch",
  "ControlStyle": "Switch",
  "EnableConsoleChests": true,
  "EnableConsoleShops": true,
  "EnableConsoleToolbar": true,
  "EnableConsoleInventory": true,
  "EnableConsoleShipping": true,
  "EnableJournalButton": true,
  "EnableCutsceneSkip": true,
  "EnableCarpenterMenuFix": true,
  "EnableFurnitureDebounce": true,
  "UseBumpersInsteadOfTriggers": false,
  "VerboseLogging": false
}
```

| Option | Description |
|--------|-------------|
| `ControllerLayout` | Physical button layout: `Switch`, `Xbox`, or `PlayStation` |
| `ControlStyle` | Control scheme: `Switch` (right=confirm) or `Xbox` (bottom=confirm) |
| `EnableConsoleChests` | Sort (X), fill stacks (Y), sidebar navigation, color picker, and A/Y item transfer in chests |
| `EnableConsoleShops` | A button purchases, LT/RT quantity selector, sell tab with A/Y, right stick scroll |
| `EnableConsoleToolbar` | 12-slot fixed toolbar with LB/RB row switching and LT/RT slot movement |
| `EnableConsoleInventory` | A picks up/places items, Y picks up one from stack, fishing rod bait/tackle via Y |
| `EnableConsoleShipping` | A ships full stack, Y ships one item from the shipping bin |
| `EnableJournalButton` | Start button opens the Quest Log/Journal instead of inventory |
| `EnableCutsceneSkip` | Press Start twice during a skippable cutscene to skip it |
| `EnableCarpenterMenuFix` | Prevent Robin's building menu from instantly closing + joystick farm view controls |
| `EnableFurnitureDebounce` | Prevent furniture from rapid-toggling between picked up and placed |
| `UseBumpersInsteadOfTriggers` | Use LB/RB instead of LT/RT (for Xbox Bluetooth controllers) |
| `VerboseLogging` | Enable detailed debug logging |

## Building from Source

### Prerequisites
- .NET 6 SDK
- Stardew Valley installed (for reference assemblies)

### Build
```bash
cd AndroidConsolizer
dotnet build --configuration Release
```

Output: `bin/Release/net6.0/AndroidConsolizer X.X.X.zip`

## Troubleshooting

### Mod not loading
- Ensure SMAPI Android is properly installed
- Check the SMAPI log at `/storage/emulated/0/StardewValley/ErrorLogs/`

### Features not working
- Enable `VerboseLogging` in config to see detailed logs
- Check SMAPI log for error messages

### Toolbar not showing 12 slots
- Make sure `EnableConsoleToolbar` is `true` in config
- The feature only works during gameplay (not in menus)

### Triggers not working (Xbox controller)
- This is a known Android limitation with Xbox Bluetooth controllers
- Enable "Use Bumpers Instead of Triggers" in the mod settings as a workaround

## Compatibility

- **Stardew Valley Expanded**: Compatible
- **Content Patcher**: Compatible
- **Generic Mod Config Menu**: Compatible (optional)
- **Star Control**: NOT compatible with Android (don't use together)

## Known Issues

- Clicking "Build" for a building you can't afford exits to the shop screen (investigating whether this is vanilla Android behavior)
- Shop sell tab not navigable when switched via touchscreen tap (Y button works)
- Equipment slots not accessible via A button in inventory
- Community Center bundle navigation and cursor issues
- Social tab cursor doesn't visually follow when switching tabs with LB/RB
- Held items can be orphaned if touch input interrupts controller selection
- Geode breaking menu works but has no visual feedback with controller
- Analog triggers register multiple presses on some controllers (use Bumper Mode as workaround)
- Trash can lid animation doesn't play on Android with controller hover

## TODO / Roadmap

- Equipment slot A-button fix
- Right joystick free cursor mode for menus and gameplay
- Zoom control slider in options menu
- Intentional item drop with controller (L3 while holding item)
- Museum donation menu controller support
- Settings menu snap navigation
- Cutscene skip visual fix (skip button renders behind dialogue)
- Expanded controller testing (8BitDo, DualSense, etc.)

## Why "Consolizer"?

Android Stardew Valley has broken controller support that makes it nearly unplayable when docked to a TV (no touchscreen). This mod "consolizes" the experience - making it play like the Nintendo Switch version when using a controller.

## Credits

- Created by sonofskywalker3
- Uses [SMAPI](https://smapi.io/) modding framework
- Android SMAPI port by [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6)

## License

MIT License - Feel free to modify and redistribute.

## Changelog

### 3.2.0 — The Robin Release
- **Robin's Build Menu — Full Controller Support** - Build, Move, and Demolish all work with the joystick
  - Building ghost follows your cursor in real time across the farm
  - **Build mode**: Move cursor to desired location, press A to place the building
  - **Move mode**: Press A to select a building, move cursor to new location, press A to confirm
  - **Demolish mode**: Press A on a building to highlight it green, press A again to demolish. Move cursor away to safely deselect without demolishing
  - Left stick pans the viewport when cursor reaches screen edges
  - Visible cursor rendered in farm view so you always know where you're pointing
- **Furniture Placement Fix** - Y button no longer rapid-toggles furniture between picked up and placed
  - One press = one interaction, works for all furniture types including beds
  - New `EnableFurnitureDebounce` config toggle (enabled by default)

### 3.1.0
- **Cleanup Update** - No behavior changes, internal housekeeping only
  - Consolidated 12 granular GMCM toggles into 5 feature groups: Console Chests, Console Shops, Console Toolbar, Console Inventory, Console Shipping
  - Removed dead code across ButtonRemapper, InventoryManagementPatches, ItemGrabMenuPatches, GameplayButtonPatches, and ModEntry
  - Cached all per-call reflection lookups (cutscene skip, inventory hover, chest sidebar buttons) for better performance
  - Cleaned up stale version references in log messages
  - **Note:** Existing `config.json` files will reset to defaults due to renamed config properties. Re-configure via GMCM if needed.

### 3.0.0
- **Console-Style Chest Item Transfer** - Chests now work like Nintendo Switch
  - **A button** instantly transfers full stack between chest and inventory (no selection step)
  - **Y button** transfers one item (hold Y for rapid single-item transfer)
  - **RB** snaps cursor to Fill Stacks button
  - Works bidirectionally for all chest types (regular chests, fishing treasure, fridge, etc.)
- **Chest Sidebar Navigation** - All sidebar buttons reachable via controller
  - Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X
  - Close X properly closes chest without reopening (A suppress-until-release)
- **Color Picker Swatch Navigation** - Full 7x3 grid navigation
  - Cursor snaps to first swatch on open, A selects color
  - B closes picker only (not the chest) via exitThisMenu same-tick guard
  - Visual stride detection probes picker at runtime for correct cursor positioning
  - Color preserved after probe (click at saved position to restore)

### 2.9.0
- **Shop Controls Overhaul** - Major improvements to shop controller experience
  - **LB/RB quantity adjustment** with hold-to-repeat (+/-1 in bumper mode, +/-10 otherwise)
  - **Right stick fast navigation** on buy tab — jump 5 items at a time with hold-to-repeat
  - **Controller button icon** on the tab-switch button — shows Y/X/square depending on layout, dims correctly on sell tab
  - **Touch tab button blocked** when controller connected — prevents accidental touchscreen taps toggling tabs
  - **Grayed-out item sell fix** — shops no longer let you sell items they don't accept
  - **Cutscene skip** — press Start twice to skip cutscenes
  - Right stick vanilla scroll desync fully fixed (vanilla scroll blocked at GamePad.GetState level)
  - Sell tooltip for unsellable items no longer shows

### 2.8.0
- **Release cleanup** - No behavior changes
  - Debug/Trace logging silenced by default (VerboseLogging now defaults to false)
  - All debug logs gated behind VerboseLogging toggle — enable in GMCM when needed
  - Removed unused legacy config properties
  - Y-sell feedback promoted to INFO for consistency with A-sell

### 2.7.x (2.7.1 — 2.7.21)
- **Carpenter Menu Fix** (v2.7.2-v2.7.4) - Robin's building menu no longer instantly closes
- **Fishing Mini-Game Fix** (v2.7.1) - X/Y button swap now applies during bobber bar
- **Shop Purchase Overhaul** (v2.7.5-v2.7.14) - Complete rewrite of purchase logic
  - Trade item shops (Desert Trader) now work correctly
  - Tool upgrades, recipes, and special purchases handled via actionWhenPurchased
  - Inventory-full refunds return both money and trade items
  - Fixed phantom purchases when switching between buy/sell tabs
  - Buy quantity no longer bleeds to sell tab
- **Console-Style Shop Selling** (v2.7.16-v2.7.21) - Full sell-tab controller support
  - A sells entire stack, Y sells one, hold Y for rapid sell
  - Sell price tooltip with gold coin icon positioned next to selected item
  - Snap-navigation-based item detection (hoveredItem doesn't work on Android sell tab)
- **Performance** (v2.7.15) - Cached reflection fields in InventoryManagementPatches

### 2.7.0
- **Console-Style Inventory Management** - A button now works like Nintendo Switch
  - A picks up entire stack to cursor, A again places or swaps
  - Y picks up a single item from a stack, hold Y for continuous pickup
  - Held items render visually at cursor slot position
  - Controller hover tooltips appear when navigating inventory
  - Fishing rod tooltip shown when holding bait or tackle (to see rod info before attaching)
- **Improved Fishing Rod Bait/Tackle** - Detaching bait or tackle now puts it on the cursor instead of first empty slot, consistent with console behavior

### 2.6.0
- **Fishing Rod Bait/Tackle Fix** - Controller support for attaching and detaching bait/tackle from fishing rods
  - Press A on bait or tackle to select it
  - Press Y on a fishing rod to attach the selected bait/tackle
  - Press Y on a fishing rod with nothing selected to detach (bait first, then tackle)
  - Supports stacking same bait type and swapping different bait/tackle

### 2.5.1
- **Fixed shop stock bug** - Limited stock items now properly decrement when purchasing partial quantities

### 2.5.0
- **Fixed X button inventory deletion bug** - Critical fix for Xbox layout + Switch style combination
- Renamed "Use D-Pad for Toolbar" to "Use Bumpers Instead of Triggers"
- Added LB/RB shop quantity adjustment when bumper mode is enabled
- Added "Start Opens Journal" feature
- Fully tested with Odin built-in controls and external Xbox Wireless controllers via Bluetooth

### 2.4.0
- **Xbox controller support** - Tested with Xbox Wireless Controller
- **Bumper mode** - Use LB/RB instead of triggers for toolbar and shops
- **Controller layout settings** - Support for Switch, Xbox, and PlayStation button layouts
- **Control style settings** - Choose between Switch-style or Xbox-style confirm/cancel

### 2.1.0
- **Console-style shipping bin** - Complete rewrite using game's native shipping flow
- A button ships entire stack, Y button ships one item
- "Last shipped" display now works properly
- Fixed toolbar selection box sizing

### 2.0.0
- **Rebranded to Android Consolizer**
- **Console-style toolbar** - 12-slot rows with LB/RB to switch rows, LT/RT to move within row
- Custom toolbar rendering that matches console layout

### 1.0.0
- Initial stable release
- Shop purchasing (A button)
- Inventory/chest sorting (X button)
- Add to stacks in chests (Y button)
- X button deletion bug blocked
