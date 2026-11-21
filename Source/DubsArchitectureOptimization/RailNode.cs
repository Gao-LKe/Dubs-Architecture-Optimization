using System;
using System.Collections.Generic;
using DubsMintMenus;
using RimWorld;
using Verse;

namespace DubsArchitectureOptimization;

// 栏行基类：分类、自定义组与组内子行共用拖拽、排序、右键隐藏行为。
internal abstract class RailNode
{
    internal abstract string Id { get; }
    internal abstract string Label { get; }
    internal virtual bool Draggable => true;
    internal virtual bool Hideable => true;
    internal virtual int Indent => 0;
    internal virtual bool IsChild => false;
    internal virtual bool Expandable => false;
    internal virtual string SelectionKey => Id;
    internal abstract void Select(MainTabWindow_MintArchitect window);
    internal abstract void ApplySide(bool targetNative);
    internal virtual IEnumerable<RailNode> Children(MainTabWindow_MintArchitect window) => Array.Empty<RailNode>();
}

internal sealed class CategoryNode : RailNode
{
    internal readonly ArchitectCategoryTab Tab;
    private readonly int indent;
    private readonly string scope;

    internal CategoryNode(ArchitectCategoryTab tab, int indent = 0, string scope = null)
    {
        Tab = tab;
        this.indent = indent;
        this.scope = scope;
    }

    internal override string Id => Tab?.def?.defName;

    internal string ScopeId => scope;

    internal override string Label => Tab?.def?.LabelCap.ToString() ?? string.Empty;

    internal override int Indent => indent;

    internal override bool IsChild => indent > 0;

    internal override string SelectionKey => string.IsNullOrEmpty(scope) ? Id : scope + "|" + Id;

    internal override void Select(MainTabWindow_MintArchitect window)
    {
        CategoryRailDrawer.ClearGroupSelection();
        MintBridge.SetSelected(window, Tab);
    }

    internal override void ApplySide(bool targetNative)
    {
        string id = Id;
        List<string> forceLeft = MintTwoColMod.Settings.ForceLeft;
        List<string> forceRight = MintTwoColMod.Settings.ForceRight;
        if (targetNative == MintBridge.IsNative(Tab))
        {
            forceLeft.Remove(id);
            forceRight.Remove(id);
            return;
        }
        if (targetNative)
        {
            if (!forceLeft.Contains(id))
            {
                forceLeft.Add(id);
            }
            forceRight.Remove(id);
        }
        else
        {
            if (!forceRight.Contains(id))
            {
                forceRight.Add(id);
            }
            forceLeft.Remove(id);
        }
    }
}

internal sealed class GroupNode : RailNode
{
    internal readonly RailGroup Group;

    internal GroupNode(RailGroup group)
    {
        Group = group;
    }

    internal override string Id => Group?.Id;

    internal override string Label => Group?.Name ?? string.Empty;

    internal override bool Expandable => Group != null && Group.Kind == RailGroupKind.Categories;

    internal override bool Hideable => false;

    internal override void Select(MainTabWindow_MintArchitect window)
    {
        MintBridge.SetSelected(window, null);
        CategoryRailDrawer.SelectGroup(Group);
    }

    internal override void ApplySide(bool targetNative)
    {
        Group.OnLeft = targetNative;
        // 组侧别由 OnLeft 决定，Force 表只服务于分类，防手工改坏设置时残留。
        MintTwoColMod.Settings.ForceLeft.Remove(Group.Id);
        MintTwoColMod.Settings.ForceRight.Remove(Group.Id);
    }

    internal override IEnumerable<RailNode> Children(MainTabWindow_MintArchitect window)
    {
        return CategoryRailDrawer.ChildrenOf(Group, window);
    }
}

// 项组内的建筑项子行：只用于展示、选择与隐藏，不参与栏侧别。
internal sealed class ItemNode : RailNode
{
    private readonly string defName;
    private readonly Designator designator;
    private readonly string scope;

    internal ItemNode(string defName, Designator designator, string scope)
    {
        this.defName = defName;
        this.designator = designator;
        this.scope = scope;
    }

    internal Designator Designator => designator;

    internal string DefName => defName;

    internal string ScopeId => scope;

    internal override string Id => RailIds.Item(defName);

    internal override string Label
    {
        get
        {
            if (designator != null)
            {
                return designator.Label;
            }
            Def def = (Def)DefDatabase<ThingDef>.GetNamedSilentFail(defName)
                   ?? DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            return def?.LabelCap.ToString() ?? defName;
        }
    }

    internal override int Indent => 1;

    internal override bool IsChild => true;

    internal override bool Draggable => true;

    internal override bool Hideable => true;

    internal override string SelectionKey => scope + "|" + Id;

    internal override void Select(MainTabWindow_MintArchitect window)
    {
        MintBridge.SetSelected(window, null);
        CategoryRailDrawer.SelectItem(this, window);
    }

    internal override void ApplySide(bool targetNative)
    {
    }
}
