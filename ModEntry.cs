using System;
using System.Linq;
using HarmonyLib;
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

            // Register events
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;

            this.Monitor.Log("Android Controller Fix loaded successfully.", LogLevel.Info);
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
                           "Y Button: Add to existing stacks (in chest)"
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
