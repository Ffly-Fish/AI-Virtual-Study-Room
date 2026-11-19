using TMPro;
using UnityEngine;

/// <summary>
/// 换装与名字牌：按 SkinDefinition 整只替换角色模型，并把名字写到头顶世界 UI。
/// 用户要求"整套 FBX 替换"，所以这里销毁旧模型再实例化新模型，
/// 但角色控制器、AnimatorDriver 等逻辑组件挂在角色根节点上，不受换装影响。
/// </summary>
public class SkinVisualBinder : MonoBehaviour
{
    /// <summary>模型挂载点：换装时只清理这个节点的子物体。</summary>
    [Header("模型挂载点")]
    [Tooltip("角色模型实例化的父节点，换装时会清空它的子物体；留空则用本物体")]
    [SerializeField] private Transform _skinRoot;

    /// <summary>共享动画控制器：所有皮肤模型骨骼同源，换装后统一挂同一个 controller。</summary>
    [Header("动画控制器")]
    [Tooltip("所有皮肤模型共用的 Animator Controller（项目里是 PlayerAnimator.controller）")]
    [SerializeField] private RuntimeAnimatorController _sharedController;

    /// <summary>目标身高（米）：把皮肤模型整体缩放到这个高度。</summary>
    [Header("目标身高")]
    [Tooltip("皮肤模型会被缩放到这个身高（米）。房间是约 2 倍尺度的低多边形资产（桌面高 1.38 米），角色保持 3.4 米才与家具成比例")]
    [SerializeField] private float _targetHeight = 3.4f;

    /// <summary>头顶名字牌文本。</summary>
    [Header("名字牌")]
    [Tooltip("显示用户名与专注状态的文本组件，留空则不显示名字")]
    [SerializeField] private TMP_Text _nameLabel;

    /// <summary>当前实例化的模型，换装时先销毁。</summary>
    private GameObject _currentModel;

    /// <summary>当前模型上的 Animator，换装后交给 PlayerAnimatorDriver 重新绑定。</summary>
    public Animator CurrentAnimator { get; private set; }

    /// <summary>启动时校正挂载点，避免用户忘记拖引用导致换装清空整只角色。</summary>
    private void Awake()
    {
        if (_skinRoot == null)
        {
            _skinRoot = transform;
        }
    }

    /// <summary>注入模型挂载点，供运行时构建角色模板时使用。</summary>
    public void SetSkinRoot(Transform skinRoot)
    {
        _skinRoot = skinRoot;
    }

    /// <summary>注入共享动画控制器，确保换装后的模型仍使用同一套状态机。</summary>
    public void SetSharedController(RuntimeAnimatorController controller)
    {
        _sharedController = controller;
    }

    /// <summary>按皮肤定义换装；skin 为空或没有模型时保留当前外观并警告。</summary>
    public void Bind(SkinDefinition skin)
    {
        if (skin == null || skin.ModelPrefab == null)
        {
            Debug.LogWarning("[SkinVisualBinder] 皮肤定义为空或缺少模型预制体，保持当前外观。", this);
            return;
        }

        if (_currentModel != null)
        {
            Destroy(_currentModel);
        }

        _currentModel = Instantiate(skin.ModelPrefab, _skinRoot);
        _currentModel.transform.localPosition = Vector3.zero;
        _currentModel.transform.localRotation = Quaternion.identity;

        NormalizeModelHeight(_currentModel);

        CurrentAnimator = _currentModel.GetComponentInChildren<Animator>(true);
        if (CurrentAnimator == null)
        {
            // 项目里的角色 FBX 只导入了骨骼与蒙皮，没有随模型带 Animator，
            // 这里在模型根节点补一个；Generic 剪辑按骨骼路径绑定，不需要 Avatar。
            CurrentAnimator = _currentModel.AddComponent<Animator>();
            CurrentAnimator.applyRootMotion = false;
        }

        if (_sharedController != null)
        {
            // 皮肤之间骨骼名同源，直接复用同一个 controller 即可保持坐下/站起过渡一致。
            CurrentAnimator.runtimeAnimatorController = _sharedController;
        }

        PlayerAnimatorDriver driver = GetComponent<PlayerAnimatorDriver>();
        if (driver != null)
        {
            driver.Rebind(CurrentAnimator);
        }
    }

    /// <summary>按渲染包围盒把模型缩放到 _targetHeight，避免不同皮肤身高差异。</summary>
    private void NormalizeModelHeight(GameObject model)
    {
        if (model == null || _targetHeight <= 0.01f)
        {
            return;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        if (bounds.size.y <= 0.01f)
        {
            return;
        }

        float scale = _targetHeight / bounds.size.y;
        model.transform.localScale *= scale;
    }

    /// <summary>设置头顶名字；优先写面板指定的 TMP 文本，否则转发给运行时生成的世界 UI。</summary>
    public void SetName(string displayName)
    {
        if (_nameLabel != null)
        {
            _nameLabel.text = displayName ?? string.Empty;
            return;
        }

        // 名字牌是运行时创建的（见 PlayerNameTag），这里通过组件查找把名字传下去。
        PlayerNameTag nameTag = GetComponent<PlayerNameTag>();
        if (nameTag != null)
        {
            nameTag.SetName(displayName);
        }
    }

    /// <summary>把专注状态同步给名字牌图标；没有名字牌时静默跳过。</summary>
    public void SetFocusTag(bool isFocusing)
    {
        PlayerNameTag nameTag = GetComponent<PlayerNameTag>();
        if (nameTag != null)
        {
            nameTag.SetFocusing(isFocusing);
        }
    }
}
