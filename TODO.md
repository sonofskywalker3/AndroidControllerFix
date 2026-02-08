# AndroidConsolizer - TODO List

**Read this file when working on any bug fix or feature.** Each item has detailed implementation notes, root cause analysis, and file references.

Items marked with RE-IMPLEMENT were working in v2.9.0 but lost when reverting to v2.7.0 due to the v2.7.10 regression. See `POSTMORTEM.md` for details.

---

## Features to Re-implement (Lost in v2.7.10-v2.9.2 Revert)

These need to be re-implemented **one at a time, one per 0.0.1 patch, each committed separately.**

### From v2.9.0 — Must Re-implement
1. ~~**Robin's Building Menu Fix**~~ — **DONE in v2.7.2-v2.7.4**
   - Prefix patches on `releaseLeftClick`, `leftClickHeld`, and `exitThisMenu` with 20-tick grace period.
   - **File:** `Patches/CarpenterMenuPatches.cs`

2. ~~**Shop Purchase Flow Fix (CRITICAL)**~~ — **DONE in v2.7.5-v2.7.14**
   - Purchase logic calls `actionWhenPurchased(shopId)`, checks/consumes trade items, handles inventory-full refunds.
   - **File:** `Patches/ShopMenuPatches.cs`

3. ~~**Fishing Mini-Game Button Fix**~~ — **DONE in v2.7.1**
   - Fixed in `GameplayButtonPatches.cs`: X/Y swap now applies when `BobberBar` is active.

4. ~~**Shop Quantity Enhancement**~~ — **DONE (never lost, confirmed working v2.8.9)**
   - Non-bumper mode: LB/RB = +/-10. Hold-to-repeat with 333ms initial delay then 50ms repeat. Quantity limits respect stock, money, trade items, and stack size.
   - **Files:** `ModEntry.cs` (initial press in OnButtonsChanged), `Patches/ShopMenuPatches.cs` (auto-repeat in Update_Postfix)

### From v2.8.0 — DO NOT Re-implement (Caused the Regression)
5. **Logic Extraction Refactor** — ~~Extracted pure logic into Logic/ helper classes~~
   - **DO NOT RE-IMPLEMENT.** This refactoring is what introduced the v2.7.10 inventory regression. See `POSTMORTEM.md`.
   - The inline logic in the patch files works correctly. Extracting it into separate classes changed behavior in subtle ways.
   - If testing infrastructure is ever needed, write tests that call the patch methods directly rather than extracting logic into separate classes.

6. **Decision Logging** — ~~Structured decision logs for regression testing~~
   - **Low priority.** Can be added later as its own patch if needed. VerboseLogging already exists and works.

---

## High Priority (Core Gameplay Feel)

### 1. Robin's Building Menu Fix — FIXED in v2.7.4
- **Root cause (identified v2.7.3):** The A button press from Robin's dialogue carries over as a mouse-down state. `snapToDefaultClickableComponent()` snaps to the cancel button (ID 107). When A is released, the call chain is: `releaseLeftClick()` → `OnReleaseCancelButton()` → `exitThisMenu()`. The four standard input methods (`receiveLeftClick`, `receiveKeyPress`, `receiveGamePadButton`) are NEVER called — only `leftClickHeld` (from the held state) and `releaseLeftClick` (on release).
- **Fix (v2.7.4):** Prefix patches on `releaseLeftClick`, `leftClickHeld`, and `exitThisMenu` (safety net) with 20-tick grace period after menu opens.
- **File:** `Patches/CarpenterMenuPatches.cs`
- **Config:** `EnableCarpenterMenuFix` in ModConfig.cs

### 1b. CarpenterMenu Move Buildings — Joystick Doesn't Pan Map
- **Symptom:** After opening Robin's building menu and selecting "Move Buildings," the joysticks do not move/pan the farm map view. Neither left nor right stick has any effect.
- **Confirmed on:** Same test session as #1 fix (v2.7.3). User tried LeftThumbstick in all directions, then RightThumbstick in all directions. No map movement. Had to use touch (MouseLeft) to interact.
- **Log evidence (v2.7.3):**
  ```
  21:41:19-21:41:31 — LeftThumbstickLeft/Right/Down repeatedly, no game response
  21:41:27 — ControllerA pressed (likely selected "Move Buildings")
  21:41:28-21:41:39 — Both LeftThumbstick and RightThumbstick in all directions, no game response
  21:41:40 — User gave up, used MouseLeft (touch) to interact
  ```
- **Root cause hypothesis:** CarpenterMenu's move-buildings mode expects mouse/touch drag to pan the farm view. On console (Switch), the left stick or d-pad scrolls the map viewport. On Android, the game likely only checks touch/mouse position for viewport movement in this mode, not gamepad stick input.
- **Investigation needed:**
  - Decompile `CarpenterMenu` and look at the move-buildings update loop — how does it handle viewport scrolling? Look for `Game1.viewport`, `Game1.panScreen`, or similar calls.
  - On Switch, CarpenterMenu probably calls `Game1.panScreen()` based on gamepad stick direction. The Android port may have stripped this or it may require a specific input path.
  - Check if `Game1.panScreen()` is called anywhere in the CarpenterMenu update when in move mode. If not, we need to add it.
