using UnityEngine;

/// <summary>
/// 调试用的专注判定器：按住空格表示"看着屏幕"，松手表示"走神"。
/// 正常情况下由 BarracudaFaceDetector 接管（GameBootstrap 优先用真模型），
/// 只有在没有配置模型或摄像头不可用时才退回这里，方便在编辑器里验证动作与金币链路。
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
