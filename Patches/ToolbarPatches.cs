using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>Harmony patches to replace Android's scrolling toolbar with a fixed 12-slot console-style toolbar.</summary>
    internal static class ToolbarPatches
    {
        private static IMonitor Monitor;

        // Toolbar slot dimensions (scaled)
        private const int SlotSize = 64;
        private const int SlotSpacing = 4;

        /// <summary>Apply Harmony patches.</summary>
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                // Completely replace Toolbar.draw with our own implementation
                harmony.Patch(
                    original: AccessTools.Method(typeof(Toolbar), nameof(Toolbar.draw), new Type[] { typeof(SpriteBatch) }),
                    prefix: new HarmonyMethod(typeof(ToolbarPatches), nameof(Toolbar_Draw_Prefix))
                );

                Monitor.Log("Toolbar patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply Toolbar patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix that completely replaces the toolbar drawing with our own 12-slot version.
        /// </summary>
        /// <returns>False to skip the original method entirely.</returns>
        private static bool Toolbar_Draw_Prefix(Toolbar __instance, SpriteBatch b)
        {
            try
            {
                // If feature is disabled, let original run
                if (!(ModEntry.Config?.EnableToolbarNavFix ?? false))
                    return true;

                var player = Game1.player;
                if (player == null)
                    return true;

                // Don't draw during events or when game says not to
                if (Game1.activeClickableMenu != null)
                    return false;

                // Calculate current row info
                int currentRow = FarmerPatches.CurrentToolbarRow;
                int rowStart = currentRow * 12;

                // Calculate toolbar dimensions
                int toolbarWidth = (SlotSize * 12) + (SlotSpacing * 11);
                int toolbarHeight = SlotSize;

                // Screen edge padding (matches game's UI spacing)
                // Note: toolbar background extends 16px beyond content, so we add that
                int edgePadding = 8;
                int backgroundPadding = 16;

                // Position at bottom center of screen with padding
                int toolbarX = (Game1.uiViewport.Width - toolbarWidth) / 2;
                int toolbarY = Game1.uiViewport.Height - toolbarHeight - backgroundPadding - edgePadding;

                // Check if player is in bottom half - move toolbar to top if so
                bool isAtTop = player.getLocalPosition(Game1.viewport).Y > (Game1.viewport.Height / 2 + 64);
                if (isAtTop)
                {
                    toolbarY = backgroundPadding + edgePadding + 8; // Extra 8 to align with date box
                    // Shift left to avoid date/time display in top right, with padding
                    toolbarX = backgroundPadding + edgePadding;
                }

                // Draw toolbar background
                IClickableMenu.drawTextureBox(
                    b,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    toolbarX - 16,
                    toolbarY - 16,
                    toolbarWidth + 32,
                    toolbarHeight + 32,
                    Color.White,
                    1f,
                    false
                );

                // Draw each slot
                for (int i = 0; i < 12; i++)
                {
                    int itemIndex = rowStart + i;
                    int slotX = toolbarX + (i * (SlotSize + SlotSpacing));
                    int slotY = toolbarY;
                    bool isSelected = player.CurrentToolIndex == itemIndex;

                    // Draw selection highlight FIRST (behind item), slightly larger than slot
                    if (isSelected)
                    {
                        int borderPadding = 4;
                        IClickableMenu.drawTextureBox(
                            b,
                            Game1.menuTexture,
                            new Rectangle(0, 256, 60, 60),
                            slotX - borderPadding,
                            slotY - borderPadding,
                            SlotSize + (borderPadding * 2),
                            SlotSize + (borderPadding * 2),
                            Color.White,
                            1f,
                            false
                        );
                    }

                    // Draw slot background
                    b.Draw(
                        Game1.menuTexture,
                        new Rectangle(slotX, slotY, SlotSize, SlotSize),
                        new Rectangle(128, 128, 64, 64),
                        Color.White
                    );

                    // Draw item on top
                    if (itemIndex < player.Items.Count && player.Items[itemIndex] != null)
                    {
                        var item = player.Items[itemIndex];
                        item.drawInMenu(
                            b,
                            new Vector2(slotX, slotY),
                            isSelected ? 1f : 0.8f,
                            1f,
                            0.9f,
                            StackDrawType.Draw,
                            Color.White,
                            true
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor?.Log($"Error in custom toolbar draw: {ex.Message}", LogLevel.Error);
                return true; // Fall back to original on error
            }

            return false; // Skip original Toolbar.draw
        }
    }
}
