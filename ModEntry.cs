using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AndroidControllerFix
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

            // Register events
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
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

        /// <summary>Raised every game tick. Used to enforce toolbar row locking and handle triggers.</summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Only enforce during gameplay
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
            GamePadState gpState = GamePad.GetState(PlayerIndex.One);

            bool isLeftTriggerDown = gpState.Triggers.Left > TriggerThreshold;
            bool isRightTriggerDown = gpState.Triggers.Right > TriggerThreshold;

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

            // Intercept controller X button BEFORE the game sees it (prevents Android deletion bug)
            if (Config.EnableSortFix && e.Pressed.Contains(SButton.ControllerX))
            {
                // Check if we're in the inventory menu
                if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
                {
                    this.Monitor.Log("Intercepting X button in inventory - suppressing and sorting", LogLevel.Debug);
                    this.Helper.Input.Suppress(SButton.ControllerX);
                    Patches.InventoryPagePatches.SortPlayerInventory();
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

            int currentIndex = player.CurrentToolIndex;
            int maxItems = player.MaxItems;
            int maxRows = maxItems / 12;
            int currentRow = currentIndex / 12;
            int positionInRow = currentIndex % 12;

            // LB - Previous row
            if (e.Pressed.Contains(SButton.LeftShoulder))
            {
                this.Helper.Input.Suppress(SButton.LeftShoulder);
                int newRow = currentToolbarRow - 1;
                if (newRow < 0) newRow = maxRows - 1; // Wrap to last row
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
                if (newRow >= maxRows) newRow = 0; // Wrap to first row
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
                if (newPosition < 0) newPosition = 11; // Wrap to end of row
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
                if (newPosition > 11) newPosition = 0; // Wrap to start of row
                int newIndex = (currentRow * 12) + newPosition;
                player.CurrentToolIndex = newIndex;
                Game1.playSound("shwip");
                if (Config.VerboseLogging)
                    this.Monitor.Log($"RT: Position {positionInRow} -> {newPosition}, Index {currentIndex} -> {newIndex}", LogLevel.Debug);
            }
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

            // Button Mappings Display
            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => "Current Button Mappings"
            );

            configMenu.AddParagraph(
                mod: this.ModManifest,
                text: () => "A Button: Purchase items in shops\n" +
                           "X Button: Sort (inventory or chest)\n" +
                           "Y Button: Add to existing stacks (in chest)\n" +
                           "LB/RB: Switch toolbar rows\n" +
                           "LT/RT: Move left/right in toolbar"
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
                name: () => "Enable Toolbar Navigation",
                tooltip: () => "LB/RB switches toolbar rows, LT/RT moves left/right within row (console-style)",
                getValue: () => Config.EnableToolbarNavFix,
                setValue: value => Config.EnableToolbarNavFix = value
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
    }
}
