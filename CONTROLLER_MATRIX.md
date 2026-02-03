# Controller Compatibility Matrix

This document tracks controller testing across different Android devices.

## Test Devices

| Device | OS | Notes |
|--------|-----|-------|
| AYN Odin Pro | Android | Primary test device |

## Controller Test Results

### AYN Odin Pro

| Controller | Connection | Status | Notes |
|------------|------------|--------|-------|
| **Built-in (Odin)** | N/A | ✅ Working | All buttons and triggers work |
| **Xbox One Wireless** | Bluetooth | ✅ Working | Triggers not detected - enable "Use Bumpers Instead of Triggers" |
| **Xbox Series X\|S Wireless** | Bluetooth | ✅ Working | Triggers not detected - enable "Use Bumpers Instead of Triggers" |
| **Nintendo Switch Pro Controller** | Bluetooth | ❌ Cannot Test | Will not pair with Odin |
| **PlayStation DualShock 4 (PS4)** | Bluetooth | ⏳ To Test | |
| **PlayStation DualSense (PS5)** | Bluetooth | ⏳ To Test | |

### 3rd Party Controllers (To Be Added)

| Controller | Connection | Status | Notes |
|------------|------------|--------|-------|
| | | | |

## Status Legend

- ✅ Working - Fully functional (with noted workarounds if any)
- ⚠️ Partial - Some features work, some don't
- ❌ Not Working - Does not function with the mod
- ❌ Cannot Test - Cannot pair/connect to device
- ⏳ To Test - Not yet tested

## Known Issues by Controller Type

### Xbox Controllers (Bluetooth)
- Analog triggers (LT/RT) report on different axes (`AXIS_GAS`/`AXIS_BRAKE`) than what Android/Stardew expects (`AXIS_LTRIGGER`/`AXIS_RTRIGGER`)
- **Workaround:** Enable "Use Bumpers Instead of Triggers" in mod settings

### Nintendo Switch Pro Controller
- Pairing issues with AYN Odin Pro - needs testing on other Android devices

## Future Test Devices

- Other Android phones/tablets
- Other Android gaming handhelds (ROG Ally, Retroid, etc.)
