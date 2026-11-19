using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 商城面板：列出皮肤总表里的全部皮肤，支持购买与穿戴。
/// 所有判定（金币够不够、是否已拥有）都在 ShopService 里做，
/// 这里只负责展示与把点击事件抛出去，避免 UI 与业务逻辑互相污染。
/// </summary>
public class ShopPanel : MonoBehaviour
{
    /// <summary>每行高度（像素），和卡片内边距一起决定面板高度。</summary>
    [Header("布局")]
    [Tooltip("每个皮肤行的高度，改大后需要同步调整面板高度")]
    [SerializeField] private float _rowHeight = 88f;

    /// <summary>点击购买某皮肤时抛出皮肤编号。</summary>
    public event Action<byte> OnBuyRequested;

    /// <summary>点击穿戴某皮肤时抛出皮肤编号。</summary>
    public event Action<byte> OnEquipRequested;

    /// <summary>关闭按钮。</summary>
    public event Action OnCloseRequested;

    /// <summary>金币文本。</summary>
    private TextMeshProUGUI _coinsText;

    /// <summary>皮肤总表，Build 时传入。</summary>
    private SkinCatalog _catalog;

    /// <summary>每行的控件引用，Refresh 时只改文字与按钮状态，不重建节点。</summary>
    private readonly List<RowWidgets> _rows = new List<RowWidgets>();

    /// <summary>一行里需要动态改动的控件。</summary>
    private class RowWidgets
    {
        /// <summary>皮肤编号。</summary>
        public byte SkinId;

        /// <summary>价格与状态文字。</summary>
        public TextMeshProUGUI StateText;

        /// <summary>购买/穿戴按钮。</summary>
        public Button ActionButton;

        /// <summary>按钮文字。</summary>
        public TextMeshProUGUI ActionLabel;
    }

