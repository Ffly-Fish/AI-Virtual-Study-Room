using UnityEngine;

/// <summary>
/// 角色动作 A/B 的驱动器：专注时坐下（isStudy = true），否则站起（isStudy = false）。
/// 动画由 Assets/Animator/PlayerAnimator.controller 里的 StandUp / SitDown 状态负责，
/// 这里只负责把布尔值写进 Animator，不直接播放状态，避免打断过渡。
/// </summary>
public class PlayerAnimatorDriver : MonoBehaviour
{
    /// <summary>目标 Animator；留空则在 Awake 时从子物体里找。</summary>
    [Header("动画组件")]
    [Tooltip("角色身上的 Animator，留空会自动从子物体查找")]
    [SerializeField] private Animator _animator;

    /// <summary>控制坐/站的状态机布尔参数名，必须与 PlayerAnimator.controller 一致。</summary>
    [Header("状态机参数")]
    [Tooltip("Animator 里控制坐下/站起的布尔参数名，默认 isStudy")]
    [SerializeField] private string _studyParamName = "isStudy";

    /// <summary>参数名对应的哈希，避免每帧做字符串查找。</summary>
    private int _studyParamHash;

    /// <summary>哈希是否已初始化；换装会在 Awake 之前发生在模板上，所以必须惰性初始化。</summary>
    private bool _isHashReady;

    /// <summary>是否已经把值写进 Animator；模型重新绑定时需要重写一次。</summary>
    private bool _hasApplied;

    /// <summary>是否已经提示过"没有 Animator"，避免每次换装都刷日志。</summary>
    private bool _hasWarnedMissingAnimator;

    /// <summary>当前是否处于专注（坐下）状态。</summary>
    private bool _isFocusing;

    /// <summary>是否因为模型还没激活而暂缓写入；激活后由 OnEnable 补写。</summary>
    private bool _isApplyPending;

    /// <summary>启动时尝试取 Animator；模型可能还没实例化，取不到不报错，等 Rebind。</summary>
    private void Awake()
    {
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>(true);
        }

        EnsureHash();
    }

    /// <summary>
    /// 角色模板是"先换装再激活"的：激活前的 Animator 写 SetBool 会报
    /// "Animator is not playing an AnimatorController"，因此激活时补写一次暂缓的状态。
    /// </summary>
    private void OnEnable()
    {
        if (_isApplyPending)
        {
            ApplyIsStudy(_isFocusing);
        }
    }

    /// <summary>哈希惰性初始化，保证在 Awake 之前调用也安全。</summary>
    private void EnsureHash()
    {
        if (_isHashReady)
        {
            return;
        }

        _studyParamHash = Animator.StringToHash(_studyParamName);
        _isHashReady = true;
    }

    /// <summary>换装后由 <see cref="SkinVisualBinder"/> 调用，把驱动器指向新的 Animator。</summary>
    public void Rebind(Animator animator)
    {
        _animator = animator;
        _hasApplied = false;
        _hasWarnedMissingAnimator = false;
        EnsureHash();
        ApplyIsStudy(_isFocusing);
    }

    /// <summary>设置专注状态；值没变化且已写入过时直接返回，避免每帧 SetBool。</summary>
    public void SetFocusing(bool isFocusing)
    {
        if (_isFocusing == isFocusing && _hasApplied)
        {
            return;
        }

        _isFocusing = isFocusing;
        ApplyIsStudy(_isFocusing);
    }

    /// <summary>真正写 Animator 的布尔值；没有 Animator 时只警告一次。</summary>
    private void ApplyIsStudy(bool isStudy)
    {
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>(true);
        }

        if (_animator == null)
        {
            if (!_hasWarnedMissingAnimator)
            {
                _hasWarnedMissingAnimator = true;
                Debug.LogWarning("[PlayerAnimatorDriver] 还没有可用的 Animator，角色暂时不会做动作 A/B。", this);
            }

            return;
        }

        // 未激活的对象上 Animator 还没挂载控制器，写入会被丢弃并刷警告，先暂缓。
        if (!_animator.gameObject.activeInHierarchy || _animator.runtimeAnimatorController == null)
        {
            _isApplyPending = true;
            _hasApplied = false;
            return;
        }

        EnsureHash();
        _animator.SetBool(_studyParamHash, isStudy);
        _hasApplied = true;
        _isApplyPending = false;
    }
}
