using System;
using UnityEngine;

/// <summary>
/// 专注计时器（纯 C#）：累计专注时长，每满 <c>secondsPerCoin</c> 秒发一枚金币。
/// 用户口径是"走神只暂停、不清零"，所以这里在非专注时直接早退，不重置累计值。
/// </summary>
public class FocusTracker
{
    /// <summary>发币间隔（秒），用户设定 10 秒 1 枚。</summary>
    private readonly float _secondsPerCoin;

    /// <summary>当前这一段的累计专注秒数（满一段清零继续累计）。</summary>
    private float _accumulatedSeconds;

    /// <summary>发币事件：参数为本轮新增的金币数量（通常是 1，掉帧时会合并）。</summary>
    public event Action<int> OnCoinEarned;

    /// <summary>距离下一枚金币的进度（0-1），供 HUD 画环形进度。</summary>
    public float Progress01 => Mathf.Clamp01(_accumulatedSeconds / _secondsPerCoin);

    /// <summary>当前累计专注秒数，调试面板与测试断言用。</summary>
    public float AccumulatedSeconds => _accumulatedSeconds;

    /// <summary>创建计时器；间隔必须为正，否则 0 除。</summary>
    public FocusTracker(float secondsPerCoin = 10f)
    {
        _secondsPerCoin = Mathf.Max(1f, secondsPerCoin);
    }

    /// <summary>每帧推进；<paramref name="isFocusing"/> 为 false 时暂停累计（不清零）。</summary>
    public void Tick(float deltaSeconds, bool isFocusing)
    {
        if (!isFocusing || deltaSeconds <= 0f)
        {
            return;
        }

        _accumulatedSeconds += deltaSeconds;

        // 用 while 而不是 if：低帧率或长时间挂起后一次补满多枚金币时不会丢。
        int earned = 0;
        while (_accumulatedSeconds >= _secondsPerCoin)
        {
            _accumulatedSeconds -= _secondsPerCoin;
            earned++;
        }

        if (earned > 0)
        {
            OnCoinEarned?.Invoke(earned);
        }
    }
}
