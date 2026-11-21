using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace DubsArchitectureOptimization;

// 两栏顺序完全由本 Mod 管理，Mint 的 A-Z 排序与置顶按钮点击后不影响行序，直接隐藏。
internal static class UiCleanupPatches
{
    private static bool drawingArchitectWindow;
    private static bool reportedAzFailure;
    private static Texture2D azTexture;

    // 隐藏按钮是可选的清理项：任一步失败只跳过该步，不影响双栏、拖拽、隐藏栏等主功能。
    internal static void Install(Harmony harmony)
    {
        if (MintBridge.DoWindowContents != null)
        {
            TryInstall("A-Z window flag", () => harmony.Patch(
                MintBridge.DoWindowContents,
                prefix: GetHarmonyMethod(nameof(EnterArchitectWindowPrefix)),
                postfix: GetHarmonyMethod(nameof(ExitArchitectWindowPostfix))));
        }
        else
        {
            Log.Warning($"[{MintTwoColMod.HarmonyId}] DoWindowContents not found, A-Z button stays visible.");
        }

        if (MintBridge.PinButton != null)
        {
            TryInstall("pin buttons", () => harmony.Patch(
                MintBridge.PinButton,
                prefix: GetHarmonyMethod(nameof(PinButtonPrefix))));
        }
        else
        {
            Log.Warning($"[{MintTwoColMod.HarmonyId}] PinButton not found, pin buttons stay visible.");
        }

        if (MintBridge.ButtonImageFitted != null && MintBridge.AzTextureField != null)
        {
            TryInstall("A-Z button", () => harmony.Patch(
                MintBridge.ButtonImageFitted,
                prefix: GetHarmonyMethod(nameof(HideAzButtonPrefix))));
        }
        else
        {
            Log.Warning($"[{MintTwoColMod.HarmonyId}] A-Z button target not found, it stays visible.");
        }
    }

    private static void TryInstall(string what, Action patch)
    {
        try
        {
            patch();
        }
        catch (Exception exception)
        {
            Log.Warning($"[{MintTwoColMod.HarmonyId}] Optional UI cleanup skipped ({what}), that button stays visible: {exception}");
        }
    }

    private static HarmonyMethod GetHarmonyMethod(string name)
    {
        MethodInfo method = typeof(UiCleanupPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        return new HarmonyMethod(method);
    }

    private static void EnterArchitectWindowPrefix()
    {
        drawingArchitectWindow = true;
    }

    private static void ExitArchitectWindowPostfix()
    {
        drawingArchitectWindow = false;
    }

    private static bool PinButtonPrefix()
    {
        return false;
    }

    private static bool HideAzButtonPrefix(Texture2D tex)
    {
        try
        {
            if (!drawingArchitectWindow)
            {
                return true;
            }
            if (azTexture == null)
            {
                azTexture = MintBridge.AzTextureField?.GetValue(null) as Texture2D;
            }
            return azTexture == null || tex != azTexture;
        }
        catch (Exception exception)
        {
            if (!reportedAzFailure)
            {
                reportedAzFailure = true;
                Log.Warning($"[{MintTwoColMod.HarmonyId}] A-Z hiding disabled, button stays visible: {exception}");
            }
            return true;
        }
    }
}
