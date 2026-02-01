# Changelog

All notable changes to AndroidControllerFix will be documented in this file.

## [0.3.4] - 2026-01-31

### Changed
- **New button scheme**: X = Sort, Y = Add to stacks
- Removed broken buy/sell toggle from active features
- Simplified GMCM config menu
- GMCM now shows current button mappings at top

### Added
- Inventory sorting (X button when in inventory menu)
- Config options: EnableSortFix, EnableAddToStacksFix

### Fixed
- X button in inventory/chest now triggers sort instead of potentially triggering deletion bug

## [0.3.3] - 2026-01-31

### Added
- Detailed logging for all button presses in chest menu
- Support for Back/Start/BigButton as organize alternatives

### Fixed
- Improved button detection in ItemGrabMenu

## [0.3.2] - 2026-01-31

### Fixed
- Shop purchase now respects quantity selected via LT/RT
- Properly calculates total cost based on quantity
- Resets quantity to 1 after purchase

## [0.3.1] - 2026-01-31

### Added
- Extensive debug logging for shop purchases
- Multiple fallback methods for purchase

### Changed
- Purchase now tries direct method call before manual implementation

## [0.3.0] - 2026-01-31

### Added
- Initial beta release
- Shop purchase fix (A button)
- Chest add-to-stacks (X button) - working
- Buy/sell toggle (Y button) - not working on Android
- GMCM configuration support
- Verbose logging option

## [Unreleased]

### Planned
- Toolbar navigation fix (console-style 12-item rows)
- Buy/sell toggle fix (need different approach)
- Fishing rod bait removal fix
- Full button remapping system
