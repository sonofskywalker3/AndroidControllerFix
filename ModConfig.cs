using StardewModdingAPI;

namespace AndroidConsolizer
{
    /// <summary>The mod configuration model.</summary>
    public class ModConfig
    {
        /*********
        ** Controller Settings
        *********/
        /// <summary>
        /// Physical button layout of your controller.
        /// Switch/Odin: A=right, B=bottom, X=top, Y=left.
        /// Xbox/PlayStation: A=bottom, B=right, X=left, Y=top.
        /// </summary>
        public ControllerLayout ControllerLayout { get; set; } = ControllerLayout.Switch;

        /// <summary>
        /// Which console's control scheme you want to use.
        /// Switch: Right=confirm, Bottom=cancel.
        /// Xbox/PS: Bottom=confirm, Right=cancel.
        /// </summary>
        public ControlStyle ControlStyle { get; set; } = ControlStyle.Switch;

        /*********
        ** Shop Settings
        *********/
        /// <summary>Whether to enable the shop purchase fix (A button buys items).</summary>
        public bool EnableShopPurchaseFix { get; set; } = true;

        /*********
        ** Toolbar Settings
        *********/
        /// <summary>Whether to enable toolbar navigation fix (LB/RB switch rows, LT/RT move within row).</summary>
        public bool EnableToolbarNavFix { get; set; } = true;

        /// <summary>
        /// Use D-pad for toolbar navigation instead of triggers.
        /// Up/Down switches rows, Left/Right switches tools.
        /// For controllers where Stardew Valley can't read the triggers (e.g., Xbox on Android).
        /// </summary>
        public bool UseDpadForToolbarNav { get; set; } = false;

        /*********
        ** Inventory & Chest Settings
        *********/
        /// <summary>Whether to enable X button sorting (inventory and chest).</summary>
        public bool EnableSortFix { get; set; } = true;

        /// <summary>Whether to enable Y button add-to-stacks (in chest).</summary>
        public bool EnableAddToStacksFix { get; set; } = true;

        /// <summary>Whether to enable shipping bin stacking fix (A button adds to existing stacks).</summary>
        public bool EnableShippingBinFix { get; set; } = true;

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
