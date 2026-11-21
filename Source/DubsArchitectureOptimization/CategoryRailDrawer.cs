using System;
using System.Collections.Generic;
using System.Linq;
using DubsMintMenus;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DubsArchitectureOptimization;

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

    private static RailGroup selectedGroup;
    private static string selectedItemKey;

    private static readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.Ordinal);

    internal static void SelectGroup(RailGroup group)
    {
        selectedGroup = group;
        selectedItemKey = null;
    }

    internal static void SelectItem(ItemNode item, MainTabWindow_MintArchitect window)
    {
        selectedItemKey = item?.SelectionKey;
        selectedGroup = item == null ? null : RailIds.Find(MintTwoColMod.Settings.Groups, item.ScopeId);
    }

    internal static void ClearGroupSelection()
    {
        selectedGroup = null;
        selectedItemKey = null;
    }

    private static bool IsExpanded(string id)
    {
        return !string.IsNullOrEmpty(id) && expandedGroups.Contains(id);
    }

    private static void ToggleExpanded(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }
        if (!expandedGroups.Remove(id))
        {
            expandedGroups.Add(id);
        }
    }

    private static Texture2D hiddenIcon;
    private static bool hiddenIconResolved;
    private static Texture2D groupExpandedIcon;
    private static Texture2D groupCollapsedIcon;
    private static bool groupIconsResolved;
    private static Texture2D groupItemIcon;
    private static bool groupItemIconResolved;

    private static bool pressActive;
    private static RailNode pressNode;
    private static bool pressNative;
    private static bool pressDraggable;
    private static Vector2 pressPosition;

    private static bool dragging;
    private static bool dropValid;
    private static bool dropNative;
    private static int dropSlot;
    private static int childLineDisplay = -1;
    private static bool childLineNative;

    private static Rect leftPort;
    private static Rect rightPort;
    private static Vector2 leftOrigin;
    private static Vector2 rightOrigin;
    private static List<RailNode> leftDisplay;
    private static List<RailNode> rightDisplay;

    internal static void ResetDrag()
    {
        pressActive = false;
        pressNode = null;
        dragging = false;
        dropValid = false;
        dropSlot = 0;
        childLineDisplay = -1;
        loggedTarget = -2;
    }

    private static bool loggedSummary;

    internal static void DrawLeft(MainTabWindow_MintArchitect window, Rect box)
    {
        leftPort = Rect.zero;
        // 左栏先于右栏绘制，左栏的拖拽命中要沿用上一帧的右栏端口，不能每帧清零；
        // 只有关闭右栏时右栏端口才需要作废。
        if (MintTwoColMod.Settings.DisableRightRail)
        {
            rightPort = Rect.zero;
        }
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

        List<RailNode> rows = HiddenRows(window);
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
                    foreach (RailNode node in rows)
                    {
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), node, false);
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

    private static void HandleHiddenInput(Rect viewRect, List<RailNode> rows)
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

    private static void Restore(RailNode node)
    {
        string id = node?.Id;
        if (string.IsNullOrEmpty(id))
        {
            return;
        }
        if (MintTwoColMod.Settings.Hidden.Remove(id))
        {
            MintTwoColMod.Settings.Write();
        }
    }

    private static List<RailNode> HiddenRows(MainTabWindow_MintArchitect window)
    {
        List<string> hidden = MintTwoColMod.Settings.Hidden;
        List<RailNode> result = new List<RailNode>();
        if (hidden.Count == 0)
        {
            return result;
        }

        List<ArchitectCategoryTab> all = MintBridge.GetCategories(window);
        Dictionary<string, ArchitectCategoryTab> byName = new Dictionary<string, ArchitectCategoryTab>(StringComparer.Ordinal);
        if (all != null)
        {
            foreach (ArchitectCategoryTab tab in all)
            {
                if (tab?.def?.defName != null)
                {
                    byName[tab.def.defName] = tab;
                }
            }
        }
        foreach (string id in hidden)
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }
            RailGroup group = RailIds.Find(MintTwoColMod.Settings.Groups, id);
            if (group != null)
            {
                result.Add(new GroupNode(group));
                continue;
            }
            if (RailIds.IsItem(id))
            {
                string defName = RailIds.ItemDefName(id);
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) != null)
                {
                    result.Add(new ItemNode(defName, null, null));
                }
                continue;
            }
            if (byName.TryGetValue(id, out ArchitectCategoryTab tab) && tab.Visible)
            {
                result.Add(new CategoryNode(tab));
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

    private static Texture2D GroupIcon(bool expanded)
    {
        if (!groupIconsResolved)
        {
            groupIconsResolved = true;
            groupExpandedIcon = ContentFinder<Texture2D>.Get("UI/DubsArchitectureOptimizationArrowDown", false) ?? TexButton.Minus;
            groupCollapsedIcon = ContentFinder<Texture2D>.Get("UI/DubsArchitectureOptimizationArrowRight", false) ?? TexButton.Plus;
        }
        return expanded ? groupExpandedIcon : groupCollapsedIcon;
    }

    private static Texture2D GroupItemIcon()
    {
        if (!groupItemIconResolved)
        {
            groupItemIconResolved = true;
            groupItemIcon = ContentFinder<Texture2D>.Get("UI/DubsArchitectureOptimizationGroup", false) ?? TexButton.Plus;
        }
        return groupItemIcon;
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

        List<RailNode> rows = Ordered(window, native);
        List<RailNode> display = new List<RailNode>();
        List<int> topSlotAtDisplay = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            RailNode node = rows[i];
            topSlotAtDisplay.Add(i);
            display.Add(node);
            if (node.Expandable && IsExpanded(node.Id))
            {
                foreach (RailNode child in node.Children(window))
                {
                    topSlotAtDisplay.Add(i + 1);
                    display.Add(child);
                }
            }
        }
        topSlotAtDisplay.Add(rows.Count);
        if (native)
        {
            leftDisplay = display;
        }
        else
        {
            rightDisplay = display;
        }

        ArchitectCategoryTab selected = MintBridge.GetSelected(window);
        string selectedId = selected?.def?.defName ?? selectedGroup?.Id;
        int leadingRows = native ? 1 : 0;
        float contentHeight = RowHeight * (display.Count + leadingRows);
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
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), null, selectedId == null && selectedItemKey == null);
                        y += RowHeight;
                    }
                    foreach (RailNode node in display)
                    {
                        bool isSelected = node is ItemNode
                            ? node.SelectionKey == selectedItemKey
                            : selectedItemKey == null && node.Id == selectedId;
                        DrawRow(new Rect(0f, y, viewRect.width, RowHeight), node, isSelected, groupMenu: true);
                        y += RowHeight;
                    }

                    HandleInput(window, viewRect, native, rows, display, topSlotAtDisplay);

                    if (dragging && dropValid && dropNative == native && pressNode != null && !pressNode.IsChild)
                    {
                        int lineSlot = LineDisplayIndex(topSlotAtDisplay, dropSlot, display.Count);
                        Widgets.DrawLineHorizontal(0f, RowHeight * (lineSlot + leadingRows), viewRect.width);
                    }
                    else if (dragging && childLineDisplay >= 0 && childLineNative == native)
                    {
                        Widgets.DrawLineHorizontal(0f, RowHeight * (childLineDisplay + leadingRows), viewRect.width);
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

    private static void DrawRow(Rect rect, RailNode node, bool selected, bool groupMenu = false)
    {
        if (Mouse.IsOver(rect))
        {
            Widgets.DrawHighlight(rect);
        }
        else if (selected)
        {
            Widgets.DrawHighlightSelected(rect);
        }

        float x = rect.x + 6f;
        if (node is GroupNode groupNode && groupMenu)
        {
            Rect icon = GroupArrowRect(rect);
            if (groupNode.Expandable)
            {
                if (Widgets.ButtonImage(icon, GroupIcon(IsExpanded(node.Id))))
                {
                    ToggleExpanded(node.Id);
                }
            }
            else
            {
                Widgets.DrawTextureFitted(icon, GroupItemIcon(), 1f);
            }
            x = icon.xMax + 2f;
        }
        else if (node != null && node.Indent > 0)
        {
            x += node.Indent * 16f;
        }

        Rect labelRect = new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x - 6f), rect.height);

        string label = node == null ? Translator.Translate("All").CapitalizeFirst() : node.Label;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, label);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static Rect GroupArrowRect(Rect row)
    {
        const float size = 18f;
        return new Rect(row.x + 5f, row.y + (row.height - size) / 2f, size, size);
    }

    private static void HandleInput(
        MainTabWindow_MintArchitect window,
        Rect viewRect,
        bool native,
        List<RailNode> rows,
        List<RailNode> display,
        List<int> topSlotAtDisplay)
    {        Event current = Event.current;
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
                    if (rightClickIndex >= 0 && rightClickIndex < display.Count)
                    {
                        RailNode rightNode = display[rightClickIndex];
                        if (rightNode is GroupNode groupNode)
                        {
                            if (groupNode.Group != null)
                            {
                                OpenGroupMenu(groupNode.Group);
                                current.Use();
                            }
                            return;
                        }
                        Hide(window, rightNode);
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
                    int totalRows = display.Count + (native ? 1 : 0);
                    if (rowIndex < 0 || rowIndex >= totalRows)
                    {
                        return;
                    }
                    int tabIndex = rowIndex - (native ? 1 : 0);
                    RailNode node = tabIndex >= 0 && tabIndex < display.Count ? display[tabIndex] : null;
                    if (node is GroupNode pressGroup && pressGroup.Expandable && IsGroupButtonHit(RowRect(viewRect, tabIndex), current.mousePosition))
                    {
                        // 展开箭头由按钮自身处理点击，避免同时开始拖拽。
                        return;
                    }
                    pressActive = true;
                    pressNode = node;
                    pressNative = native;
                    pressDraggable = node != null && node.Draggable;
                    pressPosition = current.mousePosition;
                    Dbg($"MouseDown native={native} row={rowIndex} tab={node?.Id ?? "All"} draggable={pressDraggable} click={current.mousePosition}");
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
                if (pressNode != null && pressNode.IsChild)
                {
                    // 子行只在源栏内重排，离开源栏松手即移出组。
                    dropNative = native;
                    dropValid = hasRail && targetNative == native;
                    dropSlot = dropValid ? SlotForDisplay(native, windowMouse, display, topSlotAtDisplay) : 0;
                    childLineDisplay = dropValid ? ChildLineFor(native, pressNode, display, windowMouse) : -1;
                    childLineNative = native;
                    current.Use();
                    break;
                }
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
                    ApplyCrossMove(window, pressNode, targetNative, dropSlot);
                    pressNative = targetNative;
                }

                dropNative = targetNative;
                dropSlot = targetNative == native
                    ? SlotForDisplay(native, windowMouse, display, topSlotAtDisplay)
                    : SlotFor(targetNative, windowMouse);
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
                    if (pressNode != null && pressNode.IsChild)
                    {
                        HandleChildDrop(native, pressNode, windowMouse, hasRail, targetNative);
                    }
                    else if (pressNode is CategoryNode category
                             && !category.IsChild
                             && hasRail
                             && TryGroupDrop(category, windowMouse))
                    {
                        // 分类拖进类别组：成员隐藏原来的栏行，只在组的子行里出现。
                    }
                    else
                    {
                        int slot = SlotForDisplay(native, windowMouse, display, topSlotAtDisplay);
                        Dbg($"MouseUp native={native} click={current.mousePosition} windowMouse={windowMouse} dropValid={dropValid} hasRail={hasRail} target={targetNative} slot={slot} rows={rows.Count}");
                        if (dropValid && hasRail && targetNative == native && rows.Count > 0)
                        {
                            ApplyReorder(native, rows, pressNode, slot);
                        }
                    }
                }
                else
                {
                    SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, (Map)null);
                    if (pressNode == null)
                    {
                        ClearGroupSelection();
                        MintBridge.SetSelected(window, null);
                    }
                    else if (pressNode is GroupNode clickGroup && clickGroup.Expandable)
                    {
                        // 类别组整行都是展开/折叠开关。
                        ToggleExpanded(clickGroup.Id);
                    }
                    else
                    {
                        pressNode.Select(window);
                    }
                }
                ResetDrag();
                current.Use();
                break;
            }
        }
    }

    private static void Hide(MainTabWindow_MintArchitect window, RailNode node)
    {
        string id = node?.Id;
        if (string.IsNullOrEmpty(id) || !node.Hideable)
        {
            return;
        }
        if (!MintTwoColMod.Settings.Hidden.Contains(id))
        {
            MintTwoColMod.Settings.Hidden.Add(id);
        }
        ArchitectCategoryTab selected = MintBridge.GetSelected(window);
        if (selected != null && selected.def?.defName == id)
        {
            MintBridge.SetSelected(window, null);
        }
        if (selectedGroup != null && selectedGroup.Id == id)
        {
            selectedGroup = null;
        }
        if (selectedItemKey != null && node.SelectionKey == selectedItemKey)
        {
            ClearGroupSelection();
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

    // 鼠标落点先换算到含子行的显示槽位，再映射回顶层 slot，插入线不会落到子行之间。
    private static int SlotForDisplay(bool native, Vector2 windowMouse, List<RailNode> display, List<int> topSlotAtDisplay)
    {
        int displaySlot = Mathf.Clamp(
            Mathf.RoundToInt((windowMouse.y - OriginFor(native).y) / RowHeight) - (native ? 1 : 0),
            0,
            display.Count);
        return topSlotAtDisplay[displaySlot];
    }

    private static int LineDisplayIndex(List<int> topSlotAtDisplay, int topSlot, int displayCount)
    {
        for (int i = topSlotAtDisplay.Count - 1; i >= 0; i--)
        {
            if (topSlotAtDisplay[i] <= topSlot)
            {
                return Mathf.Clamp(i, 0, displayCount);
            }
        }
        return 0;
    }

    private static bool IsGroupButtonHit(Rect row, Vector2 mouse)
    {
        return GroupArrowRect(row).Contains(mouse);
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

    private static string ScopeOf(RailNode node)
    {
        if (node is ItemNode item)
        {
            return item.ScopeId;
        }
        return node is CategoryNode category ? category.ScopeId : null;
    }

    private static string MemberId(RailNode node)
    {
        return node is ItemNode item ? item.DefName : node?.Id;
    }

    private static RailGroup GroupOf(RailNode node)
    {
        string scope = ScopeOf(node);
        return string.IsNullOrEmpty(scope) ? null : RailIds.Find(MintTwoColMod.Settings.Groups, scope);
    }

    private static int DisplayRowAt(bool native, Vector2 windowMouse, List<RailNode> display)
    {
        if (display == null)
        {
            return -1;
        }
        int index = Mathf.FloorToInt((windowMouse.y - OriginFor(native).y) / RowHeight) - (native ? 1 : 0);
        return index >= 0 && index < display.Count ? index : -1;
    }

    // 分类行落到类别组行上：加入该组，原来的栏行因移动隐藏而消失。
    private static bool TryGroupDrop(CategoryNode moved, Vector2 windowMouse)
    {
        if (!TryGetRailAt(windowMouse, out bool native))
        {
            return false;
        }
        List<RailNode> display = native ? leftDisplay : rightDisplay;
        int index = DisplayRowAt(native, windowMouse, display);
        if (index < 0)
        {
            return false;
        }
        if (display[index] is GroupNode node && node.Group != null && node.Group.Kind == RailGroupKind.Categories)
        {
            AddMember(node.Group, moved.Id);
            return true;
        }
        return false;
    }

    // 子行松手：落在本组块内重排，落到同类型的其他组行上换组，其余位置移出组。
    private static void HandleChildDrop(bool native, RailNode node, Vector2 windowMouse, bool hasRail, bool targetNative)
    {
        RailGroup own = GroupOf(node);
        if (own == null)
        {
            return;
        }
        string member = MemberId(node);
        if (!hasRail || targetNative != native)
        {
            RemoveMember(node, own, member);
            return;
        }

        List<RailNode> display = native ? leftDisplay : rightDisplay;
        int rowIndex = DisplayRowAt(native, windowMouse, display);
        int groupIndex = display?.FindIndex(entry => entry is GroupNode group && group.Group == own) ?? -1;
        if (rowIndex >= 0 && groupIndex >= 0)
        {
            int childStart = groupIndex + 1;
            int childCount = 0;
            while (childStart + childCount < display.Count && ScopeOf(display[childStart + childCount]) == own.Id)
            {
                childCount++;
            }

            if (rowIndex >= groupIndex && rowIndex < childStart + childCount)
            {
                int displaySlot = Mathf.Clamp(
                    Mathf.RoundToInt((windowMouse.y - OriginFor(native).y) / RowHeight) - (native ? 1 : 0),
                    0,
                    display.Count);
                ReorderMembers(own, member, Mathf.Clamp(displaySlot - childStart, 0, childCount));
                return;
            }

            if (display[rowIndex] is GroupNode target
                && target.Group != null
                && target.Group != own
                && target.Group.Kind == own.Kind)
            {
                ClearChildSelection(node);
                AddMember(target.Group, member);
                return;
            }
        }

        RemoveMember(node, own, member);
    }

    // 子行拖拽的插入线只在本组块内显示。
    private static int ChildLineFor(bool native, RailNode node, List<RailNode> display, Vector2 windowMouse)
    {
        RailGroup own = GroupOf(node);
        if (own == null || display == null)
        {
            return -1;
        }
        int groupIndex = display.FindIndex(entry => entry is GroupNode group && group.Group == own);
        if (groupIndex < 0)
        {
            return -1;
        }
        int childStart = groupIndex + 1;
        int childCount = 0;
        while (childStart + childCount < display.Count && ScopeOf(display[childStart + childCount]) == own.Id)
        {
            childCount++;
        }
        int displaySlot = Mathf.Clamp(
            Mathf.RoundToInt((windowMouse.y - OriginFor(native).y) / RowHeight) - (native ? 1 : 0),
            0,
            display.Count);
        return displaySlot >= groupIndex && displaySlot <= childStart + childCount
            ? Mathf.Clamp(displaySlot, childStart, childStart + childCount)
            : -1;
    }

    private static void ReorderMembers(RailGroup group, string member, int slot)
    {
        int from = group.Members.IndexOf(member);
        if (from < 0)
        {
            return;
        }
        group.Members.RemoveAt(from);
        if (from < slot)
        {
            slot--;
        }
        slot = Mathf.Clamp(slot, 0, group.Members.Count);
        group.Members.Insert(slot, member);
        MintTwoColMod.Settings.Write();
    }

    private static void RemoveMember(RailNode node, RailGroup group, string member)
    {
        if (!group.Members.Remove(member))
        {
            return;
        }
        ClearChildSelection(node);
        MintTwoColMod.Settings.Write();
    }

    // 同类组之间成员互斥，避免同一分类/建筑项同时出现在两个组。
    internal static void AddMember(RailGroup target, string member)
    {
        if (target == null || string.IsNullOrEmpty(member))
        {
            return;
        }
        bool changed = false;
        foreach (RailGroup group in MintTwoColMod.Settings.Groups)
        {
            if (group != null && group != target && group.Kind == target.Kind && group.Members.Remove(member))
            {
                changed = true;
            }
        }
        if (!target.Members.Contains(member))
        {
            target.Members.Add(member);
            changed = true;
        }
        if (changed)
        {
            MintTwoColMod.Settings.Write();
        }
    }

    private static void ClearChildSelection(RailNode node)
    {
        if (selectedItemKey != null && node.SelectionKey == selectedItemKey)
        {
            selectedItemKey = null;
        }
    }

    private static bool IsCategoryGroupMember(string defName)
    {
        if (string.IsNullOrEmpty(defName))
        {
            return false;
        }
        foreach (RailGroup group in MintTwoColMod.Settings.Groups)
        {
            if (group != null && group.Kind == RailGroupKind.Categories && group.Members.Contains(defName))
            {
                return true;
            }
        }
        return false;
    }

    private static void ApplyCrossMove(MainTabWindow_MintArchitect window, RailNode moved, bool targetNative, int slot)    {
        string id = moved?.Id;
        if (string.IsNullOrEmpty(id))
        {
            return;
        }
        Dbg($"CrossMove {id} to {(targetNative ? "left" : "right")} slot={slot}");

        // 复用同栏重排的写回路径：目标栏当前可见顺序 + 落点插入，再由 MergeOrder 保留隐藏/失效槽位。
        List<string> visible = Ordered(window, targetNative).Select(node => node.Id).ToList();
        visible.Remove(id);
        visible.Insert(Mathf.Clamp(slot, 0, visible.Count), id);

        ApplyForceSide(moved, targetNative);
        MergeOrder(targetNative ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder, visible);
        MintTwoColMod.Settings.Write();
    }

    private static void ApplyForceSide(RailNode node, bool targetNative)
    {
        node.ApplySide(targetNative);
    }

    private static Rect RowRect(Rect viewRect, int rowIndex)
    {
        return new Rect(0f, rowIndex * RowHeight, viewRect.width, RowHeight);
    }

    private static void ApplyReorder(bool native, List<RailNode> rows, RailNode moved, int slot)
    {
        if (moved == null)
        {
            return;
        }

        int from = rows.FindIndex(node => node.Id == moved.Id);
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

        Dbg($"Reorder native={native} {moved.Id} from={from} slot={slot}");
        List<string> order = native ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder;
        MergeOrder(order, rows.Select(node => node.Id).ToList());
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

    internal static List<RailNode> Ordered(MainTabWindow_MintArchitect window, bool native)
    {
        List<ArchitectCategoryTab> all = MintBridge.GetCategories(window);
        if (all == null)
        {
            return new List<RailNode>();
        }

        // 关闭右栏时两栏条目合并到左栏，顺序沿用左栏表再追加右栏表。
        bool mergeSides = native && MintTwoColMod.Settings.DisableRightRail;
        List<RailNode> filtered = all
            .Where(tab => tab?.def?.defName != null
                          && tab.Visible
                          && (mergeSides || IsOnLeft(tab) == native)
                          && !IsHidden(tab)
                          && !IsCategoryGroupMember(tab.def.defName))
            .Select(tab => (RailNode)new CategoryNode(tab))
            .OrderByDescending(node => ((CategoryNode)node).Tab.def.order)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToList();
        foreach (RailGroup group in MintTwoColMod.Settings.Groups)
        {
            if (group == null || string.IsNullOrEmpty(group.Id) || (!mergeSides && group.OnLeft != native) || IsHidden(group.Id))
            {
                continue;
            }
            filtered.Add(new GroupNode(group));
        }
        List<string> order;
        if (mergeSides)
        {
            order = new List<string>(MintTwoColMod.Settings.LeftOrder);
            foreach (string id in MintTwoColMod.Settings.RightOrder)
            {
                if (!string.IsNullOrEmpty(id) && !order.Contains(id))
                {
                    order.Add(id);
                }
            }
        }
        else
        {
            order = native ? MintTwoColMod.Settings.LeftOrder : MintTwoColMod.Settings.RightOrder;
        }
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
        return filtered.OrderBy(node => index.TryGetValue(node.Id, out int position) ? position : int.MaxValue).ToList();
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

    private static bool IsHidden(string id)
    {
        return !string.IsNullOrEmpty(id) && MintTwoColMod.Settings.Hidden.Contains(id);
    }

    internal static IEnumerable<RailNode> ChildrenOf(RailGroup group, MainTabWindow_MintArchitect window)
    {
        List<RailNode> children = new List<RailNode>();
        if (group?.Members == null || group.Kind != RailGroupKind.Categories)
        {
            return children;
        }

        foreach (string member in group.Members)
        {
            ArchitectCategoryTab tab = FindTab(window, member);
            if (tab == null || !tab.Visible || IsHidden(tab))
            {
                continue;
            }
            children.Add(new CategoryNode(tab, 1, group.Id));
        }
        return children;
    }

    // 网格显示规则：选中建筑项组时只显示该组成员（含 Orders/Zone/Floors 等非建筑条目一并排除）；
    // 未选中组时只应用用户隐藏。
    internal static bool ShouldShowInGrid(Designator designator)
    {
        string defName = null;
        if (designator is Designator_Place place
            && place.PlacingDef is ThingDef thingDef
            && !string.IsNullOrEmpty(thingDef.defName))
        {
            defName = thingDef.defName;
        }

        RailGroup group = selectedGroup;
        if (group != null && group.Kind == RailGroupKind.Items)
        {
            return defName != null && group.Members != null && group.Members.Contains(defName);
        }

        return defName == null || !IsHidden(RailIds.Item(defName));
    }

    private static ArchitectCategoryTab FindTab(MainTabWindow_MintArchitect window, string defName)
    {
        if (string.IsNullOrEmpty(defName))
        {
            return null;
        }
        List<ArchitectCategoryTab> tabs = MintBridge.GetCategories(window);
        if (tabs == null)
        {
            return null;
        }
        foreach (ArchitectCategoryTab tab in tabs)
        {
            if (tab?.def?.defName == defName)
            {
                return tab;
            }
        }
        return null;
    }

    internal static void DrawNewGroupButton(Rect rect)
    {
        if (Widgets.ButtonText(rect, "+"))
        {
            OpenNewGroupMenu();
        }
        TooltipHandler.TipRegion(rect, Translator.Translate("MintTwoCol.NewGroup"));
    }

    private static void OpenNewGroupMenu()
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>
        {
            new FloatMenuOption("MintTwoCol.NewItemGroup".Translate(), () =>
                Find.WindowStack.Add(new Dialog_GroupName("MintTwoCol.NewItemGroup".Translate(), string.Empty, name => CreateGroup(RailGroupKind.Items, name)))),
            new FloatMenuOption("MintTwoCol.NewCategoryGroup".Translate(), () =>
                Find.WindowStack.Add(new Dialog_GroupName("MintTwoCol.NewCategoryGroup".Translate(), string.Empty, name => CreateGroup(RailGroupKind.Categories, name))))
        };
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static RailGroup CreateGroup(RailGroupKind kind, string name)
    {
        RailGroup group = new RailGroup
        {
            Id = RailIds.NewGroupId(),
            Name = name,
            Kind = kind,
            OnLeft = true
        };
        MintTwoColMod.Settings.Groups.Add(group);
        MintTwoColMod.Settings.LeftOrder.Add(group.Id);
        MintTwoColMod.Settings.Write();
        return group;
    }

    // 网格右键：把建筑项加入项组；已在组内的项置灰，末尾可新建项组并直接加入。
    // 已属于某个项组时，额外提供“移出所在建筑标签”。
    internal static List<FloatMenuOption> ItemGroupMenuOptions(string defName)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>();
        if (string.IsNullOrEmpty(defName))
        {
            return options;
        }

        List<FloatMenuOption> groups = new List<FloatMenuOption>();
        foreach (RailGroup group in MintTwoColMod.Settings.Groups)
        {
            if (group == null || group.Kind != RailGroupKind.Items)
            {
                continue;
            }
            RailGroup target = group;
            bool joined = target.Members != null && target.Members.Contains(defName);
            groups.Add(joined
                ? new FloatMenuOption(target.Name, null, MenuOptionPriority.DisabledOption)
                : new FloatMenuOption(target.Name, () => AddMember(target, defName)));
        }
        groups.Add(new FloatMenuOption("MintTwoCol.NewItemGroup".Translate(), () =>
            Find.WindowStack.Add(new Dialog_GroupName("MintTwoCol.NewItemGroup".Translate(), string.Empty, name =>
            {
                RailGroup created = CreateGroup(RailGroupKind.Items, name);
                if (created != null)
                {
                    AddMember(created, defName);
                }
            }))));

        options.Add(new FloatMenuOption("MintTwoCol.AddToGroup".Translate(), () => Find.WindowStack.Add(new FloatMenu(groups))));

        List<RailGroup> owners = MintTwoColMod.Settings.Groups
            .Where(g => g != null && g.Kind == RailGroupKind.Items && g.Members != null && g.Members.Contains(defName))
            .ToList();
        if (owners.Count > 0)
        {
            options.Add(new FloatMenuOption("MintTwoCol.RemoveFromGroup".Translate(), () =>
            {
                foreach (RailGroup owner in owners)
                {
                    owner.Members.Remove(defName);
                }
                MintTwoColMod.Settings.Write();
            }));
        }
        return options;
    }

    private static void OpenGroupMenu(RailGroup group)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>
        {
            new FloatMenuOption("MintTwoCol.RenameGroup".Translate(), () =>
                Find.WindowStack.Add(new Dialog_GroupName(
                    "MintTwoCol.RenameGroup".Translate(),
                    group.Name,
                    name =>
                    {
                        group.Name = name;
                        MintTwoColMod.Settings.Write();
                    }))),
            new FloatMenuOption("MintTwoCol.RemoveMember".Translate(), () => OpenRemoveMemberMenu(group)),
            new FloatMenuOption("MintTwoCol.DeleteGroup".Translate(), () => DeleteGroup(group))
        };
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static void OpenRemoveMemberMenu(RailGroup group)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>();
        if (group.Members.Count == 0)
        {
            options.Add(new FloatMenuOption("MintTwoCol.NoMembers".Translate(), null));
        }
        foreach (string member in group.Members.ToList())
        {
            string captured = member;
            options.Add(new FloatMenuOption(MemberLabel(group.Kind, captured), () =>
            {
                group.Members.Remove(captured);
                MintTwoColMod.Settings.Write();
            }));
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static void DeleteGroup(RailGroup group)
    {
        MintTwoColMod.Settings.Groups.Remove(group);
        RemoveId(MintTwoColMod.Settings.LeftOrder, group.Id);
        RemoveId(MintTwoColMod.Settings.RightOrder, group.Id);
        RemoveId(MintTwoColMod.Settings.Hidden, group.Id);
        RemoveId(MintTwoColMod.Settings.ForceLeft, group.Id);
        RemoveId(MintTwoColMod.Settings.ForceRight, group.Id);
        if (selectedGroup == group)
        {
            ClearGroupSelection();
        }
        ResetDrag();
        MintTwoColMod.Settings.Write();
    }

    private static void RemoveId(List<string> ids, string id)
    {
        ids.RemoveAll(value => string.Equals(value, id, StringComparison.Ordinal));
    }

    private static string MemberLabel(RailGroupKind kind, string member)
    {
        if (kind == RailGroupKind.Items)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(member);
            return def?.LabelCap.ToString() ?? member;
        }
        DesignationCategoryDef category = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(member);
        return category?.LabelCap.ToString() ?? member;
    }
}
