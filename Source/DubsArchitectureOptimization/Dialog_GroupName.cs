using System;
using UnityEngine;
using Verse;

namespace DubsArchitectureOptimization;

// 自定义组的命名/重命名对话框；确认时返回去掉首尾空白后的名称。
internal sealed class Dialog_GroupName : Window
{
    private readonly string title;
    private readonly Action<string> onConfirm;
    private string name;

    public override Vector2 InitialSize => new Vector2(360f, 160f);

    internal Dialog_GroupName(string title, string initial, Action<string> onConfirm)
    {
        this.title = title;
        this.onConfirm = onConfirm;
        name = initial ?? string.Empty;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
        // Window 默认 closeOnAccept = true：Enter 会在 DoWindowContents 之前直接 Close()，
        // 自己处理 Enter 必须关掉，否则永远走不到确认逻辑。
        closeOnAccept = false;
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Enter 必须先于 TextField 读取：文本框会消费 KeyDown，放到后面永远收不到。
        bool accept = Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (accept)
        {
            Event.current.Use();
        }

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), title);
        Text.Font = GameFont.Small;
        name = Widgets.TextField(new Rect(0f, 44f, inRect.width, 28f), name ?? string.Empty);

        if (accept)
        {
            Confirm();
        }

        if (Widgets.ButtonText(new Rect(0f, inRect.yMax - 32f, 120f, 28f), "OK".Translate()))
        {
            Confirm();
        }
        if (Widgets.ButtonText(new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 28f), "Cancel".Translate()))
        {
            Close();
        }
    }

    private void Confirm()
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return;
        }
        onConfirm?.Invoke(trimmed);
        Close();
    }
}
