# Android Consolizer

A SMAPI mod that brings console-style controller support to Android Stardew Valley. Play with a controller like you would on Nintendo Switch - 12-slot toolbar rows, proper shop purchasing, chest management, and more.

## Current Version: 2.1.0

## Features

### Toolbar Navigation (Console-Style)
- **12-slot toolbar rows** instead of Android's chaotic scrolling toolbar
- **LB/RB**: Switch between toolbar rows (up to 3 rows with full backpack)
- **LT/RT**: Move left/right within the current row
- Visual toolbar matches console layout

### Shop Fixes
- **A button**: Purchase items from any shop
- **LT/RT**: Adjust purchase quantity before buying
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
| **Shipping Bin** | A | Ship entire stack |
| **Shipping Bin** | Y | Ship one item |

## Dependencies

**Required:**
- Stardew Valley 1.6.15+ (Android)
- [SMAPI 4.0.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6) by NRTnarathip

**Optional:**
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) - for in-game settings

## Tested Devices

- **AYN Odin** (Android gaming handheld)

If you test on other devices, please report your results!

## Installation

1. Download the latest release ZIP
2. Extract to your Mods folder (or install via SMAPI Launcher)
3. Launch the game via SMAPI

## Configuration

Edit `config.json` or use Generic Mod Config Menu in-game:

```json
{
  "EnableShopPurchaseFix": true,
  "EnableToolbarNavFix": true,
  "EnableSortFix": true,
  "EnableAddToStacksFix": true,
  "EnableShippingBinFix": true,
  "VerboseLogging": false
}
```

| Option | Description |
|--------|-------------|
| `EnableShopPurchaseFix` | A button purchases in shops |
| `EnableToolbarNavFix` | Console-style toolbar with LB/RB/LT/RT |
| `EnableSortFix` | X button sorts inventory/chests |
| `EnableAddToStacksFix` | Y button adds to stacks in chests |
| `EnableShippingBinFix` | A button stacks items in shipping bin |
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
