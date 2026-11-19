using UnityEngine;

/// <summary>
/// 专注状态去抖器（纯 C#，便于单测与脱离场景验证）。
/// 人脸检测本身会抖动，直接用原始结果会让角色在"坐/站"之间闪，
/// 所以要求新状态连续保持 <c>debounceSeconds</c> 秒才认可。
/// </summary>
public class FocusFilter
{
    /// <summary>状态保持多久才认可，0.4 秒为实测手感值（用户确认的口径）。</summary>
    private readonly float _debounceSeconds;

    /// <summary>偏航角超过该值视为"没看屏幕"，25 度是宽松标准下的经验阈值。</summary>
    private readonly float _maxYawDegrees;

    /// <summary>候选状态（最新原始判定，尚未认可）。</summary>
    private bool _candidate;

    /// <summary>已认可的稳定状态，对外输出。</summary>
    private bool _stable;

    /// <summary>候选状态开始保持的时间点。</summary>
    private float _candidateSince;

    /// <summary>已认可的专注状态，表现层与计时器直接读这里。</summary>
    public bool IsFocusing => _stable;

    /// <summary>创建去抖器；参数留默认值以贴合当前设计。</summary>
    public FocusFilter(float debounceSeconds = 0.4f, float maxYawDegrees = 25f)
    {
        _debounceSeconds = Mathf.Max(0.05f, debounceSeconds);
        _maxYawDegrees = Mathf.Max(1f, maxYawDegrees);
        _candidateSince = -1f;
    }

    /// <summary>喂入一个样本并返回稳定状态是否发生变化（true 表示需要通知上层刷新）。</summary>
    public bool Update(FocusSample sample)
    {
        // 原始判定：有人脸 且 偏航角在阈值内，才算"这一刻在看屏幕"。
        bool rawFocusing = sample.IsFacePresent && Mathf.Abs(sample.YawDegrees) <= _maxYawDegrees;

        if (rawFocusing != _candidate)
        {
            _candidate = rawFocusing;
            _candidateSince = sample.Timestamp;
        }

        if (_candidate == _stable)
        {
            return false;
        }

        // 候选状态还没保持够时间就继续等，避免抖动被当成真切换。
        if (sample.Timestamp - _candidateSince < _debounceSeconds)
        {
            return false;
        }

        _stable = _candidate;
        return true;
    }

    /// <summary>强制复位到指定状态，用于切后台、暂停等场景（避免恢复时误判为刚切换）。</summary>
    public void Reset(bool focusing, float now)
    {
        _stable = focusing;
        _candidate = focusing;
        _candidateSince = now;
    }
}
