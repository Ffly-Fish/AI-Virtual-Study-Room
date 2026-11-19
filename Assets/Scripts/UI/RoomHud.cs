using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 房间内 HUD：显示自己的名字、金币、专注状态与进度，并提供商城/退出入口。
/// 金币与专注状态只有自己看得到（用户要求），其他玩家只通过角色动作与名字牌感知。
/// </summary>
public class RoomHud : MonoBehaviour
{
    /// <summary>点击商城按钮。</summary>
    public event Action OnShopRequested;

    /// <summary>点击退出按钮。</summary>
    public event Action OnExitRequested;

    /// <summary>金币文本。</summary>
    private TextMeshProUGUI _coinsText;

    /// <summary>专注状态文本。</summary>
    private TextMeshProUGUI _focusText;

    /// <summary>金币进度条填充。</summary>
    private Image _progressFill;

    /// <summary>进度条宽度（像素），用于按比例算填充宽度。</summary>
    private float _progressWidth = 220f;

    /// <summary>构建 HUD：左上角状态卡、右上角按钮、底部进度条。</summary>
    public void Build()
    {
        // 用本组件的 RectTransform 当根，Show/Hide 才会作用在内容上。
        RectTransform root = (RectTransform)transform;
        UiTheme.Stretch(root);

        RectTransform statusCard = UiTheme.CreatePanel("StatusCard", root, UiTheme.PanelColor);
        UiTheme.Place(statusCard, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(320f, 132f));

        TextMeshProUGUI title = UiTheme.CreateText("Title", statusCard, "自习中", 26f, UiTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        UiTheme.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -28f), new Vector2(180f, 32f));

        _focusText = UiTheme.CreateText("FocusState", statusCard, "走神中", 22f, UiTheme.DangerColor, TextAlignmentOptions.MidlineRight);
        UiTheme.Place(_focusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -28f), new Vector2(120f, 30f));

        _coinsText = UiTheme.CreateText("Coins", statusCard, "金币 0", 24f, UiTheme.GoldColor, TextAlignmentOptions.MidlineLeft);
        UiTheme.Place(_coinsText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 52f), new Vector2(200f, 30f));

        // 进度条：底槽 + 填充，填充宽度随专注累计时间变化。
        RectTransform barBackground = UiTheme.CreateRect("ProgressBar", statusCard);
        UiTheme.Place(barBackground, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 24f), new Vector2(_progressWidth, 10f));
        Image barImage = barBackground.gameObject.AddComponent<Image>();
        barImage.color = new Color(0.125f, 0.156f, 0.204f, 1f);
        barImage.raycastTarget = false;

        RectTransform fillRect = UiTheme.CreateRect("Fill", barBackground);
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(0f, 0f);
        _progressFill = fillRect.gameObject.AddComponent<Image>();
        _progressFill.color = UiTheme.AccentColor;
        _progressFill.raycastTarget = false;

        Button shopButton = UiTheme.CreateButton("ShopButton", root, "商城", UiTheme.AccentColor, 22f);
        UiTheme.Place(shopButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -24f), new Vector2(110f, 52f));
        shopButton.onClick.AddListener(() => OnShopRequested?.Invoke());

        Button exitButton = UiTheme.CreateButton("ExitButton", root, "退出", UiTheme.DisabledColor, 22f);
        UiTheme.Place(exitButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-146f, -24f), new Vector2(110f, 52f));
        exitButton.onClick.AddListener(() => OnExitRequested?.Invoke());
    }

    /// <summary>更新金币显示。</summary>
    public void SetCoins(int coins)
    {
        if (_coinsText != null)
        {
            _coinsText.text = $"金币 {coins}";
        }
    }

    /// <summary>更新专注状态：专注时绿色、否则红色。</summary>
    public void SetFocusing(bool isFocusing)
    {
        if (_focusText == null)
        {
            return;
        }

        _focusText.text = isFocusing ? "专注中" : "走神中";
        _focusText.color = isFocusing ? UiTheme.AccentColor : UiTheme.DangerColor;
    }

    /// <summary>更新金币进度（0~1），用于让玩家看到"再专注一会儿就有金币"。</summary>
    public void SetProgress(float progress01)
    {
        if (_progressFill == null)
        {
            return;
        }

        float width = Mathf.Clamp01(progress01) * _progressWidth;
        _progressFill.rectTransform.sizeDelta = new Vector2(width, 0f);
    }

    /// <summary>显示 HUD。</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>隐藏 HUD。</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
