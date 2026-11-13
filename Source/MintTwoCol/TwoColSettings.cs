using System;
using System.Collections.Generic;
using Verse;

namespace MintTwoCol;

public sealed class TwoColSettings : ModSettings
{
    public List<string> LeftOrder = new List<string>();
    public List<string> RightOrder = new List<string>();
    public List<string> Hidden = new List<string>();
    // 手动跨栏移动的分类：ForceLeft 内为原生但放到左栏，ForceRight 内为模组但放到右栏。
    public List<string> ForceLeft = new List<string>();
    public List<string> ForceRight = new List<string>();
    public bool ShowHiddenRail;
    public bool DebugLogging;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref LeftOrder, "leftOrder", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref RightOrder, "rightOrder", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref Hidden, "hidden", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref ForceLeft, "forceLeft", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref ForceRight, "forceRight", LookMode.Value, Array.Empty<object>());
        Scribe_Values.Look(ref ShowHiddenRail, "showHiddenRail", false);
        Scribe_Values.Look(ref DebugLogging, "debugLogging", false);
        if (LeftOrder == null)
        {
            LeftOrder = new List<string>();
        }
        if (RightOrder == null)
        {
            RightOrder = new List<string>();
        }
        if (Hidden == null)
        {
            Hidden = new List<string>();
        }
        if (ForceLeft == null)
        {
            ForceLeft = new List<string>();
        }
        if (ForceRight == null)
        {
            ForceRight = new List<string>();
        }
        LeftOrder.RemoveAll(string.IsNullOrEmpty);
        RightOrder.RemoveAll(string.IsNullOrEmpty);
        Hidden.RemoveAll(string.IsNullOrEmpty);
        ForceLeft.RemoveAll(string.IsNullOrEmpty);
        ForceRight.RemoveAll(string.IsNullOrEmpty);
        // 两表同时包含同名时以左栏为准，避免设置被手工改坏后侧别不确定。
        ForceRight.RemoveAll(name => ForceLeft.Contains(name));
    }
}
