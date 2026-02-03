# Android Consolizer

A SMAPI mod that brings console-style controller support to Android Stardew Valley. Play with a controller like you would on Nintendo Switch - 12-slot toolbar rows, proper shop purchasing, chest management, and more.

## Current Version: 2.7.0

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
- **A button**: Purchase items from any shop
- **LT/RT** (or **LB/RB** in bumper mode): Adjust purchase quantity before buying
- Respects available stock and player money

### Inventory & Chest Fixes
- **X button (Inventory)**: Sort your inventory
- **X button (Chest)**: Sort chest contents
- **Y button (Chest)**: Add matching items to existing stacks
- **X button deletion bug fixed**: The vanilla Android bug where X deletes items is completely blocked

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
| **Shop** | A | Purchase selected quantity |
| **Shop** | LT/RT | Adjust purchase quantity |
| **Inventory** | X | Sort inventory |
| **Chest** | X | Sort chest contents |
| **Chest** | Y | Add to existing stacks |
| **Inventory** | A | Pick up / place / swap item |
| **Inventory** | Y | Pick up single from stack (hold for continuous) |
| **Shipping Bin** | A | Ship entire stack |
| **Shipping Bin** | Y | Ship one item |
| **Inventory** | A (on bait/tackle) | Pick up bait/tackle to cursor |
| **Inventory** | Y (on fishing rod) | Attach held bait/tackle or detach to cursor |

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
| **Xbox One Wireless (Bluetooth)** | AYN Odin Pro | ✅ Fully Working | Triggers cannot be detected in game, enable "Use Bumpers Instead of Triggers" |
| **Xbox Series X|S Wireless (Bluetooth)** | AYN Odin Pro | ✅ Fully Working | Triggers cannot be detected in game, enable "Use Bumpers Instead of Triggers" |

### Known Issues: Xbox Controller on Android

Xbox Wireless Controllers connected via Bluetooth have a known issue on Android where the analog triggers (LT/RT) are not detected by Stardew Valley. This is due to Xbox controllers reporting triggers on different axes (`AXIS_GAS`/`AXIS_BRAKE`) than what the game's framework expects (`AXIS_LTRIGGER`/`AXIS_RTRIGGER`).

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
  "EnableShopPurchaseFix": true,
  "EnableToolbarNavFix": true,
  "UseBumpersInsteadOfTriggers": false,
  "EnableSortFix": true,
  "EnableAddToStacksFix": true,
  "EnableShippingBinFix": true,
  "EnableFishingRodBaitFix": true,
  "EnableConsoleInventoryFix": true,
  "VerboseLogging": false
}
```

| Option | Description |
|--------|-------------|
| `ControllerLayout` | Physical button layout: `Switch`, `Xbox`, or `PlayStation` |
| `ControlStyle` | Control scheme: `Switch` (right=confirm) or `Xbox` (bottom=confirm) |
| `EnableShopPurchaseFix` | A button purchases in shops |
| `EnableToolbarNavFix` | Console-style toolbar with LB/RB/LT/RT |
| `UseBumpersInsteadOfTriggers` | Use LB/RB instead of LT/RT (for Xbox Bluetooth controllers) |
| `EnableSortFix` | X button sorts inventory/chests |
| `EnableAddToStacksFix` | Y button adds to stacks in chests |
| `EnableShippingBinFix` | A button stacks items in shipping bin |
| `EnableFishingRodBaitFix` | Y button attaches/detaches bait and tackle from fishing rods |
| `EnableConsoleInventoryFix` | Console-style A button pick up/place/swap in inventory |
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
- Make sure `EnableToolbarNavFix` is `true` in config
- The feature only works during gameplay (not in menus)

### Triggers not working (Xbox controller)
- This is a known Android limitation with Xbox Bluetooth controllers
- Enable "Use Bumpers Instead of Triggers" in the mod settings as a workaround

## Compatibility

- **Stardew Valley Expanded**: Compatible
- **Content Patcher**: Compatible
- **Generic Mod Config Menu**: Compatible (optional)
- **Star Control**: NOT compatible with Android (don't use together)

## Why "Consolizer"?

Android Stardew Valley has broken controller support that makes it nearly unplayable when docked to a TV (no touchscreen). This mod "consolizes" the experience - making it play like the Nintendo Switch version when using a controller.

## Credits

- Created by sonofskywalker3
- Uses [SMAPI](https://smapi.io/) modding framework
- Android SMAPI port by [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6)

## License

MIT License - Feel free to modify and redistribute.

## Changelog

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
