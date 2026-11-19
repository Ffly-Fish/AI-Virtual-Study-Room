using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 相机导演：进入座位时把主相机用 DOTween 推到该座位的观察点，结束后交还自由视角。
/// 用户允许点过场动画，所以提供 <see cref="SkipIntro"/> 立即结束。
/// 相机位置一律来自 SeatMarker.CameraPosition，代码不硬编码角度。
/// </summary>
public class CameraDirector : MonoBehaviour
{
    /// <summary>推镜目标相机；留空则用 Camera.main。</summary>
    [Header("目标相机")]
    [Tooltip("要移动的相机，留空会自动使用 MainCamera")]
    [SerializeField] private Camera _targetCamera;

    /// <summary>推镜时长（秒）。</summary>
    [Header("过场参数")]
    [Tooltip("相机从当前位置飞到座位观察点所需秒数，0 表示瞬间到位")]
    [SerializeField] private float _flyDuration = 1.8f;

    /// <summary>缓动曲线：默认 OutCubic，起步快、收尾稳，符合"坐下"的手感。</summary>
    [Tooltip("推镜缓动曲线，默认 OutCubic")]
    [SerializeField] private Ease _flyEase = Ease.OutCubic;

    /// <summary>推镜结束回调，供流程控制器切到"房间内"状态。</summary>
    public event Action OnIntroFinished;

    /// <summary>正在播放的推镜序列，跳过时统一 Kill。</summary>
    private Sequence _flySequence;

    /// <summary>是否正在播放过场。</summary>
    public bool IsPlaying => _flySequence != null && _flySequence.IsActive();

    /// <summary>启动时兜底取主相机，避免场景里忘挂引用。</summary>
    private void Awake()
    {
        if (_targetCamera == null)
        {
            _targetCamera = Camera.main;
        }

        if (_targetCamera == null)
        {
            Debug.LogError("[CameraDirector] 场景里没有相机，过场将无法播放。", this);
        }
    }

    /// <summary>飞到指定座位；重复调用会先停掉上一次，保证不会两条序列打架。</summary>
    public void FlyToSeat(SeatMarker seat)
    {
        if (_targetCamera == null || seat == null)
        {
            Debug.LogWarning("[CameraDirector] 缺少相机或座位，跳过过场。", this);
            OnIntroFinished?.Invoke();
            return;
        }

        SkipIntro();

        Transform cameraTransform = _targetCamera.transform;

        // 时长 <= 0 时直接落位，依旧走回调，保证流程状态机不卡住。
        if (_flyDuration <= 0.01f)
        {
            cameraTransform.SetPositionAndRotation(seat.CameraPosition, seat.CameraRotation);
            OnIntroFinished?.Invoke();
            return;
        }

        _flySequence = DOTween.Sequence();
        _flySequence.Append(cameraTransform.DOMove(seat.CameraPosition, _flyDuration).SetEase(_flyEase));
        _flySequence.Join(cameraTransform.DORotateQuaternion(seat.CameraRotation, _flyDuration).SetEase(_flyEase));
        _flySequence.OnComplete(() =>
        {
            _flySequence = null;
            OnIntroFinished?.Invoke();
        });
    }

    /// <summary>立即结束过场：杀掉序列但不回调，避免"跳过"和"自然结束"各触发一次。</summary>
    public void SkipIntro()
    {
        if (_flySequence != null)
        {
            _flySequence.Kill();
            _flySequence = null;
        }
    }

    /// <summary>销毁时清理序列，防止对象被删后 DOTween 继续动已销毁的 Transform。</summary>
    private void OnDestroy()
    {
        SkipIntro();
    }
}
