using System;
using System.Collections.Generic;
using System.Linq;
using DubsMintMenus;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MintTwoCol;

internal static class CategoryRailDrawer
{
    internal const float RowHeight = 30f;
    internal const float RightRailWidth = 200f;
    internal const float RailGap = 8f;
    internal const float HeaderHeight = 32f;
    internal const float MinGridWidth = 200f;
    internal const float HeaderButtonWidth = 30f;
    internal const float HeaderButtonSize = 24f;
    // 留出排序按钮、视图切换按钮与标题的最小空间，低于此值不画隐藏按钮。
    internal const float MinHeaderWidthForButton = 110f;

    private const float DragThresholdSqr = 36f;

    private static int loggedTarget = -2;

    // 调试开关：需同时开启 RimWorld 开发者模式与本 Mod 设置里的调试日志。
    private static bool DebugOn =>
        Prefs.DevMode && MintTwoColMod.Settings != null && MintTwoColMod.Settings.DebugLogging;

    private static void Dbg(string message)
    {
        if (DebugOn)
        {
            Log.Message($"[{MintTwoColMod.HarmonyId}/dbg] {message}");
        }
    }

    private static Vector2 leftScroll;
    private static Vector2 rightScroll;
    private static Vector2 hiddenScroll;

    private static Texture2D hiddenIcon;
    private static bool hiddenIconResolved;

    private static bool pressActive;
    private static ArchitectCategoryTab pressTab;
    private static bool pressNative;
    private static bool pressDraggable;
    private static Vector2 pressPosition;

    private static bool dragging;
    private static bool dropValid;
    private static bool dropNative;
    private static int dropSlot;

    private static Rect leftPort;
    private static Rect rightPort;
    private static Vector2 leftOrigin;
    private static Vector2 rightOrigin;

    internal static void ResetDrag()
    {
        pressActive = false;
        pressTab = null;
        dragging = false;
        dropValid = false;
        dropSlot = 0;
        loggedTarget = -2;
    }

    private static bool loggedSummary;

    internal static void DrawLeft(MainTabWindow_MintArchitect window, Rect box)
    {
        leftPort = Rect.zero;
        LogSummaryOnce(window);
        DrawRail(window, box, native: true, ref leftScroll);
    }

    private static void LogSummaryOnce(MainTabWindow_MintArchitect window)
    {
        if (loggedSummary)
        {
            return;
        }
        loggedSummary = true;

        List<ArchitectCategoryTab> all = MintBridge.GetCategories(window);
        if (all == null)
        {
            Log.Warning($"[{MintTwoColMod.HarmonyId}] Category cache is null.");
            return;
        }

        int nativeCount = all.Count(tab => tab != null && MintBridge.IsNative(tab));
        int moddedCount = all.Count(tab => tab != null && !MintBridge.IsNative(tab));
        Log.Message($"[{MintTwoColMod.HarmonyId}] Categories: native={nativeCount}, modded={moddedCount}.");
        foreach (IGrouping<string, ArchitectCategoryTab> group in all
                     .Where(tab => tab != null)
                     .GroupBy(tab => tab.def?.modContentPack?.PackageId ?? "null")
                     .OrderByDescending(group => group.Count())
                     .Take(12))
        {
            Log.Message($"[{MintTwoColMod.HarmonyId}]   {group.Key}: {group.Count()} ({string.Join(", ", group.Take(3).Select(tab => tab.def.defName))})");
        }
    }

    internal static void DrawRight(MainTabWindow_MintArchitect window, Rect box)
    {
        rightPort = Rect.zero;
        DrawRail(window, box, native: false, ref rightScroll);
    }

