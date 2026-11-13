using System;
using System.Collections.Generic;
using Verse;

namespace MintTwoCol;

public enum RailGroupKind
{
    Items,
    Categories
}

// 用户自定义组：Items 组装建筑项 defName，Categories 组装分类 defName；固定两级，不再嵌套。
public sealed class RailGroup : IExposable
{
    public string Id;
    public string Name;
    public RailGroupKind Kind;
    public bool OnLeft = true;
    public List<string> Members = new List<string>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id");
        Scribe_Values.Look(ref Name, "name");
        Scribe_Values.Look(ref Kind, "kind", RailGroupKind.Items);
        Scribe_Values.Look(ref OnLeft, "onLeft", true);
        Scribe_Collections.Look(ref Members, "members", LookMode.Value, Array.Empty<object>());
        if (Members == null)
        {
            Members = new List<string>();
        }
        Members.RemoveAll(string.IsNullOrEmpty);
        if (Name == null)
        {
            Name = string.Empty;
        }
    }
}

// 栏行节点 ID：分类直接用 defName（兼容旧设置），组与建筑项加前缀。
internal static class RailIds
{
    internal const string GroupPrefix = "grp:";
    internal const string ItemPrefix = "item:";

    internal static string NewGroupId()
    {
        return GroupPrefix + Guid.NewGuid().ToString("N");
    }

    internal static string Item(string defName)
    {
        return ItemPrefix + defName;
    }

    internal static bool IsGroup(string id)
    {
        return id != null && id.StartsWith(GroupPrefix, StringComparison.Ordinal);
    }

    internal static bool IsItem(string id)
    {
        return id != null && id.StartsWith(ItemPrefix, StringComparison.Ordinal);
    }

    internal static bool IsCategory(string id)
    {
        return !string.IsNullOrEmpty(id) && !IsGroup(id) && !IsItem(id);
    }

    internal static string ItemDefName(string id)
    {
        return IsItem(id) ? id.Substring(ItemPrefix.Length) : null;
    }

    internal static RailGroup Find(List<RailGroup> groups, string id)
    {
        if (groups == null || string.IsNullOrEmpty(id))
        {
            return null;
        }
        foreach (RailGroup group in groups)
        {
            if (group != null && string.Equals(group.Id, id, StringComparison.Ordinal))
            {
                return group;
            }
        }
        return null;
    }
}
