using System;
using System.Linq;
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

            // Register events
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;

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

        /// <summary>Raised every game tick. Used to enforce toolbar row locking and handle triggers.</summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // X/Y swap is now handled by GameplayButtonPatches at the GamePad.GetState level

            // Only enforce toolbar fix during gameplay
            if (!Config.EnableToolbarNavFix || Game1.activeClickableMenu != null || !Context.IsPlayerFree)
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
            if (Config.UseDpadForToolbarNav)
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

            // Intercept sort button BEFORE the game sees it (prevents Android deletion bug)
            // With button remapping, check if any pressed button remaps to X (the sort action)
            if (Config.EnableSortFix)
            {
                foreach (var button in e.Pressed)
                {
                    if (ButtonRemapper.Remap(button) == SButton.ControllerX)
                    {
                        // Check if we're in the inventory menu
                        if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
                        {
                            this.Monitor.Log($"Intercepting {button} (remapped to X) in inventory - suppressing and sorting", LogLevel.Debug);
                            this.Helper.Input.Suppress(button);
                            Patches.InventoryPagePatches.SortPlayerInventory();
                        }
                        break;
                    }
                }
            }

            // Toolbar navigation fix - only when NOT in a menu (during gameplay)
            if (Config.EnableToolbarNavFix && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                HandleToolbarNavigation(e);
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
                this.Monitor.Log($"D-Pad pressed. UseDpadForToolbarNav = {Config.UseDpadForToolbarNav}", LogLevel.Debug);
            }

            int currentIndex = player.CurrentToolIndex;
            int maxItems = player.MaxItems;
            int maxRows = maxItems / 12;
            int currentRow = currentIndex / 12;
            int positionInRow = currentIndex % 12;

            // Check if using D-pad for toolbar navigation
            if (Config.UseDpadForToolbarNav)
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
            if (Config?.UseDpadForToolbarNav == true)
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

            // Format with consistent ABXY order for both sections
            return $"--- GAMEPLAY ---\n" +
                   $"{aLabel} - {aGameplay}\n" +
                   $"{bLabel} - {bGameplay}\n" +
                   $"{xLabel} - {xGameplay}\n" +
                   $"{yLabel} - {yGameplay}\n\n" +
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

            // Shop Settings
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Shop Fixes"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Enable Shop Purchase Fix",
                tooltip: () => "A button purchases items in shops (with quantity from LT/RT)",
                getValue: () => Config.EnableShopPurchaseFix,
                setValue: value => Config.EnableShopPurchaseFix = value
            );

            // Toolbar Settings
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Toolbar Fixes"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Enable Console Toolbar",
                tooltip: () => "LB/RB switches toolbar rows, LT/RT moves left/right within row (console-style)",
                getValue: () => Config.EnableToolbarNavFix,
                setValue: value => Config.EnableToolbarNavFix = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Use D-Pad for Toolbar (If triggers can't be detected)",
                tooltip: () => "For controllers where Stardew Valley can't read the triggers (e.g., Xbox on Android).\n" +
                              "D-Pad Up/Down switches rows, LB/RB/D-Pad Left/Right switches tools.\n" +
                              "D-Pad still works normally in menus.",
                getValue: () => Config.UseDpadForToolbarNav,
                setValue: value => Config.UseDpadForToolbarNav = value
            );

            // Inventory/Chest Settings
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Inventory & Chest Fixes"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Enable Sort (X Button)",
                tooltip: () => "X button sorts inventory when in inventory menu, sorts chest when in chest menu",
                getValue: () => Config.EnableSortFix,
                setValue: value => Config.EnableSortFix = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Enable Add to Stacks (Y Button)",
                tooltip: () => "Y button adds matching items to existing stacks in chest",
                getValue: () => Config.EnableAddToStacksFix,
                setValue: value => Config.EnableAddToStacksFix = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Enable Shipping Bin Fix",
                tooltip: () => "A button ships items, Y button ships one item (console-style controls)",
                getValue: () => Config.EnableShippingBinFix,
                setValue: value => Config.EnableShippingBinFix = value
            );

            // Debug
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Debug"
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Verbose Logging",
                tooltip: () => "Log detailed information for debugging",
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
