using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时 UI 的统一外观与控件工厂。
/// 阶段 1 的界面全部由代码构建（工程里没有可复用的 UI 预制体），
/// 好处是零接线、改样式只改这里；等界面稳定后再固化成预制体。
/// </summary>
public static class UiTheme
{
    /// <summary>界面字体；由 GameBootstrap 注入中文 SDF 字体，缺省时退回 TMP 默认字体（不含中文）。</summary>
    public static TMP_FontAsset Font { get; set; }

    /// <summary>全屏遮罩底色：偏冷的深色，和场景的 low-poly 冷色调一致。</summary>
    public static readonly Color BackgroundColor = new Color(0.043f, 0.055f, 0.078f, 0.96f);

    /// <summary>面板底色。</summary>
    public static readonly Color PanelColor = new Color(0.063f, 0.078f, 0.106f, 0.98f);

    /// <summary>描边色。</summary>
    public static readonly Color BorderColor = new Color(0.141f, 0.188f, 0.251f, 1f);

    /// <summary>主文字色。</summary>
    public static readonly Color TextColor = new Color(0.949f, 0.961f, 0.980f, 1f);

    /// <summary>次要文字色。</summary>
    public static readonly Color SubTextColor = new Color(0.541f, 0.604f, 0.671f, 1f);

    /// <summary>强调色（薄荷绿），与架构图配色一致。</summary>
    public static readonly Color AccentColor = new Color(0.302f, 0.839f, 0.659f, 1f);

    /// <summary>已拥有但未穿戴状态的按钮色（蓝）。</summary>
    public static readonly Color BlueColor = new Color(0.227f, 0.616f, 1f, 1f);

    /// <summary>金币色。</summary>
    public static readonly Color GoldColor = new Color(0.831f, 0.627f, 0.090f, 1f);

    /// <summary>错误/警示色。</summary>
    public static readonly Color DangerColor = new Color(0.949f, 0.420f, 0.420f, 1f);

    /// <summary>按钮禁用色。</summary>
    public static readonly Color DisabledColor = new Color(0.180f, 0.216f, 0.267f, 1f);

    /// <summary>是否已经提示过"没有指定中文字体"，避免每帧刷日志。</summary>
    private static bool _hasWarnedMissingFont;

    /// <summary>取生效字体：优先注入的中文字体，其次 TMP 默认字体。</summary>
    public static TMP_FontAsset ResolveFont()
    {
        if (Font != null)
        {
            return Font;
        }

        if (!_hasWarnedMissingFont)
        {
            _hasWarnedMissingFont = true;
            Debug.LogWarning("[UiTheme] 没有注入界面字体，中文会显示为方块。请给 GameBootstrap 指定 HONORSansCN SDF 字体。");
        }

        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>创建一个空的 RectTransform 节点，父节点默认铺满。</summary>
    public static RectTransform CreateRect(string nodeName, Transform parent)
    {
        GameObject go = new GameObject(nodeName, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect);
        return rect;
    }

    /// <summary>铺满父节点，并留出四边内边距。</summary>
    public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>把节点锚定在某一点并设定尺寸，锚点以父节点为参照（0~1）。</summary>
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>创建文本节点；字号与颜色按设计稿统一。</summary>
    public static TextMeshProUGUI CreateText(string nodeName, Transform parent, string content, float fontSize,
        Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        RectTransform rect = CreateRect(nodeName, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = ResolveFont();
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        return text;
    }

    /// <summary>创建纯色图片节点（用作背景、描边、进度条）。</summary>
    public static Image CreateImage(string nodeName, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(nodeName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>创建带描边的面板：外框用 1 像素描边色，内部叠一层面板底色。</summary>
    public static RectTransform CreatePanel(string nodeName, Transform parent, Color fill)
    {
        RectTransform rect = CreateRect(nodeName, parent);
        Image border = rect.gameObject.AddComponent<Image>();
        border.color = BorderColor;
        border.raycastTarget = false;

        Image inner = CreateImage("Fill", rect, fill);
        Stretch(inner.rectTransform, 1f, 1f, 1f, 1f);
        inner.color = fill;
        return rect;
    }

    /// <summary>创建按钮：底色 + 居中文字，返回 Button 以便挂回调。</summary>
    public static Button CreateButton(string nodeName, Transform parent, string label, Color background, float fontSize = 26f)
    {
        RectTransform rect = CreateRect(nodeName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = background;
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.90f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 0.90f, 1f);
        colors.disabledColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText("Label", rect, label, fontSize, TextColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 8f, 0f, 8f, 0f);

        return button;
    }

    /// <summary>创建单行输入框；返回 TMP_InputField，可直接读 text。</summary>
    public static TMP_InputField CreateInputField(string nodeName, Transform parent, string placeholderText, float fontSize = 28f)
    {
        RectTransform rect = CreateRect(nodeName, parent);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.031f, 0.043f, 0.063f, 1f);
        background.raycastTarget = true;

        TMP_InputField field = rect.gameObject.AddComponent<TMP_InputField>();

        RectTransform textArea = CreateRect("TextArea", rect);
        Stretch(textArea, 14f, 6f, 14f, 6f);
        textArea.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI text = CreateText("Text", textArea, string.Empty, fontSize, TextColor, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);

        TextMeshProUGUI placeholder = CreateText("Placeholder", textArea, placeholderText, fontSize, SubTextColor, TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.rectTransform);

        field.textViewport = textArea;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.fontAsset = ResolveFont();
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 12;
        field.caretWidth = 2;
        return field;
    }
}
