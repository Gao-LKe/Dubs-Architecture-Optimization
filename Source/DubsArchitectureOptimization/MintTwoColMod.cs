using HarmonyLib;
using UnityEngine;
using Verse;

namespace DubsArchitectureOptimization;

public sealed class MintTwoColMod : Mod
{
    public const string HarmonyId = "GK.DubsArchitectureOptimization";

    internal static MintTwoColMod Instance { get; private set; }
    internal static TwoColSettings Settings { get; private set; }
    internal static Harmony Harmony { get; private set; }

    public MintTwoColMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<TwoColSettings>();
        Harmony = new Harmony(HarmonyId);

        LongEventHandler.QueueLongEvent(ApplyPatches, "MintTwoCol.LoadingLabel", false, null);

        Log.Message($"[{HarmonyId}] Loaded.");
    }

    public override string SettingsCategory()
    {
        return "更友好的Dubs建筑菜单";
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled(
            "MintTwoCol.DebugLogging".Translate(),
            ref Settings.DebugLogging,
            "MintTwoCol.DebugLoggingDesc".Translate());
        listing.CheckboxLabeled(
            "MintTwoCol.DisableRightRail".Translate(),
            ref Settings.DisableRightRail,
            "MintTwoCol.DisableRightRailDesc".Translate());
        listing.End();
    }

    private static void ApplyPatches()
    {
        try
        {
            MintBridge.Initialize(Harmony);
        }
        catch (System.Exception exception)
        {
            Log.Error($"[{HarmonyId}] Patch setup failed, Dubs Mint Menus stays unchanged: {exception}");
        }
    }
}
