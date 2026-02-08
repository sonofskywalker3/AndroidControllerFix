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
        ** Feature Toggles
        *********/
        /// <summary>Console-style chest controls: sort (X), fill stacks (Y), sidebar navigation, color picker, A/Y item transfer.</summary>
        public bool EnableConsoleChests { get; set; } = true;

        /// <summary>Console-style shop controls: A button purchases, quantity selector, sell tab, right stick scroll.</summary>
        public bool EnableConsoleShops { get; set; } = true;

        /// <summary>Console-style toolbar: 12-slot fixed toolbar with LB/RB row switching and LT/RT slot movement.</summary>
        public bool EnableConsoleToolbar { get; set; } = true;

        /// <summary>Console-style inventory: A picks up/places items, Y picks up one, fishing rod bait/tackle via Y.</summary>
        public bool EnableConsoleInventory { get; set; } = true;

        /// <summary>Console-style shipping bin: A ships full stack, Y ships one item.</summary>
        public bool EnableConsoleShipping { get; set; } = true;

        /*********
        ** Standalone Features
        *********/
        /// <summary>Whether Start button opens the Quest Log/Journal instead of inventory.</summary>
        public bool EnableJournalButton { get; set; } = true;

        /// <summary>Whether Start button can skip cutscenes (press twice to skip).</summary>
        public bool EnableCutsceneSkip { get; set; } = true;

        /// <summary>Whether to enable the carpenter menu fix (prevents Robin's building menu from instantly closing).</summary>
        public bool EnableCarpenterMenuFix { get; set; } = true;

        /// <summary>Whether to debounce furniture Y-button interactions (prevents rapid toggle between placed and picked up).</summary>
        public bool EnableFurnitureDebounce { get; set; } = true;

        /// <summary>
        /// Use bumpers (LB/RB) instead of triggers (LT/RT) for controls.
        /// Toolbar: D-Pad Up/Down switches rows, bumpers move within row.
        /// Shops: Bumpers adjust purchase quantity.
        /// For controllers where Stardew Valley can't read the triggers (e.g., Xbox via Bluetooth on Android).
        /// </summary>
        public bool UseBumpersInsteadOfTriggers { get; set; } = false;

        /*********
        ** Debug Settings
        *********/
        /// <summary>Whether to log verbose debug information.</summary>
        public bool VerboseLogging { get; set; } = false;
    }
}