- **Implementation approach:** Likely need a postfix on `CarpenterMenu.update(GameTime)` that reads left stick direction and calls `Game1.panScreen(direction, speed)` when in move-buildings mode. Need to detect the move-buildings state (probably a field like `moving` or `demolishing` on CarpenterMenu).
- **File:** `Patches/CarpenterMenuPatches.cs`

### 2. Shop Purchase Flow Bug (CRITICAL) — FIXED in v2.7.5-v2.7.14
- Purchase logic calls `actionWhenPurchased(shopId)`, checks/consumes trade items, handles inventory-full refunds.
- Uses `hoveredItem` validated against `forSale` list (forSaleButtons myIDs are all -500 on Android).
- Sell-tab detection via `inventoryVisible` field prevents phantom purchases.
- Buy quantity reset on sell tab prevents trigger bleed.

### 3. Fishing Mini-Game Button Mismatch — DONE (v2.7.1)
- Fixed: `GameplayButtonPatches.GetState_Postfix` now applies X/Y swap when `BobberBar` is active.

### 4. Shop Quantity Increment Enhancement — DONE (confirmed working v2.8.9)
- Non-bumper mode: LB/RB = +/-10, bumper mode: LB/RB = +/-1
- Hold-to-repeat with 333ms delay then 50ms repeat
- Quantity limits respect stock, money, trade items, and stack size
- **Implementation:** Initial press in `ModEntry.OnButtonsChanged`, auto-repeat in `ShopMenuPatches.Update_Postfix`

### 5. Cutscene Skip with Controller
- **Desired behavior:** Press Start once to show the skip button, press Start again within 3 seconds to confirm skip. Double-press-to-skip prevents accidental skips.
- On Android touchscreen, the skip button appears on screen and you tap it. With a controller, there's no way to activate the skip button.
- **Log evidence (v2.7.2):** User entered Town at 21:22:44, cutscene triggered. Pressed ControllerStart 4 times (21:22:50-21:22:52) — no effect. Then tapped screen (MouseLeft x2 at 21:22:53-21:22:54) to skip. Cutscene ended at 21:22:55 ("Warping to Town").
- **Mechanism:** Touchscreen skip uses `MouseLeft` (two taps — first shows skip button, second confirms). The cutscene is an `Event` object. The skip button is likely `Event.skippable` + a clickable component. On Android, `Event.receiveLeftClick` or similar handles the skip confirmation.
- **Known issue (v2.8.2):** Current implementation shows "press Start again to skip" text, but it renders behind the dialogue text box and is not visible.
- **Desired fix:** Remove the text prompt entirely. Instead, first Start press should show the same skip icon/button that the touchscreen tap shows (top-right corner of screen). Second Start press confirms the skip. This matches the touch UX — same visual, just triggered by Start instead of tap.
- **Implementation approach:** When a skippable event is active and Start is pressed, show the vanilla skip button UI (same one touch uses, positioned top-right). Second press within 3 seconds confirms the skip. Need to find the exact method — likely `Event.skipEvent()` or `Event.receiveLeftClick()` at the skip button coordinates.
- **Next step:** Decompile or inspect `Event` class to find the skip mechanism. Look for `skippable`, `skipEvent`, `skipped` fields/methods. Also find how the touch skip icon is shown/hidden to reuse it.

### 5b. Shop Inventory Tab Broken with Controller — DONE (v2.8.9-v2.8.17, shipped in v2.9.0)
- Touch tab button blocked when controller connected (v2.8.12)
- Controller button icon drawn on shop UI — Y/X/Square depending on layout, dims on sell tab (v2.8.13-v2.8.17)
- Tab switching works via controller button for all layouts
- Selling works normally on sell tab

### 5c. Buy Quantity Bleeds to Sell Tab — FIXED in v2.7.14
- **Symptom:** When on the sell tab, bumpers and triggers still adjust the buy quantity (`quantityToBuy` field). If the touch sell quantity dialog is open, triggers move both the sell dialog slider AND the buy quantity simultaneously.
- **Log evidence (v2.7.13):** RightShoulder pressed twice (11:33:59) while on sell tab at Pierre's. The touch sell dialog's quantity slider moved, and the buy quantity was also affected.
- **Root cause:** `HandleShopBumperQuantity` in ModEntry.cs runs on any LB/RB press in ShopMenu without checking `inventoryVisible`. Additionally, the vanilla game's trigger input modifies `quantityToBuy` regardless of which tab is active.
- **Fix (v2.7.14):** Guard `HandleShopBumperQuantity` with `inventoryVisible` check (return early if on sell tab). Also reset `quantityToBuy` to 1 in `Update_Postfix` whenever `inventoryVisible` is true, to catch vanilla trigger input leaking to the buy quantity while on sell tab.