    internal static void DrawHidden(MainTabWindow_MintArchitect window, Rect box)
    {
        Text.Font = GameFont.Small;
        Widgets.DrawMenuSection(box);
        Rect inner = box.ContractedBy(4f);
        if (inner.width < 20f || inner.height < 10f)
        {
            return;
        }

        List<ArchitectCategoryTab> rows = HiddenRows(window);
        if (rows.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(inner, Translator.Translate("MintTwoCol.NoHiddenCategories"));
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        Rect viewRect = new Rect(0f, 0f, inner.width - 16f, RowHeight * rows.Count);
        GUI.BeginGroup(inner);
        try
        {
            Widgets.BeginScrollView(new Rect(0f, 0f, inner.width, inner.height), ref hiddenScroll, viewRect, true);
            try
            {
                GUI.BeginGroup(viewRect);
                try
                {
                    float y = 0f;
                    foreach (ArchitectCategoryTab tab in rows)
                    {
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), tab, false);
                        y += RowHeight;
                    }

                    HandleHiddenInput(viewRect, rows);
                }
                finally
                {
                    GUI.EndGroup();
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }
        finally
        {
            GUI.EndGroup();
        }
    }

    private static void HandleHiddenInput(Rect viewRect, List<ArchitectCategoryTab> rows)
    {
        Event current = Event.current;
        if (current == null
            || current.type != EventType.MouseDown
            || current.button != 0
            || !viewRect.Contains(current.mousePosition))
        {
            return;
        }

        int index = Mathf.FloorToInt(current.mousePosition.y / RowHeight);
        if (index < 0 || index >= rows.Count)
        {
            return;
        }

        SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, (Map)null);
        Restore(rows[index]);
        current.Use();
    }

    private static void Restore(ArchitectCategoryTab tab)
    {
        string defName = tab?.def?.defName;
        if (string.IsNullOrEmpty(defName))
        {
            return;
        }
        if (MintTwoColMod.Settings.Hidden.Remove(defName))
        {
            MintTwoColMod.Settings.Write();
        }
    }

    private static List<ArchitectCategoryTab> HiddenRows(MainTabWindow_MintArchitect window)
    {
        List<string> hidden = MintTwoColMod.Settings.Hidden;
        List<ArchitectCategoryTab> result = new List<ArchitectCategoryTab>();
        if (hidden.Count == 0)
        {
            return result;
        }

        List<ArchitectCategoryTab> all = MintBridge.GetCategories(window);
        if (all == null)
        {
            return result;
        }

        Dictionary<string, ArchitectCategoryTab> byName = new Dictionary<string, ArchitectCategoryTab>(StringComparer.Ordinal);
        foreach (ArchitectCategoryTab tab in all)
        {
            if (tab?.def?.defName != null)
            {
                byName[tab.def.defName] = tab;
            }
        }
        foreach (string defName in hidden)
        {
            if (string.IsNullOrEmpty(defName))
            {
                continue;
            }
            if (byName.TryGetValue(defName, out ArchitectCategoryTab tab) && tab.Visible)
            {
                result.Add(tab);
            }
        }
        return result;
    }

    internal static Rect HeaderButtonRect(Rect header)
    {
        return new Rect(
            header.xMax + (HeaderButtonWidth - HeaderButtonSize) / 2f,
            header.y + 2f,
            HeaderButtonSize,
            HeaderButtonSize);
    }

    internal static void DrawHiddenToggle(Rect rect)
    {
        if (MintTwoColMod.Settings.ShowHiddenRail)
        {
            Widgets.DrawHighlight(rect);
        }
        if (Widgets.ButtonImage(rect, HiddenIcon()))
        {
            MintTwoColMod.Settings.ShowHiddenRail = !MintTwoColMod.Settings.ShowHiddenRail;
            MintTwoColMod.Settings.Write();
        }
        TooltipHandler.TipRegion(rect, Translator.Translate("MintTwoCol.HiddenRailToggle"));
    }

    private static Texture2D HiddenIcon()
    {
        if (!hiddenIconResolved)
        {
            hiddenIconResolved = true;
            hiddenIcon = ContentFinder<Texture2D>.Get("UI/HiddenRailIcon", false) ?? TexButton.Minus;
        }
        return hiddenIcon;
    }

