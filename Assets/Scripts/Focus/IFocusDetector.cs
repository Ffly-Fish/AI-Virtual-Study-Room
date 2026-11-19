/// <summary>
/// 专注检测器的统一入口。上层的去抖、计时、发币只依赖这个接口，
/// 因此阶段 1 可以先用模拟检测器跑通整条链路，之后再换成 Barracuda 实现。
/// </summary>
public interface IFocusDetector
{
    /// <summary>检测器当前是否可用（相机权限被拒、模型缺失、设备不支持时返回 false，界面据此进入旁观模式）。</summary>
    bool IsAvailable { get; }

    /// <summary>立即采样一次；检测器未就绪时必须返回 <see cref="FocusSample.Empty"/> 而不是抛异常。</summary>
    FocusSample Detect();

    /// <summary>释放相机与模型资源；退出房间或切后台时调用。</summary>
    void Release();
}
