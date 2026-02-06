# Bug: Can Sell Grayed-Out Items + Tooltip Shows Price for Unsellable Items

**Reported:** 2026-02-05
**Version:** v2.8.6
**Status:** Open

## Summary

The mod allows selling items that are grayed out in the shop's sell tab, and shows sell price tooltips for items the current shop doesn't accept. Each shop has its own list of acceptable items - it's not determined by item class alone.

## Current Behavior

1. **Selling grayed items:** Player can press A on a grayed-out item in the sell tab and the mod will sell it, even though the shop shouldn't accept it.

2. **Tooltip for unsellable items:** The sell price tooltip appears for items that are grayed out / not accepted by the current shop.

## Expected Behavior

1. A button should only sell items that the shop actually accepts (items that are NOT grayed out)
2. Tooltip should only appear for items the shop accepts

## Root Cause

The current code only checks if an item has a positive `sellToStorePrice()` value. It does NOT check whether the current shop accepts that item category.

Each shop has different rules:
- Pierre's: crops, foraged items, artisan goods, etc.
- Willy's: fish
- Clint's upgrade shop: nothing (sell tab probably shouldn't even work)
- etc.

## Technical Investigation Needed

The ShopMenu likely has a method or field that determines what items it will buy. Possible locations:
- `ShopMenu.highlightItemToSell()` - this is what grays out items
- `ShopMenu.canSellItem()` or similar method
- A category whitelist/blacklist on the ShopMenu instance
- Check how vanilla determines item highlighting in the sell tab

## Files to Modify

- `Patches/ShopMenuPatches.cs`
  - `ReceiveGamePadButton_Prefix` - sell logic (A button full stack, Y button single)
  - `SellOneItem` - the actual sell method
  - `ShopMenu_Draw_Postfix` - tooltip drawing

## Fix Approach

1. Find how vanilla determines if an item can be sold at the current shop
2. Add that check before:
   - Allowing A/Y button to sell
   - Showing the sell price tooltip
3. If item can't be sold, play "cancel" sound and don't process the sale
