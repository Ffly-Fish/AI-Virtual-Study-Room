using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 角色头顶的名字牌与专注状态图标。
/// 用户要求"用户名作为世界 UI 置于角色之上"，所以这里在角色根节点下建一个 World Space 画布，
/// 并挂 <see cref="BillboardToCamera"/> 让它始终面向主相机；不使用预制体，避免每套皮肤都要复制一份。
/// </summary>
public class PlayerNameTag : MonoBehaviour
{
    /// <summary>名字牌离地高度（米）。</summary>
    [Header("位置")]
    [Tooltip("名字牌相对角色根节点的高度（米）。角色约 3.4 米高，取 3.65 刚好在头顶上方")]
    [SerializeField] private float _heightOffset = 3.65f;

    /// <summary>名字牌文字宽度（米）。</summary>
    [Tooltip("名字牌在世界空间中的宽度（米），1.2 米约等于角色肩宽")]
    [SerializeField] private float _tagWidth = 1.2f;

    /// <summary>专注状态圆点：专注时绿色，走神时灰色。</summary>
    private Image _focusDot;

    /// <summary>名字文本。</summary>
    private TMP_Text _nameText;

    /// <summary>Awake 之前收到的名字，等世界 UI 建好后补写。</summary>
    private string _pendingName = string.Empty;

    /// <summary>Awake 之前收到的专注状态，等状态圆点建好后补写。</summary>
    private bool _pendingFocusing;

    /// <summary>启动时构建世界 UI，避免依赖任何预制体资源。</summary>
    private void Awake()
    {
        BuildWorldTag();
    }

    /// <summary>创建 World Space 画布、状态点与名字文本。</summary>
    private void BuildWorldTag()
    {
        GameObject canvasObject = new GameObject("NameTagCanvas", typeof(RectTransform), typeof(Canvas));
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.SetParent(transform, false);
        canvasRect.localPosition = new Vector3(0f, _heightOffset, 0f);
        canvasRect.localRotation = Quaternion.identity;
        // 世界空间画布的常用口径：尺寸按"像素"给（宽 100 像素 = 1 米），再整体缩到 0.01。
        // 若直接写 1.2 x 0.25 再乘 0.01，名字牌只有 1.2 厘米宽，屏幕上根本看不见。
        canvasRect.sizeDelta = new Vector2(_tagWidth * 160f, 44f);
        canvasRect.localScale = Vector3.one * 0.01f;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        // 用现有组件让名字牌始终面向相机，省掉一套重复的朝向逻辑；
        // 画布是平面 UI，必须用默认的"匹配相机旋转"模式，否则文字是反的。
        canvasObject.AddComponent<BillboardToCamera>();

        RectTransform dotRect = UiTheme.CreateRect("FocusDot", canvasRect);
        UiTheme.Place(dotRect, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(2f, 0f), new Vector2(10f, 10f));
        _focusDot = dotRect.gameObject.AddComponent<Image>();

        _nameText = UiTheme.CreateText("Name", canvasRect, string.Empty, 28f, UiTheme.TextColor, TextAlignmentOptions.Center);
        // 名字牌要单行显示：默认换行会把 4 个汉字拆成两行，顶到天花板。
        _nameText.enableWordWrapping = false;
        UiTheme.Stretch(_nameText.rectTransform, 26f, 0f, 0f, 0f);

        _nameText.text = _pendingName;
        SetFocusing(_pendingFocusing);
    }

    /// <summary>设置显示名；长名字由输入框限制长度，这里不再截断。</summary>
    public void SetName(string displayName)
    {
        _pendingName = displayName ?? string.Empty;
        if (_nameText != null)
        {
            _nameText.text = _pendingName;
        }
    }

    /// <summary>设置专注状态图标颜色；其他人只看到图标，看不到金币。</summary>
    public void SetFocusing(bool isFocusing)
    {
        _pendingFocusing = isFocusing;
        if (_focusDot != null)
        {
            _focusDot.color = isFocusing ? UiTheme.AccentColor : new Color(0.45f, 0.48f, 0.53f, 1f);
        }
    }
}
