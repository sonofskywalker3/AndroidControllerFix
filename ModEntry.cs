using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod configuration.</summary>
        internal static ModConfig Config;

        /// <summary>The mod's helper instance for global access.</summary>
        internal static IModHelper ModHelper;

        /// <summary>The mod's monitor instance for global access.</summary>
        internal static IMonitor ModMonitor;

        /// <summary>The current toolbar row (0, 1, or 2) that the player should be locked to.</summary>
        private int currentToolbarRow = 0;

        /// <summary>Track the last known CurrentToolIndex to detect external changes.</summary>
        private int lastToolIndex = -1;

        /// <summary>Track trigger states for edge detection.</summary>
        private bool wasLeftTriggerDown = false;
        private bool wasRightTriggerDown = false;

        /// <summary>Threshold for trigger activation (0.0 to 1.0).</summary>
        private const float TriggerThreshold = 0.5f;

        /// <summary>Tick when Start was first pressed during a skippable event (for double-press skip).</summary>
        private int cutsceneSkipFirstPressTick = -1;

        /// <summary>Whether the skip confirmation is pending (waiting for second press).</summary>
        private bool cutsceneSkipPending = false;

        // Cached reflection for cutscene skip
        private static FieldInfo EventSkippableField;
        private static FieldInfo EventSkippedField;
        private static MethodInfo EventSkipMethod;

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            ModHelper = helper;
            ModMonitor = this.Monitor;
            Config = helper.ReadConfig<ModConfig>();

            // Apply Harmony patches
            var harmony = new Harmony(this.ModManifest.UniqueID);
            Patches.ShopMenuPatches.Apply(harmony, this.Monitor);
            Patches.ItemGrabMenuPatches.Apply(harmony, this.Monitor);
            Patches.InventoryPagePatches.Apply(harmony, this.Monitor);
            Patches.FarmerPatches.Apply(harmony, this.Monitor);
            Patches.ToolbarPatches.Apply(harmony, this.Monitor);
            Patches.ShippingBinPatches.Apply(harmony, this.Monitor);
            Patches.GameplayButtonPatches.Apply(harmony, this.Monitor);
            Patches.FishingRodPatches.Apply(harmony, this.Monitor);
            Patches.InventoryManagementPatches.Apply(harmony, this.Monitor);
            Patches.CarpenterMenuPatches.Apply(harmony, this.Monitor);

            // Register events
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;

            // Cache reflection for cutscene skip
            EventSkippableField = AccessTools.Field(typeof(Event), "skippable");
            EventSkippedField = AccessTools.Field(typeof(Event), "skipped");
            EventSkipMethod = AccessTools.Method(typeof(Event), "skipEvent");

            this.Monitor.Log("Android Consolizer loaded successfully.", LogLevel.Info);
        }

        /*********
        ** Private methods
        *********/
        /// <summary>Raised after the game is launched.</summary>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Register with Generic Mod Config Menu if available
            this.RegisterConfigMenu();
        }

        /// <summary>Raised after a save is loaded. Forces tool re-equip to fix first-use issue.</summary>
        private void OnSaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            // Fix: On load, the tool at index 0 isn't fully "equipped" until you cycle through.
            // Force a re-equip by cycling the tool index.
            var player = Game1.player;
            if (player != null)
            {
                int original = player.CurrentToolIndex;
                // Cycle to a different slot and back to force equip
                player.CurrentToolIndex = original == 0 ? 1 : 0;
                player.CurrentToolIndex = original;

                // Also reset our toolbar row tracking
                currentToolbarRow = original / 12;
                lastToolIndex = original;

                this.Monitor.Log($"Save loaded - forced tool re-equip at index {original}, row {currentToolbarRow}", LogLevel.Trace);
            }
        }

        /// <summary>Raised when a menu is opened or closed.</summary>
        private void OnMenuChanged(object sender, StardewModdingAPI.Events.MenuChangedEventArgs e)
        {
            // Clean up inventory management state when leaving inventory
            if (e.OldMenu is GameMenu oldGameMenu)
            {
                Patches.InventoryManagementPatches.OnMenuClosed();
                Patches.FishingRodPatches.ClearSelection();
            }

            // Fix snap navigation in ItemGrabMenu (chests, fishing treasure, etc.)
            if (Config.EnableConsoleChests && e.NewMenu is ItemGrabMenu itemGrabMenu)
            {
                Patches.ItemGrabMenuPatches.FixSnapNavigation(itemGrabMenu);
            }

            // Track CarpenterMenu open/close for grace period fix
            if (e.NewMenu is CarpenterMenu)
            {
                Patches.CarpenterMenuPatches.OnMenuOpened();
            }
            if (e.OldMenu is CarpenterMenu)
            {
                Patches.CarpenterMenuPatches.OnMenuClosed();
            }
        }

        /// <summary>Raised every game tick. Used to enforce toolbar row locking and handle triggers.</summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // X/Y swap is now handled by GameplayButtonPatches at the GamePad.GetState level

            // Update inventory management (maintain held item visual) when in inventory menu
            if (Config.EnableConsoleInventory && Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
            {
                Patches.InventoryManagementPatches.OnUpdateTicked();
            }

            // Only enforce toolbar fix during gameplay
            if (!Config.EnableConsoleToolbar || Game1.activeClickableMenu != null || !Context.IsPlayerFree)
            {
                // Reset trigger states when not in gameplay
                wasLeftTriggerDown = false;
                wasRightTriggerDown = false;
                return;
            }

            var player = Game1.player;
            if (player == null) return;

            int maxItems = player.MaxItems;
            int maxRows = maxItems / 12;

            // Clamp currentToolbarRow to valid range
            if (currentToolbarRow >= maxRows)
                currentToolbarRow = maxRows - 1;
            if (currentToolbarRow < 0)
                currentToolbarRow = 0;

            // Share row state with the Farmer setter patch
            Patches.FarmerPatches.CurrentToolbarRow = currentToolbarRow;

            int expectedRowStart = currentToolbarRow * 12;
            int expectedRowEnd = expectedRowStart + 11;

            // Handle triggers directly via GamePadState
            HandleTriggersDirectly(player, expectedRowStart);

            // Force CurrentToolIndex to stay in the current row
            int currentIndex = player.CurrentToolIndex;
            if (currentIndex < expectedRowStart || currentIndex > expectedRowEnd)
            {
                // Keep the position within row (0-11), but force to current row
                int positionInRow = currentIndex % 12;
                int correctedIndex = expectedRowStart + positionInRow;

                if (Config.VerboseLogging && currentIndex != lastToolIndex)
                {
                    this.Monitor.Log($"Row lock: Index {currentIndex} outside row {currentToolbarRow} ({expectedRowStart}-{expectedRowEnd}), correcting to {correctedIndex}", LogLevel.Debug);
                }

                player.CurrentToolIndex = correctedIndex;
                currentIndex = correctedIndex;
            }

            lastToolIndex = currentIndex;
        }

        /// <summary>Handle trigger input directly via GamePadState for slot navigation.</summary>
        private void HandleTriggersDirectly(Farmer player, int rowStart)
        {
            // Skip if using D-pad for toolbar navigation
            if (Config.UseBumpersInsteadOfTriggers)
                return;

            GamePadState gpState = GamePad.GetState(PlayerIndex.One);

            float leftTrigger = gpState.Triggers.Left;
            float rightTrigger = gpState.Triggers.Right;

            // Debug: Log trigger values when they're non-zero
            if (Config.VerboseLogging && (leftTrigger > 0.01f || rightTrigger > 0.01f))
            {
                this.Monitor.Log($"Triggers raw: LT={leftTrigger:F2}, RT={rightTrigger:F2}", LogLevel.Debug);
            }

            bool isLeftTriggerDown = leftTrigger > TriggerThreshold;
            bool isRightTriggerDown = rightTrigger > TriggerThreshold;

            int currentIndex = player.CurrentToolIndex;
            int positionInRow = currentIndex % 12;

            // Left trigger - move left (on press edge)
            if (isLeftTriggerDown && !wasLeftTriggerDown)
            {
                int newPosition = positionInRow - 1;
                if (newPosition < 0) newPosition = 11; // Wrap to end of row
                int newIndex = rowStart + newPosition;
                player.CurrentToolIndex = newIndex;
                Game1.playSound("shwip");
                if (Config.VerboseLogging)
                    this.Monitor.Log($"LT (direct): Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
            }

            // Right trigger - move right (on press edge)
            if (isRightTriggerDown && !wasRightTriggerDown)
            {
                int newPosition = positionInRow + 1;
                if (newPosition > 11) newPosition = 0; // Wrap to start of row
                int newIndex = rowStart + newPosition;
                player.CurrentToolIndex = newIndex;
                Game1.playSound("shwip");
                if (Config.VerboseLogging)
                    this.Monitor.Log($"RT (direct): Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
            }

            wasLeftTriggerDown = isLeftTriggerDown;
            wasRightTriggerDown = isRightTriggerDown;
        }

        /// <summary>Raised when buttons are pressed or released.</summary>
        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            // Log all button changes for debugging
            if (Config.VerboseLogging && e.Pressed.Any())
            {
                this.Monitor.Log($"Buttons pressed: {string.Join(", ", e.Pressed)}", LogLevel.Debug);
            }

            // Handle inventory menu buttons (sort, bait/tackle)
            if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
            {
                // Note: Y button hold-repeat is handled via polling in InventoryManagementPatches.OnUpdateTicked

                foreach (var button in e.Pressed)
                {
                    // CRITICAL: Always suppress raw ControllerX in inventory to prevent Android deletion bug
                    if (button == SButton.ControllerX && Config.EnableConsoleChests)
                    {
                        this.Monitor.Log($"Suppressing raw X button in inventory to prevent deletion", LogLevel.Debug);
                        this.Helper.Input.Suppress(button);
                    }

                    SButton remapped = ButtonRemapper.Remap(button);

                    // A button (after remapping) = Console-style inventory management
                    if (remapped == SButton.ControllerA)
                    {
                        // Try console-style inventory management first
                        if (Config.EnableConsoleInventory)
                        {
                            if (Patches.InventoryManagementPatches.HandleAButton(gameMenu, this.Monitor))
                            {
                                this.Helper.Input.Suppress(button);
                                continue; // Handled by inventory management
                            }
                        }

                        // Fall back to fishing rod bait tracking
                        if (Config.EnableConsoleInventory)
                        {
                            Patches.FishingRodPatches.OnAButtonPressed(gameMenu, this.Monitor);
                            // Don't suppress - let normal A behavior continue
                        }
                    }

                    // Y button (after remapping) = Fishing rod bait/tackle management
                    // Note: Single-stack pickup is handled via polling in InventoryManagementPatches.OnUpdateTicked
                    if (remapped == SButton.ControllerY)
                    {
                        // Try fishing rod bait/tackle management first
                        if (Config.EnableConsoleInventory)
                        {
                            if (Patches.FishingRodPatches.TryHandleBaitTackle(gameMenu, this.Helper, this.Monitor))
                            {
                                this.Helper.Input.Suppress(button);
                                continue; // Handled by fishing rod
                            }
                        }
                        // Single-stack pickup is handled via polling in OnUpdateTicked
                    }

                    // X button (after remapping) = Sort inventory
                    if (remapped == SButton.ControllerX && Config.EnableConsoleChests)
                    {
                        this.Monitor.Log($"Intercepting {button} (remapped to X) in inventory - sorting", LogLevel.Debug);
                        this.Helper.Input.Suppress(button);
                        Patches.InventoryPagePatches.SortPlayerInventory();
                    }
                }
            }

            // Shop quantity adjustment with bumpers (when triggers don't work)
            if (Config.UseBumpersInsteadOfTriggers && Game1.activeClickableMenu is ShopMenu shopMenu)
            {
                HandleShopBumperQuantity(e, shopMenu);
            }

            // Shop quantity adjustment in non-bumper mode: LB/RB = +/-10
            if (!Config.UseBumpersInsteadOfTriggers && Game1.activeClickableMenu is ShopMenu shopMenuNonBumper)
            {
                HandleShopQuantityNonBumper(e, shopMenuNonBumper);
            }

            // Cutscene skip - Start button skips events (double-press to confirm)
            if (Config.EnableCutsceneSkip && Game1.CurrentEvent != null)
            {
                if (e.Pressed.Contains(SButton.ControllerStart))
                {
                    this.Helper.Input.Suppress(SButton.ControllerStart);
                    HandleCutsceneSkip();
                }
            }

            // Journal button - Start opens Quest Log during gameplay
            if (Config.EnableJournalButton && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                if (e.Pressed.Contains(SButton.ControllerStart))
                {
                    this.Helper.Input.Suppress(SButton.ControllerStart);
                    OpenQuestLog();
                    if (Config.VerboseLogging)
                        this.Monitor.Log("Start button: Opening Quest Log", LogLevel.Debug);
                }
            }

            // Toolbar navigation fix - only when NOT in a menu (during gameplay)
            if (Config.EnableConsoleToolbar && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                HandleToolbarNavigation(e);
            }
        }

        /// <summary>Opens the Quest Log menu, handling Android's different constructor signature.</summary>
        private void OpenQuestLog()
        {
            try
            {
                // Android QuestLog requires an int parameter (page index), PC version has parameterless constructor
                // Use reflection to handle both cases
                var questLogType = typeof(QuestLog);
                var ctorWithInt = questLogType.GetConstructor(new[] { typeof(int) });

                if (ctorWithInt != null)
                {
                    Game1.activeClickableMenu = (IClickableMenu)ctorWithInt.Invoke(new object[] { 0 });
                }
                else
                {
                    Game1.activeClickableMenu = (IClickableMenu)Activator.CreateInstance(questLogType);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to open Quest Log: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Handle cutscene skip with Start button (double-press to confirm).</summary>
        private void HandleCutsceneSkip()
        {
            try
            {
                var currentEvent = Game1.CurrentEvent;
                if (currentEvent == null)
                    return;

                // Check if the event is skippable
                if (EventSkippableField == null)
                    return;

                bool isSkippable = (bool)EventSkippableField.GetValue(currentEvent);
                if (!isSkippable)
                {
                    if (Config.VerboseLogging)
                        this.Monitor.Log("Current event is not skippable", LogLevel.Debug);
                    return;
                }

                int currentTick = Game1.ticks;
                int ticksSinceFirstPress = currentTick - cutsceneSkipFirstPressTick;
                const int skipWindowTicks = 180; // 3 seconds at 60fps

                if (cutsceneSkipPending && ticksSinceFirstPress <= skipWindowTicks)
                {
                    // Second press within window - skip the event
                    EventSkippedField?.SetValue(currentEvent, true);
                    EventSkipMethod?.Invoke(currentEvent, null);

                    this.Monitor.Log("Cutscene skipped (Start pressed twice)", LogLevel.Info);
                    cutsceneSkipPending = false;
                    cutsceneSkipFirstPressTick = -1;
                }
                else
                {
                    // First press - show skip prompt and start timer
                    cutsceneSkipFirstPressTick = currentTick;
                    cutsceneSkipPending = true;

                    // Show HUD message to indicate skip is pending
                    Game1.addHUDMessage(new HUDMessage("Press Start again to skip", HUDMessage.newQuest_type) { noIcon = true });

                    if (Config.VerboseLogging)
                        this.Monitor.Log("Cutscene skip pending - press Start again within 3 seconds to confirm", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error in cutscene skip: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Handle shop quantity adjustment with bumpers when triggers aren't available.</summary>
        private void HandleShopBumperQuantity(ButtonsChangedEventArgs e, ShopMenu shopMenu)
        {
            // In bumper mode: LB/RB = +/-1
            // LB - Decrease quantity by 1
            if (e.Pressed.Contains(SButton.LeftShoulder))
            {
                this.Helper.Input.Suppress(SButton.LeftShoulder);
                Patches.ShopMenuPatches.AdjustQuantity(shopMenu, -1);
                Patches.ShopMenuPatches.StartLBHold();
            }

            // RB - Increase quantity by 1
            if (e.Pressed.Contains(SButton.RightShoulder))
            {
                this.Helper.Input.Suppress(SButton.RightShoulder);
                Patches.ShopMenuPatches.AdjustQuantity(shopMenu, 1);
                Patches.ShopMenuPatches.StartRBHold();
            }
        }

        /// <summary>Handle shop quantity adjustment in non-bumper mode: LB/RB = +/-10.</summary>
        private void HandleShopQuantityNonBumper(ButtonsChangedEventArgs e, ShopMenu shopMenu)
        {
            // In non-bumper mode: LB/RB = +/-10
            // LB - Decrease quantity by 10
            if (e.Pressed.Contains(SButton.LeftShoulder))
            {
                this.Helper.Input.Suppress(SButton.LeftShoulder);
                Patches.ShopMenuPatches.AdjustQuantity(shopMenu, -10);
                Patches.ShopMenuPatches.StartLBHold();
            }

            // RB - Increase quantity by 10
            if (e.Pressed.Contains(SButton.RightShoulder))
            {
                this.Helper.Input.Suppress(SButton.RightShoulder);
                Patches.ShopMenuPatches.AdjustQuantity(shopMenu, 10);
                Patches.ShopMenuPatches.StartRBHold();
            }
        }

        /// <summary>Handle toolbar navigation with LB/RB for row switching and LT/RT for slot movement.</summary>
        private void HandleToolbarNavigation(ButtonsChangedEventArgs e)
        {
            var player = Game1.player;
            if (player == null) return;

            // Debug: Log D-pad mode status when D-pad is pressed
            if (Config.VerboseLogging && (e.Pressed.Contains(SButton.DPadUp) || e.Pressed.Contains(SButton.DPadDown) ||
                e.Pressed.Contains(SButton.DPadLeft) || e.Pressed.Contains(SButton.DPadRight)))
            {
                this.Monitor.Log($"D-Pad pressed. UseBumpersInsteadOfTriggers = {Config.UseBumpersInsteadOfTriggers}", LogLevel.Debug);
            }

            int currentIndex = player.CurrentToolIndex;
            int maxItems = player.MaxItems;
            int maxRows = maxItems / 12;
            int currentRow = currentIndex / 12;
            int positionInRow = currentIndex % 12;

            // Check if using D-pad for toolbar navigation
            if (Config.UseBumpersInsteadOfTriggers)
            {
                // D-pad Up - Previous row
                if (e.Pressed.Contains(SButton.DPadUp))
                {
                    this.Helper.Input.Suppress(SButton.DPadUp);
                    int newRow = currentToolbarRow - 1;
                    if (newRow < 0) newRow = maxRows - 1;
                    currentToolbarRow = newRow;
                    int newIndex = (newRow * 12) + positionInRow;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"DPad Up: Row {currentRow} -> {newRow}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // D-pad Down - Next row
                if (e.Pressed.Contains(SButton.DPadDown))
                {
                    this.Helper.Input.Suppress(SButton.DPadDown);
                    int newRow = currentToolbarRow + 1;
                    if (newRow >= maxRows) newRow = 0;
                    currentToolbarRow = newRow;
                    int newIndex = (newRow * 12) + positionInRow;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"DPad Down: Row {currentRow} -> {newRow}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // LB / D-pad Left - Move left in current row
                if (e.Pressed.Contains(SButton.LeftShoulder) || e.Pressed.Contains(SButton.DPadLeft))
                {
                    if (e.Pressed.Contains(SButton.LeftShoulder))
                        this.Helper.Input.Suppress(SButton.LeftShoulder);
                    if (e.Pressed.Contains(SButton.DPadLeft))
                        this.Helper.Input.Suppress(SButton.DPadLeft);
                    int newPosition = positionInRow - 1;
                    if (newPosition < 0) newPosition = 11;
                    int newIndex = (currentRow * 12) + newPosition;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"Left: Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // RB / D-pad Right - Move right in current row
                if (e.Pressed.Contains(SButton.RightShoulder) || e.Pressed.Contains(SButton.DPadRight))
                {
                    if (e.Pressed.Contains(SButton.RightShoulder))
                        this.Helper.Input.Suppress(SButton.RightShoulder);
                    if (e.Pressed.Contains(SButton.DPadRight))
                        this.Helper.Input.Suppress(SButton.DPadRight);
                    int newPosition = positionInRow + 1;
                    if (newPosition > 11) newPosition = 0;
                    int newIndex = (currentRow * 12) + newPosition;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"Right: Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }
            }
            else
            {
                // Standard navigation: LB/RB for rows, LT/RT for slots
                // LB - Previous row
                if (e.Pressed.Contains(SButton.LeftShoulder))
                {
                    this.Helper.Input.Suppress(SButton.LeftShoulder);
                    int newRow = currentToolbarRow - 1;
                    if (newRow < 0) newRow = maxRows - 1;
                    currentToolbarRow = newRow;
                    int newIndex = (newRow * 12) + positionInRow;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"LB: Row {currentRow} -> {newRow}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // RB - Next row
                if (e.Pressed.Contains(SButton.RightShoulder))
                {
                    this.Helper.Input.Suppress(SButton.RightShoulder);
                    int newRow = currentToolbarRow + 1;
                    if (newRow >= maxRows) newRow = 0;
                    currentToolbarRow = newRow;
                    int newIndex = (newRow * 12) + positionInRow;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"RB: Row {currentRow} -> {newRow}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // LT - Move left in current row
                if (e.Pressed.Contains(SButton.LeftTrigger))
                {
                    this.Helper.Input.Suppress(SButton.LeftTrigger);
                    int newPosition = positionInRow - 1;
                    if (newPosition < 0) newPosition = 11;
                    int newIndex = (currentRow * 12) + newPosition;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"LT: Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }

                // RT - Move right in current row
                if (e.Pressed.Contains(SButton.RightTrigger))
                {
                    this.Helper.Input.Suppress(SButton.RightTrigger);
                    int newPosition = positionInRow + 1;
                    if (newPosition > 11) newPosition = 0;
                    int newIndex = (currentRow * 12) + newPosition;
                    player.CurrentToolIndex = newIndex;
                    Game1.playSound("shwip");
                    if (Config.VerboseLogging)
                        this.Monitor.Log($"RT: Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
                }
            }
        }

        /// <summary>Generate the controls display text based on current layout and style.</summary>
        private string GetControlsDisplay()
        {
            var layout = Config?.ControllerLayout ?? ControllerLayout.Switch;
            var style = Config?.ControlStyle ?? ControlStyle.Switch;

            // Determine button labels based on layout
            string aLabel, bLabel, xLabel, yLabel;

            if (layout == ControllerLayout.PlayStation)
            {
                aLabel = "Cross"; bLabel = "Circle"; xLabel = "Square"; yLabel = "Triangle";
            }
            else
            {
                aLabel = "A"; bLabel = "B"; xLabel = "X"; yLabel = "Y";
            }

            // A/B swap is based on layout vs style mismatch
            bool isXboxLayout = layout == ControllerLayout.Xbox || layout == ControllerLayout.PlayStation;
            bool isXboxStyle = style == ControlStyle.Xbox;
            bool swapAB = isXboxLayout != isXboxStyle;

            // Determine what A and B do based on style
            string aGameplay, bGameplay, aMenu, bMenu;

            if (swapAB)
            {
                aGameplay = "Cancel / Open Inventory";
                bGameplay = "Confirm / Talk / Pickup";
                aMenu = "Back / Close Menu";
                bMenu = "Confirm / Purchase / Ship Stack";
            }
            else
            {
                aGameplay = "Confirm / Talk / Pickup";
                bGameplay = "Cancel / Open Inventory";
                aMenu = "Confirm / Purchase / Ship Stack";
                bMenu = "Back / Close Menu";
            }

            // X/Y functions are ALWAYS positional based on layout:
            // Left button = Use Tool (gameplay), Add to Stacks (menu)
            // Top button = Crafting Menu (gameplay), Sort Inventory (menu)
            string xGameplay, yGameplay, xMenu, yMenu;

            if (layout == ControllerLayout.Switch)
            {
                // Switch: X is top, Y is left
                xGameplay = "Open Crafting Menu";
                yGameplay = "Use Tool";
                xMenu = "Sort Inventory";
                yMenu = "Add to Stacks / Ship One";
            }
            else
            {
                // Xbox/PS: X is left, Y is top
                xGameplay = "Use Tool";
                yGameplay = "Open Crafting Menu";
                xMenu = "Add to Stacks / Ship One";
                yMenu = "Sort Inventory";
            }

            // Toolbar controls depend on D-pad mode
            string toolbarControls;
            if (Config?.UseBumpersInsteadOfTriggers == true)
            {
                toolbarControls = "--- TOOLBAR (D-Pad Mode) ---\n" +
                                  "D-Pad Up/Down - Switch Rows\n" +
                                  "LB/RB/D-Pad L/R - Move Left/Right";
            }
            else
            {
                toolbarControls = "--- TOOLBAR ---\n" +
                                  "LB/RB - Switch Rows\n" +
                                  "LT/RT - Move Left/Right";
            }

            // Start button function
            string startGameplay = Config?.EnableJournalButton == true ? "Open Journal" : "Open Inventory";

            // Format with consistent ABXY order for both sections
            return $"--- GAMEPLAY ---\n" +
                   $"{aLabel} - {aGameplay}\n" +
                   $"{bLabel} - {bGameplay}\n" +
                   $"{xLabel} - {xGameplay}\n" +
                   $"{yLabel} - {yGameplay}\n" +
                   $"Start - {startGameplay}\n\n" +
                   $"--- MENUS ---\n" +
                   $"{aLabel} - {aMenu}\n" +
                   $"{bLabel} - {bMenu}\n" +
                   $"{xLabel} - {xMenu}\n" +
                   $"{yLabel} - {yMenu}\n\n" +
                   toolbarControls;
        }

        /// <summary>Register the config menu with GMCM.</summary>
        private void RegisterConfigMenu()
        {
            // Get Generic Mod Config Menu's API
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
            {
                this.Monitor.Log("Generic Mod Config Menu not found - config will only be available via config.json", LogLevel.Info);
                return;
            }

            // Register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(Config)
            );

            // Version Display - AT TOP
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => $"Android Consolizer v{this.ModManifest.Version}"
            );

            // Controller Settings
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Controller Settings"
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Controller Layout",
                tooltip: () => "The physical button layout of your controller.\n" +
                              "Switch/Odin: A=right, B=bottom, X=top, Y=left\n" +
                              "Xbox: A=bottom, B=right, X=left, Y=top\n" +
                              "PlayStation: Same as Xbox (Cross=A, Circle=B, Square=X, Triangle=Y)",
                getValue: () => Config.ControllerLayout.ToString(),
                setValue: value => Config.ControllerLayout = Enum.Parse<ControllerLayout>(value),
                allowedValues: new[] { "Switch", "Xbox", "PlayStation" },
                formatAllowedValue: value => value switch
                {
                    "Switch" => "Switch",
                    "Xbox" => "Xbox",
                    "PlayStation" => "PlayStation",
                    _ => value
                }
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Control Style",
                tooltip: () => "Which console's control scheme you want.\n" +
                              "Switch: Right=confirm, Bottom=cancel\n" +
                              "Xbox/PS: Bottom=confirm, Right=cancel",
                getValue: () => Config.ControlStyle.ToString(),
                setValue: value => Config.ControlStyle = Enum.Parse<ControlStyle>(value),
                allowedValues: new[] { "Switch", "Xbox" },
                formatAllowedValue: value => value switch
                {
                    "Switch" => "Switch style",
                    "Xbox" => "Xbox/PS style",
                    _ => value
                }
            );

            // Button Controls Display (dynamic based on layout/style)
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Your Controls"
            );

            configMenu.AddParagraph(
                mod: this.ModManifest,
                text: () => "(Save and reopen this menu to see updated controls)"
            );

            configMenu.AddParagraph(
                mod: this.ModManifest,
                text: () => GetControlsDisplay()
            );

            // Feature Toggles
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Feature Toggles"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Console Chests",
                tooltip: () => "Sort (X), fill stacks (Y), sidebar navigation, color picker, and A/Y item transfer in chests.",
                getValue: () => Config.EnableConsoleChests,
                setValue: value => Config.EnableConsoleChests = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Console Shops",
                tooltip: () => "A button purchases items, LT/RT quantity selector, sell tab with A/Y, right stick scroll.",
                getValue: () => Config.EnableConsoleShops,
                setValue: value => Config.EnableConsoleShops = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Console Toolbar",
                tooltip: () => "12-slot fixed toolbar. LB/RB switches rows, LT/RT moves left/right within row.",
                getValue: () => Config.EnableConsoleToolbar,
                setValue: value => Config.EnableConsoleToolbar = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Console Inventory",
                tooltip: () => "A picks up/places items, Y picks up one from stack, Y on fishing rod attaches/detaches bait and tackle.",
                getValue: () => Config.EnableConsoleInventory,
                setValue: value => Config.EnableConsoleInventory = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Console Shipping",
                tooltip: () => "A ships full stack, Y ships one item from the shipping bin.",
                getValue: () => Config.EnableConsoleShipping,
                setValue: value => Config.EnableConsoleShipping = value
            );

            // Standalone Features
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Standalone Features"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Start Opens Journal",
                tooltip: () => "Start button opens the Quest Log/Journal instead of inventory.",
                getValue: () => Config.EnableJournalButton,
                setValue: value => Config.EnableJournalButton = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Start Skips Cutscenes",
                tooltip: () => "Press Start twice during a skippable cutscene to skip it.",
                getValue: () => Config.EnableCutsceneSkip,
                setValue: value => Config.EnableCutsceneSkip = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Carpenter Menu Fix",
                tooltip: () => "Prevents Robin's building menu from instantly closing when opened with A button.",
                getValue: () => Config.EnableCarpenterMenuFix,
                setValue: value => Config.EnableCarpenterMenuFix = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Furniture Debounce",
                tooltip: () => "Prevents Y button from rapidly toggling furniture between placed and picked up. Adds ~500ms cooldown between interactions.",
                getValue: () => Config.EnableFurnitureDebounce,
                setValue: value => Config.EnableFurnitureDebounce = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Use Bumpers Instead of Triggers",
                tooltip: () => "For controllers where triggers aren't detected (e.g., Xbox via Bluetooth).\n" +
                              "Toolbar: D-Pad Up/Down switches rows, LB/RB moves within row.\n" +
                              "Shops: LB/RB adjusts purchase quantity.",
                getValue: () => Config.UseBumpersInsteadOfTriggers,
                setValue: value => Config.UseBumpersInsteadOfTriggers = value
            );

            // Debug
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Debug"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Verbose Logging",
                tooltip: () => "Log detailed information for debugging.",
                getValue: () => Config.VerboseLogging,
                setValue: value => Config.VerboseLogging = value
            );
        }
    }

    /// <summary>Interface for Generic Mod Config Menu API.</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddParagraph(IManifest mod, Func<string> text);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
    }
}
