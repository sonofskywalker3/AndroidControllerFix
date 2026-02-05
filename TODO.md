# AndroidConsolizer - TODO List

**Read this file when working on any bug fix or feature.** Each item has detailed implementation notes, root cause analysis, and file references.

Items marked with RE-IMPLEMENT were working in v2.9.0 but lost when reverting to v2.7.0 due to the v2.7.10 regression. See `POSTMORTEM.md` for details.

---

## Features to Re-implement (Lost in v2.7.10-v2.9.2 Revert)

These need to be re-implemented **one at a time, one per 0.0.1 patch, each committed separately.**

### From v2.9.0 — Must Re-implement
1. **Robin's Building Menu Fix** — CarpenterMenu instantly closes when opened with A button
   - **What it did:** Prefix patch on `CarpenterMenu.receiveLeftClick` with 15-tick grace period after menu opens. Toggleable via `EnableCarpenterMenuFix`.
   - **File needed:** New `Patches/CarpenterMenuPatches.cs`
   - **Config:** Add `EnableCarpenterMenuFix` to ModConfig.cs
   - **Implementation notes:** Track `Game1.ticks` at menu open, block `receiveLeftClick` for 15 ticks. Simple prefix patch. Add GMCM toggle.
   - **This is a standalone fix — touches CarpenterMenuPatches.cs, ModConfig.cs, ModEntry.cs only.**

2. **Shop Purchase Flow Fix (CRITICAL)** — Purchases don't call `actionWhenPurchased()`, don't consume trade items
   - **What it did:** Purchase logic now calls `actionWhenPurchased(shopId)`, checks/consumes trade items (`TradeItem`/`TradeItemCount`), and handles inventory-full refunds.
   - **File:** `Patches/ShopMenuPatches.cs`
   - **Implementation notes:** After charging player and adding item, must: (1) call `actionWhenPurchased(shopId)` on the ISalable, (2) check `TradeItem`/`TradeItemCount` in ItemStockInformation and deduct from player inventory, (3) handle tool upgrades (toolUpgradeForSale flow), (4) on inventory full, refund money AND trade items.
   - **This only touches ShopMenuPatches.cs.**

3. ~~**Fishing Mini-Game Button Fix**~~ — **DONE in v2.7.1**
   - Fixed in `GameplayButtonPatches.cs`: X/Y swap now applies when `BobberBar` is active.

4. **Shop Quantity Enhancement** — LB/RB adjusts quantity by +/-10, hold-to-repeat
   - **What it did:** Non-bumper mode: LB/RB = +/-10. Hold-to-repeat with 333ms initial delay then 50ms repeat. Quantity limits respect stock, money, trade items, and stack size.
   - **Files:** `ModEntry.cs` (initial press in OnButtonsChanged), `Patches/ShopMenuPatches.cs` (auto-repeat in Update_Postfix)
   - **This touches ModEntry.cs and ShopMenuPatches.cs.**

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

### 2. Shop Purchase Flow Bug (CRITICAL)
RE-IMPLEMENT (was in v2.9.0, lost in revert)
- Purchase logic needs to call `actionWhenPurchased(shopId)`, check/consume trade items (`TradeItem`/`TradeItemCount`), and handle inventory-full refunds
- Tool upgrades, recipes, and all special purchase behaviors need to work correctly
- **Previous implementation:** Used Option B in ShopMenuPatches.cs — purchase logic calls `actionWhenPurchased(shopId)`, checks/consumes trade items, handles inventory-full refunds.
- **NEW BUG — Sell screen also triggers buy:** `ReceiveGamePadButton_Postfix` fires on ALL A presses in ShopMenu, including when the player is on the sell/inventory tab. The postfix reads `hoveredItem` which still points to the last buy-list item, so pressing A to sell an inventory item ALSO purchases from the buy list. The postfix must check if the shop is in buy mode before executing purchase logic.
- **Log evidence (v2.7.2):** User pressed Y (sell mode) at 21:25:01 in Pierre's shop, navigated to inventory item, pressed A at 21:25:06 → postfix fired "Purchase complete! Bought 1x Parsnip Seeds" using stale hoveredItem from the buy list. `currentlySnappedComponent: 13, name=13` (an inventory slot, not a forSale button).
- **Fix:** At minimum, add an early-exit check: if the snapped component is NOT a forSale button (myID != forSaleButtons[n].myID), don't attempt a purchase. Better: check if the shop is on the buy tab vs sell tab. Consider Option B approach (simulate receiveLeftClick at snapped component coords) which would naturally respect which tab is active.

### 3. Fishing Mini-Game Button Mismatch — DONE (v2.7.1)
- Fixed: `GameplayButtonPatches.GetState_Postfix` now applies X/Y swap when `BobberBar` is active.

### 4. Shop Quantity Increment Enhancement
RE-IMPLEMENT (was in v2.9.0, lost in revert)
- Non-bumper mode: LB/RB = +/-10, bumper mode: LB/RB = +/-1
- Hold-to-repeat with 333ms delay then 50ms repeat
- Quantity limits respect stock, money, trade items, and stack size
- **Previous implementation:** Initial press in `ModEntry.OnButtonsChanged`, auto-repeat in `ShopMenuPatches.Update_Postfix`