### 5d. Console-Style Shop Selling — DONE (v2.7.16-v2.7.20)
- **A** sells the full stack, **Y** sells one, **Hold Y** sells repeatedly (~333ms delay, ~50ms repeat)
- Sell price tooltip: custom-drawn box positioned next to the selected inventory slot showing per-unit and stack total
- **Key finding (v2.7.19):** `hoveredItem` is NEVER set on the sell tab with controller — `performHoverAction` uses mouse position which doesn't track snap navigation on Android. All sell-tab item detection uses `GetSellTabSelectedItem()` which reads `currentlySnappedComponent` and maps it to the inventory slot index.
- Sell price: `Object.sellToStorePrice()` for Object items (accounts for quality/professions), `salePrice()/2` for non-Object items (rings, boots, weapons)
- **File:** `Patches/ShopMenuPatches.cs`

### 5e. Shop Buy List Right Stick Scrolling — DONE (v2.8.18-v2.8.22, shipped in v2.9.0)
- Vanilla right stick scroll blocked at GamePad.GetState level (`ShouldSuppressRightStick()` in ShopMenuPatches, zeroed in GameplayButtonPatches.GetState_Postfix)
- Replaced with right stick jump-5-items navigation using simulated DPad presses (selection AND view move together)
- Hold-to-repeat with 250ms delay, 100ms repeat rate
- Raw right stick Y cached in `GameplayButtonPatches.RawRightStickY` for our navigation code
- **File:** `Patches/ShopMenuPatches.cs`, `Patches/GameplayButtonPatches.cs`

---

## Medium Priority (Menu/UI Parity)

### 6. Furniture Placement Fix
- Y button rapid-toggles between placed and picked up
- Confirmed on Odin Pro and Logitech G Cloud. A does NOT pick up furniture, only Y does (same as Switch)
- Switch has same Y-toggle behavior but with ~500ms delay between each cycle. Android has no delay.
- Logs show `ControllerX` (Switch layout remap) firing 6 times in 3 seconds during testing
- G Cloud testing: Confirmed holding button flashes furniture in and out rapidly
- NOT a different mechanic from console - just a missing debounce
- Fix approach: Prefix patch on `Furniture.performToolAction()` with cooldown timer
  - Track last furniture interaction tick (`Game1.ticks`)
  - Block subsequent calls within ~30 ticks (500ms at 60fps)
  - Only affects furniture interactions - tools (axe, pickaxe, hoe, etc.) use completely different code paths and are unaffected
- May also need to patch/debounce the furniture placement path (when placing from inventory) if that also rapid-fires

### 7. Console-Style Chest Item Transfer
- A/Y should directly transfer items between chest and inventory
- **Current behavior (confirmed on G Cloud):** In regular chests AND fishing chests, pressing A on an item selects it (red outline), then you must navigate and press A again to place it. This is the vanilla Android behavior. Uses red selection box instead of attaching item to cursor.
- **Desired behavior (matches console):**
  - **A** on a chest item immediately transfers the full stack to the first open inventory slot. A on an inventory item immediately transfers the full stack into the chest.
  - **Y** transfers a single item in either direction (one from stack to/from chest).
  - No selection step needed. Much faster workflow: grab items, then sort afterwards.
  - Item should attach to the cursor (not red selection box) for visual consistency with console behavior.
- **Scope:** Applies to all `ItemGrabMenu` instances — regular chests, fishing treasure chests, fridge, etc. Must work bidirectionally (chest->inventory AND inventory->chest).
- **Implementation:** Patch `ItemGrabMenu` to intercept A/Y on both chest and inventory items and call the appropriate transfer method instead of the default select behavior. Use cursor-attached visual rather than red outline.
- **DONE in v2.9.31.** A/Y intercept in `ReceiveGamePadButton_Prefix` after side-button handler. Four transfer methods handle full-stack and single-item in both directions. Config toggle: `EnableChestTransferFix`.

### 7b. RB Snaps to Fill Stacks Button in Chest — DONE (v2.9.34)
- RB snaps cursor to Fill Stacks button in `ReceiveGamePadButton_Prefix`. Skips when color picker is open.
- **File:** `Patches/ItemGrabMenuPatches.cs`

