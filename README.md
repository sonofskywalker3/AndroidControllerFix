# Android Controller Fix

A SMAPI mod that fixes broken controller support on Android Stardew Valley, enabling shop purchasing, inventory/chest sorting, and other actions that normally require touchscreen.

## Current Version: 1.0.0

## Features

- **Shop Purchase (A button)**: Purchase items from any shop with controller
  - Use LT/RT to adjust quantity before pressing A
  - Respects available stock and player money
- **Sort Inventory (X button)**: Sorts your inventory when in the inventory menu
- **Sort Chest (X button)**: Sorts chest contents when viewing a chest
- **Add to Stacks (Y button)**: Adds matching items from your inventory to existing stacks in chest
- **Fixes X Button Deletion Bug**: The vanilla Android game has a bug where pressing X in the inventory deletes the selected item. This mod intercepts the X button input before the game sees it, completely preventing this bug.

### Button Mappings

| Context | Button | Action |
|---------|--------|--------|
| Shop | A | Purchase selected quantity |
| Shop | LT/RT | Adjust purchase quantity |
| Inventory Menu | X | Sort inventory |
| Chest Menu | X | Sort chest contents |
| Chest Menu | Y | Add to existing stacks |

### Known Limitations
- **Buy/Sell Toggle**: Y button in shops doesn't switch between buy and sell tabs (Android limitation)

## Dependencies

**Required:**
- Stardew Valley 1.6.15+ (Android)
- [SMAPI 4.0.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6) by NRTnarathip

**Optional:**
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) - for in-game settings

## Tested Devices

This mod has only been tested on:
- **AYN Odin** (Android gaming handheld)

If you test on other devices, please report your results!

## Installation

1. Download the latest release ZIP
2. Extract the `AndroidControllerFix` folder
3. Copy to your Stardew Valley mods folder:
   `/storage/emulated/0/StardewValley/Mods/`
4. Launch the game via SMAPI Launcher

## Configuration

Edit `config.json` or use Generic Mod Config Menu in-game:

```json
{
  "EnableShopPurchaseFix": true,
  "EnableSortFix": true,
  "EnableAddToStacksFix": true,
  "VerboseLogging": false
}
```

## Building from Source

### Prerequisites
- .NET 6 SDK
- Stardew Valley installed (for reference assemblies)

### Build
```bash
cd AndroidControllerFix
dotnet build --configuration Release
```

Output: `bin/Release/net6.0/AndroidControllerFix X.X.X.zip`

## Troubleshooting

### Mod not loading
- Ensure SMAPI Android is properly installed
- Check the SMAPI log at `/storage/emulated/0/StardewValley/ErrorLogs/`

### Features not working
- Enable `VerboseLogging` in config to see detailed logs
- Check SMAPI log for error messages

## Compatibility

- **Stardew Valley Expanded**: Compatible
- **Content Patcher**: Compatible
- **Generic Mod Config Menu**: Compatible (optional)
- **Star Control**: NOT compatible with Android (don't use together)

## Credits

- Created by sonofskywalker3
- Uses [SMAPI](https://smapi.io/) modding framework
- Android SMAPI port by [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6)

## License

MIT License - Feel free to modify and redistribute.

## Changelog

### 1.0.0
- **Fixes X button deletion bug** - The X button no longer deletes your selected item in vanilla Android Stardew Valley. Input is intercepted before the game sees it.
- All features tested and working on AYN Odin

### 0.3.x (Beta)
- Shop purchase fix (A button)
- Inventory and chest sorting (X button)
- Add to existing stacks in chests (Y button)
- Generic Mod Config Menu support
