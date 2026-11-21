using System;
using System.Collections.Generic;
using System.Reflection;
using DubsMintMenus;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DubsArchitectureOptimization;

internal static class CategoryRailPatches
{
    private static bool reportedLeftFailure;
    private static bool reportedRightFailure;
    private static bool reportedHeaderButtonFailure;
    private static bool reportedHiddenFailure;
    private static bool rightRailVisible;
    private static bool hiddenRailVisible;
    private static bool headerButtonVisible;
    private static bool newGroupButtonVisible;
    private static Rect headerButtonRect;
    private static Rect newGroupButtonRect;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(MintBridge.DrawArchitectList, prefix: GetHarmonyMethod(nameof(DrawArchitectListPrefix)));
        harmony.Patch(MintBridge.ScrollBox, prefix: GetHarmonyMethod(nameof(ScrollBoxPrefix)));
        harmony.Patch(
            MintBridge.DrawGridHeader,
            prefix: GetHarmonyMethod(nameof(DrawGridHeaderPrefix)),
            postfix: GetHarmonyMethod(nameof(DrawGridHeaderPostfix)));
        if (MintBridge.RightClickMenu != null)
        {
            harmony.Patch(MintBridge.RightClickMenu, prefix: GetHarmonyMethod(nameof(RightClickMenuPrefix)));
        }
    }

    private static HarmonyMethod GetHarmonyMethod(string name)
    {
        MethodInfo method = typeof(CategoryRailPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        return new HarmonyMethod(method);
    }

    private static bool DrawArchitectListPrefix(MainTabWindow_MintArchitect __instance, Rect box)
    {
        if (!MintBridge.Available)
        {
            return true;
        }

        try
        {
            CategoryRailDrawer.DrawLeft(__instance, box);
            return false;
        }
        catch (Exception exception)
        {
            if (!reportedLeftFailure)
            {
                reportedLeftFailure = true;
                Log.Error($"[{MintTwoColMod.HarmonyId}] Custom category rail failed, falling back to Dubs Mint Menus layout: {exception}");
            }
            return true;
        }
    }

    private static bool ScrollBoxPrefix(MainTabWindow_MintArchitect __instance, ref Rect canvas)
    {
        rightRailVisible = false;
        hiddenRailVisible = false;
        if (!MintBridge.Available)
        {
            return true;
        }

        float railSpan = CategoryRailDrawer.RightRailWidth + CategoryRailDrawer.RailGap;
        if (MintTwoColMod.Settings.ShowHiddenRail
            && canvas.width - railSpan * 2f >= CategoryRailDrawer.MinGridWidth)
        {
            Rect hidden = new Rect(
                canvas.xMax - CategoryRailDrawer.RightRailWidth,
                canvas.y - CategoryRailDrawer.HeaderHeight,
                CategoryRailDrawer.RightRailWidth,
                canvas.height + CategoryRailDrawer.HeaderHeight);
            try
            {
                CategoryRailDrawer.DrawHidden(__instance, hidden);
                hiddenRailVisible = true;
                canvas.width -= railSpan;
            }
            catch (Exception exception)
            {
                if (!reportedHiddenFailure)
                {
                    reportedHiddenFailure = true;
                    Log.Error($"[{MintTwoColMod.HarmonyId}] Hidden category rail failed, it stays closed: {exception}");
                }
            }
        }

        if (MintTwoColMod.Settings.DisableRightRail)
        {
            return true;
        }

        if (canvas.width - railSpan < CategoryRailDrawer.MinGridWidth)
        {
            return true;
        }

        Rect rail = new Rect(
            canvas.xMax - CategoryRailDrawer.RightRailWidth,
            canvas.y - CategoryRailDrawer.HeaderHeight,
            CategoryRailDrawer.RightRailWidth,
            canvas.height + CategoryRailDrawer.HeaderHeight);

        try
        {
            CategoryRailDrawer.DrawRight(__instance, rail);
            rightRailVisible = true;
            canvas.width -= railSpan;
        }
        catch (Exception exception)
        {
            rightRailVisible = false;
            if (!reportedRightFailure)
            {
                reportedRightFailure = true;
                Log.Error($"[{MintTwoColMod.HarmonyId}] Custom mod category rail failed, grid keeps full width: {exception}");
            }
        }
        return true;
    }

    // Mint 网格/列表右键菜单入口：建筑项上追加「加入项组」，原有右键选项原样保留。
    private static bool RightClickMenuPrefix(Designator des)
    {
        if (!(des is Designator_Place place) || !(place.PlacingDef is ThingDef def) || string.IsNullOrEmpty(def.defName))
        {
            return true;
        }

        List<FloatMenuOption> options = new List<FloatMenuOption>();
        if (des.RightClickFloatMenuOptions != null)
        {
            options.AddRange(des.RightClickFloatMenuOptions);
        }
        options.AddRange(CategoryRailDrawer.ItemGroupMenuOptions(def.defName));

        Event.current.Use();
        if (options.Count > 0)
        {
            Find.WindowStack.Add(new FloatMenu(options));
        }
        return false;
    }

    private static void DrawGridHeaderPrefix(ref Rect header)    {
        float railSpan = CategoryRailDrawer.RightRailWidth + CategoryRailDrawer.RailGap;
        if (rightRailVisible)
        {
            header.width -= railSpan;
        }
        if (hiddenRailVisible)
        {
            header.width -= railSpan;
        }

        headerButtonVisible = false;
        newGroupButtonVisible = false;
        if (!MintBridge.Available || header.width < CategoryRailDrawer.MinHeaderWidthForButton + CategoryRailDrawer.HeaderButtonWidth)
        {
            return;
        }

        // 最右是隐藏栏开关，其左侧是新建组按钮。
        header.width -= CategoryRailDrawer.HeaderButtonWidth;
        headerButtonRect = CategoryRailDrawer.HeaderButtonRect(header);
        headerButtonVisible = true;
        header.width -= CategoryRailDrawer.HeaderButtonWidth;
        newGroupButtonRect = CategoryRailDrawer.HeaderButtonRect(header);
        newGroupButtonVisible = true;
    }

    private static void DrawGridHeaderPostfix()
    {
        if (newGroupButtonVisible)
        {
            try
            {
                CategoryRailDrawer.DrawNewGroupButton(newGroupButtonRect);
            }
            catch (Exception exception)
            {
                if (!reportedHeaderButtonFailure)
                {
                    reportedHeaderButtonFailure = true;
                    Log.Error($"[{MintTwoColMod.HarmonyId}] New group button failed: {exception}");
                }
            }
        }

        if (!headerButtonVisible)
        {
            return;
        }
        headerButtonVisible = false;

        try
        {
            CategoryRailDrawer.DrawHiddenToggle(headerButtonRect);
        }
        catch (Exception exception)
        {
            if (!reportedHeaderButtonFailure)
            {
                reportedHeaderButtonFailure = true;
                Log.Error($"[{MintTwoColMod.HarmonyId}] Hidden rail button failed: {exception}");
            }
        }
    }
}
