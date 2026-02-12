# Agent Prompt: Donation Page Ingredient Navigation (#9c)

## Task
Analyze the SMAPI diagnostic logs from v3.2.34 to understand the ingredient component layout on the CC bundle donation page. Then implement navigation from inventory slots UP to ingredient list components and back DOWN, so the user can see what items a bundle needs.

## Context

### What exists
`Patches/JunimoNoteMenuPatches.cs` has working controller support for:
- **Overview page** (#9b, v3.2.30-v3.2.33): Populates `allClickableComponents` from `bundles` field, wires neighbor IDs spatially, handles thumbstick/A navigation.
- **Donation page** (#9a, v3.2.26-v3.2.29): Manages a tracked cursor over inventory slots (6 columns, `_trackedSlotIndex`). A-press uses GetMouseState override for `receiveLeftClick`. Custom cursor drawn in `Draw_Postfix`.

### What needs to happen for #9c
Extend donation page navigation so pressing **Up from the top inventory row** moves the cursor to the ingredient list area, and pressing **Down from the ingredient list** returns to inventory. A on an ingredient component should also work (GetMouseState override + receiveLeftClick, same pattern).

### Prior diagnostic data (from v3.2.25 at zoomLevel=1.2972656)
- **Inventory:** 36 slots (IDs 0-35), 6 columns, bounds start (132,116), each 104x96
- **Ingredient list:** IDs 1000-1004, name='ingredient_list_slot', Y:301-377 (3+2 layout)
- **Ingredient slots:** all id=-500, all neighbors=-1, Y:473-549 (same 3+2 layout)
- **Back button:** id=-500, bounds (28,737,80,76)

### Your job: Analyze v3.2.34 logs

1. Read the SMAPI log at `bin/Release/net6.0/SMAPI-latest.txt`
2. Search for lines containing `DONATION PAGE` to find the component dumps
3. For each bundle the user tested, extract:
   - `ingredientList` component positions, IDs, names, bounds
   - `ingredientSlots` component positions, IDs, names, bounds
   - Inventory slot 0 and slot 5 positions (for Y-reference)
   - Back button and purchase button positions
   - Zoom level
4. Present a summary table showing:
   - How ingredient list components are arranged spatially
   - How many ingredients each tested bundle has
   - The Y gap between inventory top row and ingredient list bottom row
   - Whether `ingredientList` or `ingredientSlots` is the right target for navigation (which one has useful IDs? which one responds to clicks?)

### Key questions to answer
- Are the `ingredientList` components (IDs 1000+) the correct navigation targets, or are `ingredientSlots` (id=-500) better?
- What's the spatial layout of ingredients — single row, two rows, variable?
- What Y coordinate separates inventory from ingredients? (needed for the "up from top row" trigger)
- Do ingredient components have useful `hoverText` for tooltips?
- Is there a `purchaseButton` (the "submit/donate" button)?

### Files to read
1. `bin/Release/net6.0/SMAPI-latest.txt` — the diagnostic logs (search for `DONATION PAGE`)
2. `Patches/JunimoNoteMenuPatches.cs` — the current implementation (for context)

### Output format
Present your findings as a structured analysis with:
1. Raw component data for each tested bundle
2. Spatial layout diagram (ASCII art showing relative positions)
3. Answers to the key questions above
4. Recommended approach for implementing navigation (which components to target, how to wire up/down between zones)

### What NOT to do
- Don't modify any code — this is analysis only
- Don't guess at data — use only what's in the logs
- If the logs haven't been dropped yet, say so clearly
