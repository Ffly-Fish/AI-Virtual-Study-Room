/// <summary>
/// 一次采样的人脸判定结果。做成 struct 是因为 8Hz 采样、每帧都可能产生新样本，
/// 用 class 会增加 GC 压力（Android 上尤其明显）。
/// </summary>
public struct FocusSample
{
    /// <summary>是否检测到人脸（检测器自己的原始判断，未经去抖）。</summary>
    public bool IsFacePresent;

    /// <summary>头部偏航角（度）：0 表示正对屏幕，正值/负值代表左右转头方向。</summary>
    public float YawDegrees;

    /// <summary>检测器给出的置信度（0-1），无模型或未检测到时为 0。</summary>
    public float Confidence;

    /// <summary>样本产生的时间（Time.unscaledTime），用于去抖计时。</summary>
    public float Timestamp;

    /// <summary>构造一个"无人脸"样本，供检测器初始化与异常分支复用。</summary>
    public static FocusSample Empty(float now)
    {
        return new FocusSample
        {
            IsFacePresent = false,
            YawDegrees = 0f,
            Confidence = 0f,
            Timestamp = now,
        };
    }
}
