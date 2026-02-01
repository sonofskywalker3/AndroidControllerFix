using StardewModdingAPI;

namespace AndroidControllerFix
{
    /// <summary>The mod configuration model.</summary>
    public class ModConfig
    {
        /*********
        ** Shop Settings
        *********/
        /// <summary>Whether to enable the shop purchase fix (A button buys items).</summary>
        public bool EnableShopPurchaseFix { get; set; } = true;

        /*********
        ** Inventory & Chest Settings
        *********/
        /// <summary>Whether to enable X button sorting (inventory and chest).</summary>
        public bool EnableSortFix { get; set; } = true;

        /// <summary>Whether to enable Y button add-to-stacks (in chest).</summary>
        public bool EnableAddToStacksFix { get; set; } = true;

        /*********
        ** Legacy Settings (kept for compatibility but not used)
        *********/
        public bool EnableBuySellToggle { get; set; } = false;
        public SButton BuySellToggleButton { get; set; } = SButton.ControllerY;
        public bool EnableChestOrganizeFix { get; set; } = true;
        public SButton ChestOrganizeButton { get; set; } = SButton.ControllerX;
        public SButton AddToStacksButton { get; set; } = SButton.ControllerY;

        /*********
        ** Debug Settings
        *********/
        /// <summary>Whether to log verbose debug information.</summary>
        public bool VerboseLogging { get; set; } = true;
    }
}
