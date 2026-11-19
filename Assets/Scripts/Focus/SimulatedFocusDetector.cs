using UnityEngine;

/// <summary>
/// 调试用的专注判定器：按住空格表示"看着屏幕"，松手表示"走神"。
/// 真实摄像头判定（Barracuda 人脸模型）尚未接入，先用它把
/// "判定 → 动作 A/B → 金币"整条链路跑通；接入模型后由 GameBootstrap 换成真判定器。
/// </summary>
public class SimulatedFocusDetector : MonoBehaviour, IFocusDetector
{
    /// <summary>表示专注的按键，按住即专注。</summary>
    [Header("调试按键")]
    [Tooltip("按住这个键表示正在看屏幕（专注），松开表示走神")]
    [SerializeField] private KeyCode _focusKey = KeyCode.Space;

    /// <summary>是否启动时默认专注，方便不用按键也能看到坐下动画。</summary>
    [Tooltip("勾选后启动即视为专注，无需按住按键")]
    [SerializeField] private bool _startFocusing;

    /// <summary>当前是否处于"人在屏幕前"的模拟状态。</summary>
    private bool _hasFace;

    /// <summary>
    /// 临时诊断开关：非空时忽略键盘输入、直接返回该值，便于无人值守验证"专注 → 坐下 → 金币"链路。
    /// TODO: 接入真实人脸模型后删除此开关。
    /// </summary>
    public static bool? ForceOverride;

    /// <summary>调试判定器永远可用，不依赖硬件。</summary>
    public bool IsAvailable => true;

    /// <summary>初始化模拟状态。</summary>
    private void Awake()
    {
        _hasFace = _startFocusing;
    }

    /// <summary>采样：按键按下或勾选了默认专注时视为脸在屏幕前，偏航角固定为 0（正对屏幕）。</summary>
    public FocusSample Detect()
    {
        if (ForceOverride.HasValue)
        {
            return new FocusSample
            {
                IsFacePresent = ForceOverride.Value,
                YawDegrees = 0f,
                Confidence = ForceOverride.Value ? 1f : 0f,
                Timestamp = Time.unscaledTime,
            };
        }

        bool pressed = Input.GetKey(_focusKey);
        bool facePresent = _startFocusing || pressed;

        return new FocusSample
        {
            IsFacePresent = facePresent,
            YawDegrees = 0f,
            Confidence = facePresent ? 1f : 0f,
            Timestamp = Time.unscaledTime,
        };
    }

    /// <summary>调试判定器不占用摄像头，无需释放资源。</summary>
    public void Release()
    {
    }
}