    /// <summary>按皮肤总表构建面板；catalog 为空时只显示空列表。</summary>
    public void Build(SkinCatalog catalog)
    {
        _catalog = catalog;

        // 用本组件的 RectTransform 当根，避免内容挂到画布下、Show/Hide 只动空壳。
        RectTransform root = (RectTransform)transform;
        UiTheme.Stretch(root);
        Image dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        int count = catalog != null ? catalog.Count : 0;
        float panelHeight = 150f + Mathf.Max(1, count) * _rowHeight;

        RectTransform card = UiTheme.CreatePanel("Card", root, UiTheme.PanelColor);
        UiTheme.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, panelHeight));

        TextMeshProUGUI title = UiTheme.CreateText("Title", card, "商城 · 皮肤", 34f, UiTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        UiTheme.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -46f), new Vector2(320f, 44f));

        _coinsText = UiTheme.CreateText("Coins", card, "金币 0", 26f, UiTheme.GoldColor, TextAlignmentOptions.MidlineRight);
        UiTheme.Place(_coinsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-140f, -46f), new Vector2(220f, 44f));

        Button closeButton = UiTheme.CreateButton("CloseButton", card, "关闭", UiTheme.DisabledColor, 22f);
        UiTheme.Place(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-28f, -24f), new Vector2(96f, 44f));
        closeButton.onClick.AddListener(() => OnCloseRequested?.Invoke());

        for (int i = 0; i < count; i++)
        {
            BuildRow(card, catalog.GetAt(i), i);
        }
    }

    /// <summary>构建一行皮肤条目：名称、价格/状态、操作按钮。</summary>
    private void BuildRow(RectTransform card, SkinDefinition skin, int rowIndex)
    {
        if (skin == null)
        {
            return;
        }

        RectTransform row = UiTheme.CreateRect($"Row_{skin.Id}", card);
        UiTheme.Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -110f - rowIndex * _rowHeight), new Vector2(624f, _rowHeight - 8f));

        Image rowBackground = row.gameObject.AddComponent<Image>();
        rowBackground.color = new Color(0.086f, 0.106f, 0.141f, 1f);
        rowBackground.raycastTarget = false;

        TextMeshProUGUI nameText = UiTheme.CreateText("Name", row, skin.DisplayName, 26f, UiTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        UiTheme.Place(nameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(300f, 40f));

        TextMeshProUGUI stateText = UiTheme.CreateText("State", row, $"{skin.PriceCoins} 金币", 22f, UiTheme.SubTextColor, TextAlignmentOptions.MidlineLeft);
        UiTheme.Place(stateText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(330f, 0f), new Vector2(160f, 34f));

        Button actionButton = UiTheme.CreateButton("Action", row, "购买", UiTheme.AccentColor, 22f);
        UiTheme.Place(actionButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-20f, 0f), new Vector2(140f, 52f));

        RowWidgets widgets = new RowWidgets
        {
            SkinId = skin.Id,
            StateText = stateText,
            ActionButton = actionButton,
            ActionLabel = actionButton.GetComponentInChildren<TextMeshProUGUI>(),
        };

        // 点击行为按"当前状态"决定：未拥有就买，已拥有未穿戴就穿，已穿戴则按钮禁用。
        actionButton.onClick.AddListener(() =>
        {
            if (actionButton.interactable == false)
            {
                return;
            }

            if (_catalog != null && _catalog.Get(widgets.SkinId) != null && IsEquipped(widgets.SkinId))
            {
                return;
            }

            if (IsOwned(widgets.SkinId))
            {
                OnEquipRequested?.Invoke(widgets.SkinId);
            }
            else
            {
                OnBuyRequested?.Invoke(widgets.SkinId);
            }
        });

        _rows.Add(widgets);
    }

    /// <summary>刷新金币与每行状态；coins 直接来自 WalletService，inventory 决定拥有/穿戴。</summary>
    public void Refresh(int coins, InventoryStore inventory)
    {
        _ownedSkinIds.Clear();
        byte equippedId = 0;

        if (inventory != null)
        {
            equippedId = inventory.EquippedId;
            if (_catalog != null)
            {
                for (int i = 0; i < _catalog.Count; i++)
                {
                    SkinDefinition skin = _catalog.GetAt(i);
                    if (skin != null && inventory.IsOwned(skin.Id))
                    {
                        _ownedSkinIds.Add(skin.Id);
                    }
                }
            }
        }

        _currentEquippedId = equippedId;

        if (_coinsText != null)
        {
            _coinsText.text = $"金币 {coins}";
        }

        foreach (RowWidgets row in _rows)
        {
            SkinDefinition skin = _catalog != null ? _catalog.Get(row.SkinId) : null;
            if (skin == null)
            {
                continue;
            }

            bool owned = _ownedSkinIds.Contains(row.SkinId);
            bool equipped = owned && row.SkinId == _currentEquippedId;

            row.StateText.text = equipped ? "已穿戴" : owned ? "已拥有" : $"{skin.PriceCoins} 金币";
            row.StateText.color = equipped ? UiTheme.AccentColor : UiTheme.SubTextColor;

            row.ActionLabel.text = equipped ? "使用中" : owned ? "穿戴" : "购买";
            row.ActionButton.interactable = !equipped;
            Image buttonImage = row.ActionButton.targetGraphic as Image;
            if (buttonImage != null)
            {
                buttonImage.color = equipped ? UiTheme.DisabledColor : owned ? UiTheme.BlueColor : UiTheme.AccentColor;
            }
        }
    }

    /// <summary>当前已拥有的皮肤编号集合，Refresh 时重建。</summary>
    private readonly List<byte> _ownedSkinIds = new List<byte>();

    /// <summary>当前穿戴的皮肤编号。</summary>
    private byte _currentEquippedId;

    /// <summary>是否已拥有指定皮肤（仅用于点击分支判断）。</summary>
    private bool IsOwned(byte skinId)
    {
        return _ownedSkinIds.Contains(skinId);
    }

    /// <summary>是否正在穿戴指定皮肤。</summary>
    private bool IsEquipped(byte skinId)
    {
        return _currentEquippedId == skinId;
    }

    /// <summary>显示面板并触发一次刷新由调用方负责。</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>隐藏面板。</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
