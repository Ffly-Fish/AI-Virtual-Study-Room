using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 轻提示：屏幕下方的短句反馈（金币不足、购买成功、掉线等）。
/// 自行管理淡入淡出，不需要 DOTween，也避免和相机过场争用同一个 Tween。
/// </summary>
public class ToastView : MonoBehaviour
{
    /// <summary>提示停留时长（秒，不含淡入淡出）。</summary>
    [Header("显示时长")]
    [Tooltip("提示完全显示的秒数，淡入淡出各 0.15 秒")]
    [SerializeField] private float _holdSeconds = 1.6f;

    /// <summary>淡入淡出时长（秒）。</summary>
    [Tooltip("淡入与淡出各自的时长")]
    [SerializeField] private float _fadeSeconds = 0.15f;

    /// <summary>提示文本。</summary>
    private TextMeshProUGUI _text;

    /// <summary>整个提示的透明度控制。</summary>
    private CanvasGroup _group;

    /// <summary>正在播放的淡出协程，新提示会打断它。</summary>
    private Coroutine _running;

    /// <summary>构建提示节点，默认放在屏幕下方中间。</summary>
    public void Build()
    {
        // 用本组件的 RectTransform 当根，CanvasGroup 才会作用在真正的显示内容上。
        RectTransform root = (RectTransform)transform;
        UiTheme.Stretch(root);
        _group = root.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        RectTransform card = UiTheme.CreatePanel("Card", root, new Color(0.043f, 0.055f, 0.078f, 0.92f));
        UiTheme.Place(card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(560f, 56f));

        _text = UiTheme.CreateText("Message", card, string.Empty, 24f, UiTheme.TextColor, TextAlignmentOptions.Center);
        UiTheme.Stretch(_text.rectTransform, 16f, 0f, 16f, 0f);
    }

    /// <summary>显示一条提示；已在显示中的提示会被立即替换。</summary>
    public void Show(string message)
    {
        if (_text == null || string.IsNullOrEmpty(message))
        {
            return;
        }

        if (_running != null)
        {
            StopCoroutine(_running);
        }

        _text.text = message;
        _running = StartCoroutine(PlayRoutine());
    }

    /// <summary>淡入 → 停留 → 淡出。</summary>
    private IEnumerator PlayRoutine()
    {
        yield return FadeTo(1f);
        yield return new WaitForSeconds(_holdSeconds);
        yield return FadeTo(0f);
        _running = null;
    }

    /// <summary>把透明度线性推到目标值；用 unscaled 时间，游戏暂停时提示也能正常消失。</summary>
    private IEnumerator FadeTo(float targetAlpha)
    {
        float startAlpha = _group.alpha;
        float elapsed = 0f;
        while (elapsed < _fadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / _fadeSeconds);
            yield return null;
        }

        _group.alpha = targetAlpha;
    }
}