### 8. Equipment Slot Placement Bug
- Can navigate to equipment slots but A button does nothing
- **Symptom:** In the inventory page, selected a ring with A (red outline), navigated to the ring equipment slot, pressed A — nothing happened. The ring was not equipped.
- Confirmed on Logitech G Cloud.
- Touch/drag DOES work to equip items, but using touch while an item is controller-selected causes the controller-selected item to be abandoned outside any slot.
- **CRITICAL SIDE EFFECT:** When the menu was closed with an item "held" (selected but not in a slot), the item dropped on the ground and was lost (ring fell in river). See #11 for safety net.
- **Investigation:** Check if `InventoryManagementPatches` handles equipment slot IDs. Equipment slots have different component IDs than the 36-slot inventory grid. The A-button handler may only be coded for inventory grid slots, not equipment slots (hat, shirt, pants, boots, ring1, ring2).
- Fix: Extend A-button handling to recognize equipment slot component IDs and call the appropriate equip method.
- **Read `ANDROID_INVENTORY_NOTES.md` before working on this.**

### 9. Community Center Bundle Navigation + Cursor Bug
- Multiple issues with CC bundle controller interaction
- **Access method (working):** CC icon is on the tab line just right of the last GameMenu tab. Accessible from anywhere in the game. LB/RB switches between rooms correctly. This is vanilla behavior and works fine.
- **Bug 1 - Can't switch bundles within a room:** When viewing a specific room's bundles, cannot use bumpers or triggers to switch between individual bundles within that room. Can only switch rooms. Need to confirm whether Switch allows intra-room bundle switching (user believes it does).
- **Bug 2 - Cursor jumps when selecting items for donation:** When in a bundle's item submission view, the red outline cursor is used. Moving down to select an item (e.g., frog) and pressing A causes the cursor to jump ~4 slots and select the wrong item (e.g., catfish). Nothing gets added to the bundle.
- **Bug 3 - Bundle interaction at actual CC building:** When standing at a specific room tile in the actual Community Center, limited to viewing only that room's bundles (as expected), but the cursor/selection issues from Bug 2 still apply.
- Confirmed on Logitech G Cloud.
- **Fix approach:** Apply the same cursor/snap navigation patches used in inventory to the bundle donation menu. The 4-slot jump suggests the cursor is interpreting A-press coordinates incorrectly (same class of bug as the inventory A-button-as-click issue).
- May need patches on `JunimoNoteMenu` (the bundle UI class).

### 10. Trash Can / Sort Unreachable in Item Grab Menus — FIXED in v2.9.8
- **Fixed:** Sidebar buttons (Sort Chest, Fill Stacks, Color Toggle, Sort Inventory, Trash, Close X) are now reachable via snap navigation.
- Close X: A simulates B press + suppress-A-until-release at GetState level (v2.9.28). Replaces the old 120-tick reopen hack.
- See `CHESTNAV_SPEC.md` for the full navigation wiring spec.

### 11. Touch Interrupt Returns Held Item + Intentional Item Drop
- Two related issues with held items
- **Bug — Touch orphans controller-held items:** When an item is held by the cursor via our controller code (A-button pickup), using the touchscreen does NOT return that item to the inventory. Instead, the item remains "floating" on screen — still attached to the cursor but no longer associated with any slot. The cursor itself gets pulled away by touch, leaving the item orphaned. If the menu is then closed while the item is not in any inventory slot, it drops on the ground. User lost a ring to the river this way.
- Confirmed on Logitech G Cloud.
- **Fix for touch interrupt:** Detect when touch input begins (mouse event fires) while the mod has a controller-held item. When this happens, immediately return the item to its original inventory slot (or first open slot) before allowing touch to proceed. This prevents the orphaned state entirely.
- **Do NOT add a menu-exit safety net.** Dropping items on the ground is intentional game behavior that should remain possible. On console, there's a snap-to zone outside the left edge of the inventory window (inventory isn't fullscreen). On mobile touch, releasing an item when no slot is highlighted in green drops it.
- **Intentional item drop (controller):** Since there's no "outside the inventory window" zone on mobile fullscreen, need a dedicated controller input for intentional drops. Proposed: **Left stick click (L3) while holding an item drops it on the ground.** This is an unused button in menus and provides a deliberate, hard-to-accidentally-press action.
- Alternative considered: a snap-to "drop zone" component at the edge of the inventory grid. Less intuitive than a button press since every slot is already tightly packed on mobile.
- **Read `ANDROID_INVENTORY_NOTES.md` before working on this.**

### 12. Right Joystick Cursor Mode + Zoom Control
- Bundled feature (LARGE)
- **Cursor mode**: Right joystick moves free cursor in menus + gameplay
  - Essential for precise furniture placement, museum donations
  - On Switch: right stick moves cursor, disappears after inactivity
  - Press for left click
  - Implementation: Read right stick axis from `GamePad.GetState()`, call `Game1.setMousePosition()` per tick
  - No performance concern - just reading two floats and setting x/y
  - Complexity is in behavior: dead zones, acceleration curves, auto-hide, interaction with snap navigation
