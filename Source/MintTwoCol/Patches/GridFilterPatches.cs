using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MintTwoCol;

// 普通网格过滤器：应用用户隐藏，选中建筑项组时只保留该组成员。
internal static class GridFilterPatches
{
    internal static void Install(Harmony harmony)
    {
        if (MintBridge.GetDesignators == null)
        {
            return;
        }
        HarmonyMethod postfix = new HarmonyMethod(typeof(GridFilterPatches).GetMethod(nameof(FilterDesignators), BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(MintBridge.GetDesignators, postfix: postfix);
    }

    // 用 ref __result 直接改写返回值；只靠 postfix 的返回值在部分 Harmony 版本里不会生效。
    private static void FilterDesignators(ref IEnumerable<Designator> __result)
    {
        if (__result == null)
        {
            return;
        }

        __result = __result.Where(CategoryRailDrawer.ShouldShowInGrid).ToList();
    }
}
