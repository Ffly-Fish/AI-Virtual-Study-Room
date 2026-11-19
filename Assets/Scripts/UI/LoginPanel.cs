using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录面板：输入用户名后进入房间。
/// 阶段 1 没有账号系统，用户名只作为显示名与 PlayerPrefs 的记忆项。
/// </summary>
public class LoginPanel : MonoBehaviour
{
    /// <summary>用户名最大长度，和网络层的 FixedString64Bytes 保持余量。</summary>
    [Header("输入约束")]
    [Tooltip("用户名最大长度，超出的部分不会输入进去")]
    [SerializeField] private int _maxNameLength = 12;

    /// <summary>点击进入时抛出用户名，空名由面板拦下。</summary>
    public event Action<string> OnEnterRequested;

    /// <summary>用户名输入框。</summary>
    private TMP_InputField _nameField;

    /// <summary>提示文字（错误/加载中）。</summary>
    private TextMeshProUGUI _hintText;

    /// <summary>进入按钮，搜索期间要禁用防止重复点击。</summary>
    private Button _enterButton;

    /// <summary>构建整个面板：标题、输入框、按钮、提示行。</summary>
    public void Build(string defaultName)
    {
        // 直接用本组件所在的 RectTransform 当根：Show/Hide 切换的就是这个物体。
        // 若在画布下另建同名根节点，隐藏时只隐藏空壳，内容仍会留在屏幕上。
        RectTransform root = (RectTransform)transform;
        UiTheme.Stretch(root);
        Image background = root.gameObject.AddComponent<Image>();
        background.color = UiTheme.BackgroundColor;
        background.raycastTarget = true;

        RectTransform card = UiTheme.CreatePanel("Card", root, UiTheme.PanelColor);
        UiTheme.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 380f));

        TextMeshProUGUI title = UiTheme.CreateText("Title", card, "AI 自习室", 52f, UiTheme.TextColor, TextAlignmentOptions.Center);
        UiTheme.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(560f, 64f));

        TextMeshProUGUI subtitle = UiTheme.CreateText("Subtitle", card, "输入名字，和同学一起在线上自习", 22f,
            UiTheme.SubTextColor, TextAlignmentOptions.Center);
        UiTheme.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(560f, 34f));

        _nameField = UiTheme.CreateInputField("NameField", card, "你的名字", 28f);
        UiTheme.Place(_nameField.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 10f), new Vector2(420f, 64f));
        _nameField.characterLimit = _maxNameLength;
        _nameField.text = defaultName ?? string.Empty;

        _enterButton = UiTheme.CreateButton("EnterButton", card, "进入", UiTheme.AccentColor);
        Image enterImage = _enterButton.GetComponent<Image>();
        enterImage.color = UiTheme.AccentColor;
        UiTheme.Place(_enterButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 76f), new Vector2(420f, 64f));
        _enterButton.onClick.AddListener(HandleEnterClicked);

        _hintText = UiTheme.CreateText("Hint", card, string.Empty, 20f, UiTheme.DangerColor, TextAlignmentOptions.Center);
        UiTheme.Place(_hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(560f, 28f));
    }

    /// <summary>显示面板；进入房间后由流程层关闭。</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>隐藏面板。</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>显示一行提示；传空串即清空。用于"名字不能为空""正在搜索房间…"等状态。</summary>
    public void SetHint(string message, bool isError = true)
    {
        if (_hintText == null)
        {
            return;
        }

        _hintText.text = message ?? string.Empty;
        _hintText.color = isError ? UiTheme.DangerColor : UiTheme.SubTextColor;
    }

    /// <summary>切换"进入"按钮可用状态，避免搜索期间重复点击。</summary>
    public void SetInteractable(bool isInteractable)
    {
        if (_enterButton != null)
        {
            _enterButton.interactable = isInteractable;
        }

        if (_nameField != null)
        {
            _nameField.interactable = isInteractable;
        }
    }

    /// <summary>点击进入：空名拦截并提示，其余交给流程层去搜索房间。</summary>
    private void HandleEnterClicked()
    {
        string playerName = _nameField != null ? _nameField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(playerName))
        {
            SetHint("名字不能为空");
            return;
        }

        SetHint(string.Empty);
        OnEnterRequested?.Invoke(playerName);
    }
}
