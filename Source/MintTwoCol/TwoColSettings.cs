using System;
using System.Collections.Generic;
using Verse;

namespace MintTwoCol;

public sealed class TwoColSettings : ModSettings
{
    public List<string> LeftOrder = new List<string>();
    public List<string> RightOrder = new List<string>();
    public List<string> Hidden = new List<string>();
    public bool ShowHiddenRail;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref LeftOrder, "leftOrder", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref RightOrder, "rightOrder", LookMode.Value, Array.Empty<object>());
        Scribe_Collections.Look(ref Hidden, "hidden", LookMode.Value, Array.Empty<object>());
        Scribe_Values.Look(ref ShowHiddenRail, "showHiddenRail", false);
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
        LeftOrder.RemoveAll(string.IsNullOrEmpty);
        RightOrder.RemoveAll(string.IsNullOrEmpty);
        Hidden.RemoveAll(string.IsNullOrEmpty);
    }
}