    private static void DrawRail(MainTabWindow_MintArchitect window, Rect box, bool native, ref Vector2 scroll)
    {
        Text.Font = GameFont.Small;
        Widgets.DrawMenuSection(box);
        Rect inner = box.ContractedBy(4f);
        if (inner.width < 20f || inner.height < 10f)
        {
            return;
        }

        // 端口去掉 16px 滚动条；origin 是内容坐标原点在窗口坐标中的位置。
        Rect port = new Rect(inner.x, inner.y, Mathf.Max(0f, inner.width - 16f), inner.height);
        if (native)
        {
            leftPort = port;
            leftOrigin = inner.position - scroll;
        }
        else
        {
            rightPort = port;
            rightOrigin = inner.position - scroll;
        }

        List<ArchitectCategoryTab> rows = Ordered(window, native);
        ArchitectCategoryTab selected = MintBridge.GetSelected(window);
        int leadingRows = native ? 1 : 0;
        float contentHeight = RowHeight * (rows.Count + leadingRows);
        Rect viewRect = new Rect(0f, 0f, inner.width - 16f, contentHeight);

        GUI.BeginGroup(inner);
        try
        {
            Widgets.BeginScrollView(new Rect(0f, 0f, inner.width, inner.height), ref scroll, viewRect, true);
            try
            {
                GUI.BeginGroup(viewRect);
                try
                {
                    float y = 0f;
                    if (native)
                    {
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), null, selected == null);
                        y += RowHeight;
                    }
                    foreach (ArchitectCategoryTab tab in rows)
                    {
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), tab, selected == tab);
                        y += RowHeight;
                    }

                    HandleInput(window, viewRect, native, rows);

