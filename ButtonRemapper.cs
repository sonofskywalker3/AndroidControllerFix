using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;

namespace AndroidConsolizer
{
    /// <summary>Physical button layout of the controller.</summary>
    public enum ControllerLayout
    {
        /// <summary>Switch/Odin layout: A=right, B=bottom, X=top, Y=left.</summary>
        Switch,
        /// <summary>Xbox layout: A=bottom, B=right, X=left, Y=top.</summary>
        Xbox,
        /// <summary>PlayStation layout: Same positions as Xbox (Cross=A, Circle=B, Square=X, Triangle=Y).</summary>
        PlayStation
    }

    /// <summary>Desired control style (which console's button behavior to emulate).</summary>
    public enum ControlStyle
    {
        /// <summary>Switch-style controls: Right=confirm, Bottom=cancel.</summary>
        Switch,
        /// <summary>Xbox/PS-style controls: Bottom=confirm, Right=cancel.</summary>
        Xbox
    }

    /// <summary>
    /// Remaps controller buttons based on controller layout and desired control style.
    /// </summary>
    internal static class ButtonRemapper
    {
        /// <summary>
        /// A/B swap is now handled by GameplayButtonPatches at the GamePad.GetState level,
        /// which applies everywhere (main menu, game menus, gameplay).
        /// This method always returns false to avoid double-swapping.
        /// </summary>
        private static bool ShouldSwapAB()
        {
            // A/B swap handled by GamePad.GetState patch - don't swap again here
            return false;
        }

        /// <summary>
        /// Determines if X and Y buttons should be swapped based on LAYOUT.
        /// X/Y functions are positional: Top=Sort, Left=AddToStacks (same as gameplay).
        /// Switch layout: X=top, Y=left - no swap needed (X=Sort, Y=AddToStacks)
        /// Xbox/PS layout: X=left, Y=top - swap needed (Y=Sort, X=AddToStacks)
        /// </summary>
        private static bool ShouldSwapXY()
        {
            var layout = ModEntry.Config?.ControllerLayout ?? ControllerLayout.Switch;

            // Swap X/Y for Xbox/PS layout so that:
            // - Top button (Y on Xbox) triggers Sort (which checks for X after swap)
            // - Left button (X on Xbox) triggers AddToStacks (which checks for Y after swap)
            return layout == ControllerLayout.Xbox || layout == ControllerLayout.PlayStation;
        }

        /// <summary>Remap an XNA Buttons value based on the current settings.</summary>
        public static Buttons Remap(Buttons button)
        {
            bool swapAB = ShouldSwapAB();
            bool swapXY = ShouldSwapXY();

            return button switch
            {
                Buttons.A when swapAB => Buttons.B,
                Buttons.B when swapAB => Buttons.A,
                Buttons.X when swapXY => Buttons.Y,
                Buttons.Y when swapXY => Buttons.X,
                _ => button
            };
        }

        /// <summary>Remap an SMAPI SButton value based on the current settings.</summary>
        public static SButton Remap(SButton button)
        {
            bool swapAB = ShouldSwapAB();
            bool swapXY = ShouldSwapXY();

            return button switch
            {
                SButton.ControllerA when swapAB => SButton.ControllerB,
                SButton.ControllerB when swapAB => SButton.ControllerA,
                SButton.ControllerX when swapXY => SButton.ControllerY,
                SButton.ControllerY when swapXY => SButton.ControllerX,
                _ => button
            };
        }

        /// <summary>Check if the specified button (after remapping) matches the target button.</summary>
        public static bool IsButton(Buttons pressed, Buttons target)
        {
            return Remap(pressed) == target;
        }

        /// <summary>Check if the specified button (after remapping) matches the target button.</summary>
        public static bool IsButton(SButton pressed, SButton target)
        {
            return Remap(pressed) == target;
        }

        /// <summary>Get a description of the current remapping for logging.</summary>
        public static string GetRemapDescription()
        {
            bool swapAB = ShouldSwapAB();
            bool swapXY = ShouldSwapXY();

            if (!swapAB && !swapXY)
                return "No remapping";

            var parts = new System.Collections.Generic.List<string>();
            if (swapAB) parts.Add("A↔B");
            if (swapXY) parts.Add("X↔Y");
            return $"Swapping: {string.Join(", ", parts)}";
        }
    }
}