- **Zoom control**: Add to in-game Options page (not GMCM)
  - CONFIRMED: Zoom slider does NOT exist on Android - mobile port stripped it because zoom is pinch-to-zoom
  - On console: `whichOption = 18`, `OptionsSlider`, range 75%-200%, controls `Game1.options.desiredBaseZoomLevel`
  - Need to inject custom `OptionsSlider` subclass into `OptionsPage.options` list
  - Use `Display.MenuChanged` event to detect GameMenu open, insert into options list (no Harmony needed - GMCM proves this works)
  - Must subclass `OptionsSlider` with own value management since game's zoom handling may not be wired on Android
  - Note: GMCM's "Mod Options" button is partially cut off (1px visible) at bottom of Options page - scroll bounds don't account for injected elements. Our slider injection may need to fix scroll bounds too.

### 13. Chest Sidebar Navigation — FIXED in v2.9.8-v2.9.30
- Sidebar buttons all navigable and functional: Sort Chest, Fill Stacks, Sort Inventory, Trash, Close X
- Color toggle button opens/closes the DiscreteColorPicker via direct `.visible` toggle + "drumkit6" sound (v2.9.12)
- `receiveLeftClick` does NOT work for the color toggle on Android — must toggle `.visible` directly
- Color picker swatch navigation: DONE (see #13b)

### 13b. Color Picker Swatch Navigation — FIXED in v2.9.14-v2.9.30
- **All requirements implemented:**
  1. Inventory/sidebar navigation blocked while picker is open (all D-pad/stick intercepted in prefix)
  2. Cursor snaps to first swatch on picker open
  3. Swatches wired as 7x3 grid with correct neighbors, edge neighbors = -1 (no escape)
  4. B closes picker only (not the chest) — exitThisMenu patch blocks same-frame close
  5. Color toggle not navigable while picker open (neighbors set to -1)
  6. A on swatch selects color via `receiveLeftClick` at bounds.Center
- **Visual stride detection (v2.9.20-v2.9.25):** Probes picker's nearest-color hit-test at runtime to find exact visual stride (104x80 on test device). Relocates swatch bounds to visual grid positions so `snapCursorToCurrentSnappedComponent()` positions cursor correctly.
- **B-closes-whole-chest fix (v2.9.29):** Game's update loop has separate B-close path using pre-cached gamepad state. GetState suppression can't help. Fix: `exitThisMenu` prefix with same-tick guard.
- **Close X reopen fix (v2.9.28):** Replaced 120-tick hack with suppress-A-until-release at GetState level. Clean, no timing window.
- **Color preservation (v2.9.30):** Probe changes chest color as side effect. Fixed by clicking at saved color's grid position after probe. `menu.context` is NOT a Chest on Android.
- **File:** `Patches/ItemGrabMenuPatches.cs`, `Patches/GameplayButtonPatches.cs`

### 13c. Color Picker Cursor Position Slightly Off
- **Minimal priority — cosmetic annoyance only.** Functionality is correct (A selects the right color), but the visible cursor doesn't align perfectly with the swatch grid during navigation.
- Likely caused by the gap between the relocated component bounds (used for snap positioning) and the actual rendered swatch visuals. The probed stride values may not perfectly match the picker's internal rendering offsets.
- **Not blocking anything.** Swatches select correctly, navigation works, colors apply. Just visually imprecise.

### 14. Gift Log / Social Tab Cursor Fix
- Cursor doesn't follow when switching tabs with LB/RB
- **Confirmed persists on Logitech G Cloud** (Feb 4 test). No longer conflicting — issue is real and reproducible across devices.
- Symptoms: (1) Switch to social tab with LB/RB, cursor stays visually on inventory tab icon. (2) D-pad down DOES scroll villager list and A DOES open gift log, but no visual indicator of who's selected. (3) B from gift log puts cursor back on inventory tab visually. (4) Pressing right once from social tab skips straight to map tab.
- Log analysis: After tab switch, mod correctly stops intercepting (only fires on inventoryTab). All social tab input passes through to vanilla game handlers. The game receives input fine.
- Possible root cause: `GameMenu.changeTab()` doesn't call `snapToDefaultClickableComponent()` on the new page. Fix: Patch tab change to force snap.
- NOT a mod bug - vanilla Android controller issue
- Previous conflicting test (Feb 3, Odin Pro: "all other tabs work fine") may have been testing different conditions or a fluke.

---

## Lower Priority (Polish & Nice-to-Have)

### 15. Disable Touchscreen Option
- GMCM toggle to disable all touch/mouse input when using controller
- **Deprioritized:** Touchscreen provides useful fallback for logging/testing when controller can't reach something. Not needed for core gameplay loop.
- Off by default. Suppresses `MouseLeft`, `MouseRight`, and other mouse button events in `OnButtonsChanged` when enabled
- Touch on Android comes through as mouse events; controller uses `ControllerA`/`ControllerB`/etc. - already separate input paths
- Risk: some vanilla Android controller code may internally simulate mouse clicks (A-button-as-click). Verify suppressing mouse events doesn't break controller code paths.
- Simple implementation: ~10-20 lines in `OnButtonsChanged` + GMCM toggle in `ModConfig.cs`/`ModEntry.cs`

### 16. ~~Trash Can + Sort Button Fix~~ — IMPLEMENTED in v2.7.2+
- (a) A on trash can (slot 105) now trashes held item via `Utility.trashItem()` (handles refund + sound)
- (b) A on sort button (slot 106) now sorts inventory (cancels hold first if needed)
- (c) B while holding snaps to trash can; B again cancels hold and closes menu
- **REMAINING: Trash can lid animation does NOT work on Android.** See bug report below.
- Also found: **slot 12341** is reachable by navigating far left/up from inventory grid - likely a tab icon. A press on it is also blocked. May want a general "pass through to game for non-inventory slots" fallback instead of blocking.
- **BUG: Trash Can Lid Animation on Android**
  - On console/desktop, hovering over the trash can animates the lid open (rotation from 0 to PI/2).
  - On Android with controller snap navigation, the lid does NOT animate despite multiple approaches tried.
  - **What works:** Sound ("trashcanlid") plays. Trashing items works. Drag-and-drop (touch) DOES show the lid animation.
  - **What was tried and failed:**
    1. `performHoverAction()` with trash can center coords — game's own `performHoverAction` (using real mouse pos) fights it, net effect is lid stays closed
    2. `Game1.setMousePosition()` to trash can center every tick — breaks cursor display entirely, item displaced, no cursor visible
    3. Reflection: set `trashCanLidRotation` field directly in `OnUpdateTicked` — field IS set correctly (confirmed via logging: value reaches 1.5708/PI/2) but visual doesn't change
    4. Reflection: set field in Harmony PREFIX on `InventoryPage.draw()` — same result, field set to PI/2 right before draw, no visual change
    5. Draw lid sprite ourselves in Harmony POSTFIX on `InventoryPage.draw()` using exact same `SpriteBatch.Draw()` call as vanilla (same texture, source rect, position, rotation, origin, scale, layer depth) — no visual change, no error
  - **Key diagnostic finding:** Approach #4 confirmed via logging that `AccessTools.Field(typeof(InventoryPage), "trashCanLidRotation")` finds the field, reads/writes it successfully, value persists between frames at PI/2. But the rendered trash can lid doesn't rotate.
  - **Hypothesis:** The Android port may render InventoryPage through a different code path that either (a) re-draws the trash can area after our postfix at a higher layer, (b) uses a completely different draw method for the trash can on mobile, or (c) has a mobile-specific overlay that covers the lid area. The fact that touch-drag works but controller-snap doesn't suggests the animation path is tied to the touch/mouse input system at a rendering level, not just the field value.
  - **Next steps to try:** (a) Dump all draw calls in the frame to see if something draws over our lid. (b) Try drawing at layer depth 1.0f (maximum) to see if visibility is the issue. (c) Check if `GameMenu.draw()` on Android has a mobile-specific override that redraws inventory components. (d) Check if `trashCan.draw(b)` on Android draws both the base AND the lid (unlike desktop where they're separate). (e) Compare the actual `InventoryPage.draw()` IL on Android vs desktop decompilation.

### 17. Title/Main Menu Cursor Fix
- Cursor (hand icon) reportedly invisible on main menu with controller
- UNVERIFIED - Not reproducible on Odin Pro. Cursor shows up fine, menu works. May be device/controller-specific or outdated report.

### 18. Museum Donation Menu
- Controller-only placement is inaccessible
- **Confirmed inaccessible without touchscreen** on Logitech G Cloud. There is no way to select an item to donate using only controller inputs. Touch is required.
- This will likely require the Right Joystick Cursor Mode (#12) to fix properly, since museum donation uses free-placement on a grid rather than snap navigation.
- Alternative: Could potentially implement a snap-based item selection overlay, but the placement grid is freeform and doesn't map well to snap navigation.

### 19. Geode Breaking Menu
- Partially works but with no visual feedback
- **Tested on Logitech G Cloud.** Geode was highlighted in inventory, but did NOT visually move to the anvil. Pushing up on the joystick invisibly moved it to the anvil area, then pressing A cracked it open. Functional but very unintuitive — no visual feedback that anything is happening.
- **Fix approach:** Apply the same controller input handling used in inventory management. The geode menu (`GeodeMenu`) likely needs:
  - A-button to select geode from inventory
  - Visual feedback showing geode moving to anvil (or auto-place on anvil when selected)
  - A-button on anvil to crack
- Start by implementing inventory-style patches on `GeodeMenu`, then test and get logs.

### 20. Settings Menu Controller Navigation
- Options menu partially works via free cursor, not snap-based
- Feb 3 test: Options tab does NOT use snap navigation at all. Left joystick moves a free cursor anywhere on screen, right joystick scrolls the options list. This is different from all other tabs which use snap navigation.
- Can move cursor below visible area to reach the partially-hidden GMCM "Mod Options" button (1px visible at bottom)
- This means basic controller interaction IS possible (move cursor + A to click), but it's imprecise and unintuitive
- Full fix would be complex: inject snap navigation for sliders, checkboxes, dropdowns. Lower priority since free cursor works as workaround.

### 21. Additional Controller Testing
- Expand testing to other controllers and devices
- Current priority: Odin Pro built-in + Logitech G Cloud built-in + Xbox wireless (for swapped layout testing). Get everything working on these first.
- Note: Controller layout switching (Switch/Xbox in GMCM) untested on G Cloud — needs verification.
- Future: Build comprehensive testing checklist, grade every controller on pass/fail per feature. Possibly test across multiple Android devices.
- Controllers to test: 8BitDo, PS4/PS5 DualSense, others on hand

### 22. Analog Trigger Multi-Read (G Cloud / Analog Triggers)
- Analog triggers register multiple discrete presses
- **Symptom:** On Logitech G Cloud, analog triggers register multiple button presses at different pull distances. A full trigger pull moves the toolbar selection by 4 slots instead of 1. A slight pull moves by 1, pulling further adds another, etc.
- **Workaround:** Bumper Mode fixes this — when bumper mode is enabled, triggers work correctly as single presses (one pull = one slot regardless of distance).
- **Root cause:** The mod likely reads trigger input as a digital button press, but the G Cloud's analog triggers cross multiple thresholds as they're pulled. Each threshold crossing fires a separate button event.
- **Fix approach:** Add trigger debounce — after a trigger press is registered, ignore subsequent trigger events for a short window (~100-200ms or ~6-12 ticks). Alternatively, read the analog trigger value directly from `GamePad.GetState().Triggers.Left/Right` and only fire on the initial cross above a threshold (e.g., > 0.5), ignoring further changes until the trigger is fully released (< 0.1).
- This may also affect Xbox wireless controllers that currently "need Bumper Mode" — the bumper mode requirement might be masking the same analog trigger issue rather than a complete trigger failure.

---

## Feature Requests

### 23. Lock Inventory Slots
- Prevent specific inventory slots from being moved/sorted
- User request. Would need a way to mark slots as locked (maybe long-press or a modifier button), and sorting/transfer operations would skip locked slots.
- GMCM toggle to enable/disable the feature.

### 24. Save Inventory Layout Profiles
- Save and restore inventory arrangements
- User request. Would save the current inventory layout (which item in which slot) and allow restoring it later. Useful after sorting disrupts a preferred arrangement.
- Could pair with #23 (locked slots) — locked slots define the layout, profiles save/restore it.

---

## Needs Investigation (May Be Engine-Level)

### 25. Tool Charging Broken While Moving
- **Symptom:** Holding the tool button while the left joystick is pressed in any direction causes the tool to rapid-fire single uses instead of charging. The player stops moving and the tool just keeps firing until the joystick is released.
- **Expected behavior (console):** Holding the tool button while moving begins charging immediately. The player hops one square at a time while the charge builds. Releasing the button fires the charged tool.
- **Confirmed reproducible** on v2.8.12 and v2.9.1 testing. Affects all upgradeable tools (Axe, Pickaxe, Hoe, Watering Can) at all upgrade levels with charge mechanics (Steel+).
- **NOT mod-caused.** Earlier hypothesis blamed `GameplayButtonPatches` X/Y swap, but the issue occurs regardless of layout. This is an Android port difference — the port likely requires the player to be stationary before entering the "charging" state. When the movement stick is held, it falls back to repeated single-use.
- **Root cause hypothesis:** Android's tool-use code path checks for movement and prevents charge state entry. The "hop one square at a time" behavior on Switch suggests the engine has a specific "charging while moving" mode that Android may have stripped or never implemented.
- **Fix approach (v1):** Allow normal movement to continue while charging. Don't need the console hop-to-grid-center behavior for the first version.
- **Future enhancement:** Console-style hop movement (snap to map grid center between squares) while charging. Separate patch from the basic charging-while-moving fix.
- **Investigation needed:** Decompile the tool-use state machine. Look for where the game decides between "single use" and "start charging" — likely a check for player movement or `Farmer.isMoving()`. The fix may involve patching that check to allow charging regardless of movement state.
- **Testing plan:** For each tool (Axe, Pickaxe, Hoe, Watering Can), at each upgrade level (Copper, Steel, Gold, Iridium):
  - Test charged/hold use while stationary (hold tool button to charge, release)
  - Test charged/hold use while walking (hold direction + hold tool button)
  - Compare each result to Switch behavior
  - Note: Copper adds no charge mechanic, Steel = 1 charge level, Gold = 2, Iridium = 3

### 26. SMAPI / Mod Menu Button Position (G Cloud Title Screen)
- Probably not our bug
- On Logitech G Cloud, the SMAPI details and mod menu button on the title screen are positioned ~1/3 up the screen instead of in the corner. Cannot tap on them, and pressing A with the controller cursor does nothing.
- Installing StardewUI mod did not fix it.
- G Cloud resolution is 1920x1080 (16:9) — same aspect ratio as standard but higher resolution than Switch (720p). The positioning issue may be a SMAPI scaling/anchor bug at 1080p.
- **Recommendation:** Open a bug report with SMAPI or the relevant mod, not our responsibility to fix. Document here for reference only.
- Note: The in-game settings menu GMCM button (bottom of Options page) IS reachable via free cursor — this issue is title-screen-only.

---

## Optimization

Performance investigation (v2.7.14 session): Game starts at 60fps, degrades to ~53fps over a play session with periodic frame hitches. Root cause is a combination of per-tick overhead and log I/O pressure.

### O1. Cache Reflection in InventoryManagementPatches — FIXED in v2.7.15
- `ClearHoverState()` does 4 uncached `AccessTools.Field()` lookups per call — runs every tick while A is held
- `TriggerHoverTooltip()` does up to 5 uncached `AccessTools.Field()` lookups when slot changes — runs every tick while A is NOT held
- Every tick in the inventory menu does 4-5 reflection lookups at 60/sec
- **Fix:** Cache all fields as statics in `Apply()`, same pattern as ShopMenuPatches

### O2. Remove GamePad.GetState() from Draw Postfix
- `InventoryManagementPatches.InventoryPage_Draw_Postfix` (line 123) calls `GamePad.GetState()` every frame just to check if A is pressed
- This is a second gamepad query per frame (the first is in `OnUpdateTicked` at line 190). On Android, `GamePad.GetState()` is a JNI call through MonoGame
- **Fix:** Store the A-button state in a static bool from `OnUpdateTicked` and read it in the draw postfix instead of re-querying the gamepad
- **File:** `Patches/InventoryManagementPatches.cs`

### O3. Cache Reflection in HandleShopBumperQuantity — ALREADY DONE
- `HandleShopBumperQuantity` and `HandleShopQuantityNonBumper` call `ShopMenuPatches.AdjustQuantity()` which uses `QuantityToBuyField` and `InvVisibleField` — both cached at init in `ShopMenuPatches.Apply()` since v3.0.7-v3.0.9. No uncached reflection in these code paths.
- **Also cached in v3.1.3:** `FillOutStacks` method in `ItemGrabMenuPatches` (was uncached in `AddToExistingStacks`).

### O4. VerboseLogging I/O Pressure
- When VerboseLogging is enabled, every button press logs a line (`ModEntry.cs:245`), trigger values log per-tick when non-zero (`ModEntry.cs:202`), and inventory hold logging fires periodically
- Over a long session the SMAPI log file grows large (63k+ tokens observed). Periodic hitches likely correlate with log buffer flushes to disk
- **Not a code bug** — inherent to having VerboseLogging enabled. Turn off VerboseLogging in GMCM when playing for fun
- **Possible future improvement:** Add a less-verbose logging tier that only logs on state transitions (button pressed/released) rather than every tick, or batch log writes

---

## Not Needed / Working Fine / Intentionally Different
- ~~Last shipped display~~ - Works correctly
- ~~Non-shippable items~~ - Properly greyed out
- ~~Shipping bin stacking~~ - Matches console behavior (no stacking)
- ~~Crafting menu fixes~~ - No issues reported
- ~~Menu navigation fixes~~ - No issues reported
- ~~Menu tab navigation (ZL/ZR)~~ - Confirmed working on G Cloud (cycling through all tabs)
- ~~Console-style shop (hold-A to buy)~~ - Intentionally kept enhanced quantity selector instead. Our approach is better: select exact quantity with LT/RT before buying vs. hold-A and hope. Console has no undo for over-purchasing.
- ~~Dialogue text completion~~ - Not reproducible. Tested on Odin Pro and Logitech G Cloud — first A completes text, second A advances. Works correctly. Keep on radar for Bluetooth controllers only.
- ~~Hold-to-repeat tool actions~~ - Confirmed working on Logitech G Cloud. Holding Y to multi-swing tools (chop/mine/water) works correctly. Previously reported as broken — may have been fixed in a game update or was device-specific.
- ~~Slingshot controller fix~~ - NOT YET TESTED. Kept here as reminder — needs testing when slingshot is available (mines floor 40+). Previous reports of rapid-fire/freeze may or may not still apply.

---

## Tested Controllers

| Controller | Status | Notes |
|------------|--------|-------|
| Odin Built-in | Full | All buttons + triggers work |
| Logitech G Cloud Built-in | Full | Analog triggers need Bumper Mode (see #21). 1920x1080 @ 7", Android 11, Snapdragon 720G |
| Xbox One Wireless (Bluetooth) | Full | Triggers need Bumper Mode |
| Xbox Series X\|S Wireless (Bluetooth) | Full | Triggers need Bumper Mode |

See `CONTROLLER_MATRIX.md` for full testing details by device.
