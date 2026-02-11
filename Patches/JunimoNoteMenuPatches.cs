using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Diagnostic patches for JunimoNoteMenu (Community Center bundles).
    /// Logs component layout, navigation wiring, and A-press behavior.
    /// </summary>
    internal static class JunimoNoteMenuPatches
    {
        private static IMonitor Monitor;
        private static bool _hasLoggedOverview = false;
        private static bool _hasLoggedBundlePage = false;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            // Patch receiveGamePadButton to log all gamepad input
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveGamePadButton)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveGamePadButton_Prefix))
            );

            // Patch receiveLeftClick to log click coordinates vs snapped component
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), nameof(JunimoNoteMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(ReceiveLeftClick_Prefix))
            );

            // Patch update to log component layout once when bundle page opens
            harmony.Patch(
                original: AccessTools.Method(typeof(JunimoNoteMenu), "update", new[] { typeof(GameTime) }),
                postfix: new HarmonyMethod(typeof(JunimoNoteMenuPatches), nameof(Update_Postfix))
            );

            Monitor.Log("JunimoNoteMenu diagnostic patches applied.", LogLevel.Trace);
        }

        /// <summary>Reset logging flag when menu changes.</summary>
        public static void OnMenuChanged()
        {
            _hasLoggedOverview = false;
            _hasLoggedBundlePage = false;
        }

        private static void ReceiveGamePadButton_Prefix(JunimoNoteMenu __instance, Microsoft.Xna.Framework.Input.Buttons b)
        {
            try
            {
                var snapped = __instance.currentlySnappedComponent;
                bool specificBundle = GetSpecificBundlePage(__instance);

                Monitor.Log($"[JunimoNote DIAG] receiveGamePadButton: button={b}, specificBundlePage={specificBundle}, " +
                    $"snapped={snapped?.myID.ToString() ?? "null"} '{snapped?.name ?? ""}' " +
                    $"bounds={snapped?.bounds.ToString() ?? "N/A"}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] receiveGamePadButton error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void ReceiveLeftClick_Prefix(JunimoNoteMenu __instance, int x, int y)
        {
            try
            {
                var snapped = __instance.currentlySnappedComponent;
                bool specificBundle = GetSpecificBundlePage(__instance);

                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick: x={x}, y={y}, specificBundlePage={specificBundle}, " +
                    $"snapped={snapped?.myID.ToString() ?? "null"} '{snapped?.name ?? ""}' " +
                    $"bounds={snapped?.bounds.ToString() ?? "N/A"}, " +
                    $"mouseX={Game1.getMouseX()}, mouseY={Game1.getMouseY()}", LogLevel.Info);

                // Log what component the click actually hits
                if (__instance.allClickableComponents != null)
                {
                    foreach (var comp in __instance.allClickableComponents)
                    {
                        if (comp.containsPoint(x, y))
                        {
                            Monitor.Log($"[JunimoNote DIAG]   -> Click hits component: id={comp.myID} '{comp.name}' bounds={comp.bounds}", LogLevel.Info);
                        }
                    }
                }

                // Also check ingredientSlots and ingredientList via reflection
                var ingredientSlots = GetField<List<ClickableTextureComponent>>(__instance, "ingredientSlots");
                if (ingredientSlots != null)
                {
                    for (int i = 0; i < ingredientSlots.Count; i++)
                    {
                        if (ingredientSlots[i].containsPoint(x, y))
                        {
                            Monitor.Log($"[JunimoNote DIAG]   -> Click hits ingredientSlot[{i}]: id={ingredientSlots[i].myID} bounds={ingredientSlots[i].bounds}", LogLevel.Info);
                        }
                    }
                }

                var ingredientList = GetField<List<ClickableTextureComponent>>(__instance, "ingredientList");
                if (ingredientList != null)
                {
                    for (int i = 0; i < ingredientList.Count; i++)
                    {
                        if (ingredientList[i].containsPoint(x, y))
                        {
                            Monitor.Log($"[JunimoNote DIAG]   -> Click hits ingredientList[{i}]: id={ingredientList[i].myID} " +
                                $"item={ingredientList[i].item?.Name ?? "null"} bounds={ingredientList[i].bounds}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] receiveLeftClick error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void Update_Postfix(JunimoNoteMenu __instance, GameTime time)
        {
            try
            {
                bool specificBundle = GetSpecificBundlePage(__instance);

                // Log overview page components once
                if (!specificBundle && !_hasLoggedOverview)
                {
                    _hasLoggedOverview = true;
                    _hasLoggedBundlePage = false; // Reset for next bundle page entry
                    Monitor.Log("[JunimoNote DIAG] === OVERVIEW PAGE (bundle selection) ===", LogLevel.Info);
                    LogAllComponents(__instance);
                }

                // Log bundle page components once
                if (specificBundle && !_hasLoggedBundlePage)
                {
                    _hasLoggedBundlePage = true;
                    _hasLoggedOverview = false; // Reset for when we go back to overview
                    Monitor.Log("[JunimoNote DIAG] === SPECIFIC BUNDLE PAGE (ingredient donation) ===", LogLevel.Info);
                    LogAllComponents(__instance);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[JunimoNote DIAG] Update_Postfix error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void LogAllComponents(JunimoNoteMenu menu)
        {
            Monitor.Log("[JunimoNote DIAG] === BUNDLE PAGE COMPONENT DUMP ===", LogLevel.Info);

            // Log allClickableComponents
            if (menu.allClickableComponents != null)
            {
                Monitor.Log($"[JunimoNote DIAG] allClickableComponents count: {menu.allClickableComponents.Count}", LogLevel.Info);
                foreach (var comp in menu.allClickableComponents)
                {
                    Monitor.Log($"[JunimoNote DIAG]   id={comp.myID} name='{comp.name}' bounds={comp.bounds} " +
                        $"up={comp.upNeighborID} down={comp.downNeighborID} left={comp.leftNeighborID} right={comp.rightNeighborID} " +
                        $"fullyImmutable={comp.fullyImmutable}", LogLevel.Info);
                }
            }

            // Log ingredientSlots
            var ingredientSlots = GetField<List<ClickableTextureComponent>>(__instance: menu, "ingredientSlots");
            if (ingredientSlots != null)
            {
                Monitor.Log($"[JunimoNote DIAG] ingredientSlots count: {ingredientSlots.Count}", LogLevel.Info);
                for (int i = 0; i < ingredientSlots.Count; i++)
                {
                    var s = ingredientSlots[i];
                    Monitor.Log($"[JunimoNote DIAG]   slot[{i}]: id={s.myID} name='{s.name}' bounds={s.bounds} " +
                        $"item={s.item?.Name ?? "null"} " +
                        $"up={s.upNeighborID} down={s.downNeighborID} left={s.leftNeighborID} right={s.rightNeighborID}", LogLevel.Info);
                }
            }

            // Log ingredientList
            var ingredientList = GetField<List<ClickableTextureComponent>>(__instance: menu, "ingredientList");
            if (ingredientList != null)
            {
                Monitor.Log($"[JunimoNote DIAG] ingredientList count: {ingredientList.Count}", LogLevel.Info);
                for (int i = 0; i < ingredientList.Count; i++)
                {
                    var l = ingredientList[i];
                    Monitor.Log($"[JunimoNote DIAG]   list[{i}]: id={l.myID} name='{l.name}' bounds={l.bounds} " +
                        $"item={l.item?.Name ?? "null"} " +
                        $"up={l.upNeighborID} down={l.downNeighborID} left={l.leftNeighborID} right={l.rightNeighborID}", LogLevel.Info);
                }
            }

            // Log inventory if present
            if (menu.inventory != null)
            {
                Monitor.Log($"[JunimoNote DIAG] inventory.inventory count: {menu.inventory.inventory?.Count ?? 0}", LogLevel.Info);
                if (menu.inventory.inventory != null)
                {
                    for (int i = 0; i < Math.Min(menu.inventory.inventory.Count, 12); i++)
                    {
                        var inv = menu.inventory.inventory[i];
                        Monitor.Log($"[JunimoNote DIAG]   inv[{i}]: id={inv.myID} bounds={inv.bounds} " +
                            $"up={inv.upNeighborID} down={inv.downNeighborID} left={inv.leftNeighborID} right={inv.rightNeighborID}", LogLevel.Info);
                    }
                    if (menu.inventory.inventory.Count > 12)
                        Monitor.Log($"[JunimoNote DIAG]   ... and {menu.inventory.inventory.Count - 12} more inventory slots", LogLevel.Info);
                }
            }

            // Log snapped component
            var snapped = menu.currentlySnappedComponent;
            Monitor.Log($"[JunimoNote DIAG] currentlySnappedComponent: id={snapped?.myID.ToString() ?? "null"} '{snapped?.name ?? ""}'", LogLevel.Info);

            Monitor.Log("[JunimoNote DIAG] === END COMPONENT DUMP ===", LogLevel.Info);
        }

        private static bool GetSpecificBundlePage(JunimoNoteMenu menu)
        {
            try
            {
                var field = AccessTools.Field(typeof(JunimoNoteMenu), "specificBundlePage");
                if (field != null)
                    return (bool)field.GetValue(menu);
            }
            catch { }
            return false;
        }

        private static T GetField<T>(JunimoNoteMenu __instance, string fieldName) where T : class
        {
            try
            {
                var field = AccessTools.Field(typeof(JunimoNoteMenu), fieldName);
                return field?.GetValue(__instance) as T;
            }
            catch { return null; }
        }
    }
}