### 5. Cutscene Skip with Controller
- **Desired behavior:** Press Start once to show the skip button, press Start again within 3 seconds to confirm skip. Double-press-to-skip prevents accidental skips.
- On Android touchscreen, the skip button appears on screen and you tap it. With a controller, there's no way to activate the skip button.
- **Log evidence (v2.7.2):** User entered Town at 21:22:44, cutscene triggered. Pressed ControllerStart 4 times (21:22:50-21:22:52) — no effect. Then tapped screen (MouseLeft x2 at 21:22:53-21:22:54) to skip. Cutscene ended at 21:22:55 ("Warping to Town").
- **Mechanism:** Touchscreen skip uses `MouseLeft` (two taps — first shows skip button, second confirms). The cutscene is an `Event` object. The skip button is likely `Event.skippable` + a clickable component. On Android, `Event.receiveLeftClick` or similar handles the skip confirmation.
- **Implementation approach:** When a skippable event is active and Start is pressed, simulate the skip button click. First press: call whatever shows the skip button. Second press within 3 seconds: call whatever confirms the skip. Need to find the exact method — likely `Event.skipEvent()` or `Event.receiveLeftClick()` at the skip button coordinates.
- **Next step:** Decompile or inspect `Event` class to find the skip mechanism. Look for `skippable`, `skipEvent`, `skipped` fields/methods.

### 5b. Shop Inventory Tab Broken with Controller
- **Symptom:** Tapping the "inventory" button in the shop interface (to switch to sell mode via touch) doesn't draw the controller cursor or let you select inventory items the way pressing Y does.
- **Log evidence (v2.7.2):** MouseLeft events at 21:25:14 and 21:25:22 in Pierre's ShopMenu — user tapped the inventory/sell tab button on the touchscreen. No mod log output suggests the tap was handled by the game but the controller snap navigation didn't update to the inventory grid.
- **Root cause:** The shop has two modes — buy (forSale list) and sell (player inventory). Pressing Y switches modes correctly and snap navigation works in sell mode. But tapping the inventory tab button via touchscreen doesn't trigger whatever Y does to set up snap navigation for the inventory grid.
- **Fix:** Detect when the shop switches to inventory/sell mode (regardless of whether it was triggered by Y or touch) and ensure snap navigation is properly initialized. May need to patch the tab button click handler or detect the mode change in `Update_Postfix`.

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

### 10. Trash Can / Sort Unreachable in Item Grab Menus
- Can't navigate to trash or sort in chest/fishing contexts
- **Symptom:** When in an `ItemGrabMenu` (chests, fishing treasure, caught-fish-with-full-inventory), cannot navigate the cursor to the trash can or sort button. Only inventory slots are reachable.
- Confirmed on Logitech G Cloud.
- This is separate from the inventory page trash can fix (#15 implemented) — that fix is on `InventoryPage`, not `ItemGrabMenu`.
- **Root cause:** Same as #13 (Chest Color Selection) — `ItemGrabMenuPatches.cs` has no snap navigation modifications. The trash can and sort button exist as clickable components but their neighborIDs aren't chained to the inventory grid in the item grab context.
- Fix should be bundled with #13 when modifying ItemGrabMenu snap navigation.

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

### 13. Chest Color Selection
- Far-right chest options (color, auto-sort) unreachable with D-pad/joystick
- Confirmed on Odin Pro and Logitech G Cloud. Mod already works around sort (X) and add-to-stacks (Y), but color picker has NO workaround - completely inaccessible
- G Cloud testing: Can't move cursor to any buttons, only inventory slots.
- Root cause: ItemGrabMenuPatches.cs has ZERO snap navigation modifications. The buttons exist as clickable components but their neighborIDs aren't chained to the inventory grid
- ShippingBinPatches already proves neighbor ID modification works (creates custom drop zone with snap chains)
- Fix approach: Postfix patch on ItemGrabMenu that modifies rightNeighborID/leftNeighborID on relevant components after menu populates
  - Step 1: Add logging to dump all `allClickableComponents` (myID, bounds, name) to identify button IDs
  - Step 2: Chain rightNeighborID from rightmost inventory slot -> organize button -> color picker -> fill stacks
  - Step 3: Chain leftNeighborID back from those buttons -> inventory
- Precedent: ShippingBinPatches.cs already does this exact pattern
- **Bundle with #10** — both require ItemGrabMenu snap navigation overhaul

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

### 25. Upgraded Tool Use While Moving
- Must stop completely to use upgraded tools
- On Switch/PC, tools can be used while walking
- May be engine-level Android difference
- UNVERIFIED on Odin Pro - no upgraded tools yet in current playthrough
- **Testing plan:** For each tool (Axe, Pickaxe, Hoe, Watering Can), at each upgrade level (Copper, Steel, Gold, Iridium):
  - Test basic use while stationary (Y button)
  - Test basic use while walking (hold direction + Y)
  - Test charged/hold use while stationary (hold Y to charge, release)
  - Test charged/hold use while walking (hold direction + hold Y)
  - Compare each result to Switch behavior (same tool, same upgrade, same action)
  - Note: Copper adds no charge mechanic, Steel = 1 charge level, Gold = 2, Iridium = 3
  - Record: Does the action fire? Does the player stop moving? Is there a delay? Does charge level work correctly?

### 26. SMAPI / Mod Menu Button Position (G Cloud Title Screen)
- Probably not our bug
- On Logitech G Cloud, the SMAPI details and mod menu button on the title screen are positioned ~1/3 up the screen instead of in the corner. Cannot tap on them, and pressing A with the controller cursor does nothing.
- Installing StardewUI mod did not fix it.
- G Cloud resolution is 1920x1080 (16:9) — same aspect ratio as standard but higher resolution than Switch (720p). The positioning issue may be a SMAPI scaling/anchor bug at 1080p.
- **Recommendation:** Open a bug report with SMAPI or the relevant mod, not our responsibility to fix. Document here for reference only.
- Note: The in-game settings menu GMCM button (bottom of Options page) IS reachable via free cursor — this issue is title-screen-only.

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