                    if (dragging && dropValid && dropNative == native)
                    {
                        int lineSlot = Mathf.Clamp(dropSlot, 0, rows.Count);
                        Widgets.DrawLineHorizontal(0f, RowHeight * (lineSlot + leadingRows), viewRect.width);
                    }
                }
                finally
                {
                    GUI.EndGroup();
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }
        finally
        {
            GUI.EndGroup();
        }
    }

    private static void DrawRow(Rect rect, ArchitectCategoryTab tab, bool selected)
    {
        if (Mouse.IsOver(rect))
        {
            Widgets.DrawHighlight(rect);
        }
        else if (selected)
        {
            Widgets.DrawHighlightSelected(rect);
        }

        string label = tab == null ? Translator.Translate("All").CapitalizeFirst() : tab.def.LabelCap;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(rect.ContractedBy(6f, 0f), label);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void HandleInput(MainTabWindow_MintArchitect window, Rect viewRect, bool native, List<ArchitectCategoryTab> rows)
    {
        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        // Unity 的 GUI.BeginScrollView 在鼠标移出滚动区域后会把 MouseDrag/MouseUp 改写成 Ignore，
        // 拖拽期间按 rawType 还原，否则跨栏拖动离开源栏后收不到事件。
        EventType type = current.type;
        if (type == EventType.Ignore
            && pressActive
            && (current.rawType == EventType.MouseDrag || current.rawType == EventType.MouseUp))
        {
            type = current.rawType;
        }

        if ((pressActive || dragging) && type != EventType.Repaint && type != EventType.Layout)
        {
            Dbg($"Input type={type} raw={current.rawType} button={current.button} pos={current.mousePosition} native={native} pressNative={pressNative} draggable={pressDraggable} dragging={dragging} target={loggedTarget}");
        }

        switch (type)
        {
            case EventType.MouseDown:
                if (!viewRect.Contains(current.mousePosition) || pressActive)
                {
                    return;
                }
                if (current.button == 1)
                {
                    int rightClickIndex = Mathf.FloorToInt(current.mousePosition.y / RowHeight) - (native ? 1 : 0);
                    if (rightClickIndex >= 0 && rightClickIndex < rows.Count)
                    {
                        Hide(window, rows[rightClickIndex]);
                        current.Use();
                    }
                    return;
                }
                if (current.button != 0)
                {
                    return;
                }
                {
                    int rowIndex = Mathf.FloorToInt(current.mousePosition.y / RowHeight);
                    int totalRows = rows.Count + (native ? 1 : 0);
                    if (rowIndex < 0 || rowIndex >= totalRows)
                    {
                        return;
                    }
                    int tabIndex = rowIndex - (native ? 1 : 0);
                    ArchitectCategoryTab tab = tabIndex >= 0 && tabIndex < rows.Count ? rows[tabIndex] : null;
                    pressActive = true;
                    pressTab = tab;
                    pressNative = native;
                    pressDraggable = tab != null;
                    pressPosition = current.mousePosition;
                    Dbg($"MouseDown native={native} row={rowIndex} tab={tab?.def?.defName ?? "All"} draggable={pressDraggable} click={current.mousePosition}");
                    current.Use();
                }
                break;

            case EventType.MouseDrag:
            {
                if (!pressActive || pressNative != native || !pressDraggable)
                {
                    return;
                }
                if (!dragging)
                {
                    if ((current.mousePosition - pressPosition).sqrMagnitude <= DragThresholdSqr)
                    {
                        return;
                    }
                    dragging = true;
                    dropNative = native;
                    Dbg($"DragStart native={native} pressPos={pressPosition} click={current.mousePosition} origin={OriginFor(native)}");
                }

                Vector2 windowMouse = OriginFor(native) + current.mousePosition;
                bool hasRail = TryGetRailAt(windowMouse, out bool targetNative);
                int targetCode = !hasRail ? -1 : (targetNative ? 1 : 0);
                if (targetCode != loggedTarget)
                {
                    loggedTarget = targetCode;
                    Dbg($"DragTarget native={native} click={current.mousePosition} windowMouse={windowMouse} origin={OriginFor(native)} leftPort={leftPort} rightPort={rightPort} target={targetCode} frame={Time.frameCount}");
                }
                if (!hasRail)
                {
                    dropValid = false;
                    current.Use();
                    return;
                }

                if (targetNative != native)
                {
                    // 进入另一栏即移交：写侧别与目标栏顺序，之后按同栏拖拽继续。
                    dropSlot = SlotFor(targetNative, windowMouse);
                    ApplyCrossMove(window, pressTab, targetNative, dropSlot);
                    pressNative = targetNative;
                }

                dropNative = targetNative;
                dropSlot = SlotFor(targetNative, windowMouse);
                dropValid = true;
                current.Use();
                break;
            }

            case EventType.MouseUp:
            {
                if (!pressActive || pressNative != native)
                {
                    return;
                }
                if (dragging)
                {
                    Vector2 windowMouse = OriginFor(native) + current.mousePosition;
                    bool hasRail = TryGetRailAt(windowMouse, out bool targetNative);
                    int slot = SlotFor(native, windowMouse);
                    Dbg($"MouseUp native={native} click={current.mousePosition} windowMouse={windowMouse} dropValid={dropValid} hasRail={hasRail} target={targetNative} slot={slot} rows={rows.Count}");
                    if (dropValid && hasRail && targetNative == native && rows.Count > 0)
                    {
                        ApplyReorder(native, rows, pressTab, slot);
                    }
                }
                else
                {
                    SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, (Map)null);
                    MintBridge.SetSelected(window, pressTab);
                }
                ResetDrag();
                current.Use();
                break;
            }
        }
    }

    private static void Hide(MainTabWindow_MintArchitect window, ArchitectCategoryTab tab)
    {
        string defName = tab?.def?.defName;
        if (string.IsNullOrEmpty(defName))
        {
            return;
        }
        if (!MintTwoColMod.Settings.Hidden.Contains(defName))
        {
            MintTwoColMod.Settings.Hidden.Add(defName);
        }
        if (MintBridge.GetSelected(window) == tab)
        {
            MintBridge.SetSelected(window, null);
        }
        ResetDrag();
        MintTwoColMod.Settings.Write();
    }

    private static Vector2 OriginFor(bool native)
    {
        return native ? leftOrigin : rightOrigin;
    }

    private static int SlotFor(bool native, Vector2 windowMouse)
    {
        // 最近的行边界：行上半区插到该行之前，下半区插到该行之后。
        return Mathf.RoundToInt((windowMouse.y - OriginFor(native).y) / RowHeight) - (native ? 1 : 0);
    }

    private static bool TryGetRailAt(Vector2 windowMouse, out bool targetNative)
    {
        if (leftPort.Contains(windowMouse))
        {
            targetNative = true;
            return true;
        }
        if (rightPort.Contains(windowMouse))
        {
            targetNative = false;
            return true;
        }
        targetNative = false;
        return false;
    }

    private static void ApplyCrossMove(MainTabWindow_MintArchitect window, ArchitectCategoryTab moved, bool targetNative, int slot)
    {
        string defName = moved?.def?.defName;
        if (string.IsNullOrEmpty(defName))
        {
            return;
        }
        Dbg($"CrossMove {defName} to {(targetNative ? "left" : "right")} slot={slot}");

        // 复用同栏重排的写回路径：目标栏当前可见顺序 + 落点插入，再由 MergeOrder 保留隐藏/失效槽位。
        List<string> visible = Ordered(window, targetNative).Select(tab => tab.def.defName).ToList();
        visible.Remove(defName);
        visible.Insert(Mathf.Clamp(slot, 0, visible.Count), defName);

        ApplyForceSide(moved, targetNative);
        MergeOrder(targetNative ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder, visible);
        MintTwoColMod.Settings.Write();
    }

    private static void ApplyForceSide(ArchitectCategoryTab tab, bool targetNative)
    {
        string defName = tab.def.defName;
        List<string> forceLeft = MintTwoColMod.Settings.ForceLeft;
        List<string> forceRight = MintTwoColMod.Settings.ForceRight;
        if (targetNative == MintBridge.IsNative(tab))
        {
            forceLeft.Remove(defName);
            forceRight.Remove(defName);
            return;
        }
        if (targetNative)
        {
            if (!forceLeft.Contains(defName))
            {
                forceLeft.Add(defName);
            }
            forceRight.Remove(defName);
        }
        else
        {
            if (!forceRight.Contains(defName))
            {
                forceRight.Add(defName);
            }
            forceLeft.Remove(defName);
        }
    }

    private static void ApplyReorder(bool native, List<ArchitectCategoryTab> rows, ArchitectCategoryTab moved, int slot)
    {
        if (moved == null)
        {
            return;
        }

        int from = rows.IndexOf(moved);
        if (from < 0)
        {
            return;
        }
        rows.RemoveAt(from);
        if (from < slot)
        {
            slot--;
        }
        slot = Mathf.Clamp(slot, 0, rows.Count);
        rows.Insert(slot, moved);

        Dbg($"Reorder native={native} {moved.def.defName} from={from} slot={slot}");
        List<string> order = native ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder;
        MergeOrder(order, rows.Select(tab => tab.def.defName).ToList());
        MintTwoColMod.Settings.Write();
    }

    // 只重排可见项，隐藏项与失效条目保留在原槽位，隐藏后恢复仍回原位置。
    private static void MergeOrder(List<string> order, List<string> visibleNames)
    {
        HashSet<string> visible = new HashSet<string>(visibleNames, StringComparer.Ordinal);
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        Queue<string> pending = new Queue<string>(visibleNames);
        List<string> merged = new List<string>(order.Count + visibleNames.Count);
        foreach (string name in order)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            if (visible.Contains(name))
            {
                if (seen.Add(name) && pending.Count > 0)
                {
                    merged.Add(pending.Dequeue());
                }
            }
            else
            {
                merged.Add(name);
            }
        }
        while (pending.Count > 0)
        {
            merged.Add(pending.Dequeue());
        }
        order.Clear();
        order.AddRange(merged);
    }

    internal static List<ArchitectCategoryTab> Ordered(MainTabWindow_MintArchitect window, bool native)
    {
        List<ArchitectCategoryTab> all = MintBridge.GetCategories(window);
        if (all == null)
        {
            return new List<ArchitectCategoryTab>();
        }

        List<ArchitectCategoryTab> filtered = all
            .Where(tab => tab != null && tab.Visible && IsOnLeft(tab) == native && !IsHidden(tab))
            .OrderByDescending(tab => tab.def.order)
            .ThenBy(tab => tab.def.defName, StringComparer.Ordinal)
            .ToList();
        List<string> order = native ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder;
        if (order.Count == 0)
        {
            return filtered;
        }

        Dictionary<string, int> index = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
        {
            if (!string.IsNullOrEmpty(order[i]))
            {
                index[order[i]] = i;
            }
        }
        return filtered.OrderBy(tab => index.TryGetValue(tab.def.defName, out int position) ? position : int.MaxValue).ToList();
    }

    private static bool IsOnLeft(ArchitectCategoryTab tab)
    {
        string defName = tab?.def?.defName;
        if (!string.IsNullOrEmpty(defName))
        {
            if (MintTwoColMod.Settings.ForceLeft.Contains(defName))
            {
                return true;
            }
            if (MintTwoColMod.Settings.ForceRight.Contains(defName))
            {
                return false;
            }
        }
        return MintBridge.IsNative(tab);
    }

    private static bool IsHidden(ArchitectCategoryTab tab)
    {
        string defName = tab?.def?.defName;
        return !string.IsNullOrEmpty(defName) && MintTwoColMod.Settings.Hidden.Contains(defName);
    }
}
