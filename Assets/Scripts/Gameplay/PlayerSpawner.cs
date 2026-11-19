using UnityEngine;

/// <summary>
/// 角色生成器（阶段 1 只生成本地玩家；阶段 2 由网络层调用同一套流程）。
/// 生成顺序遵循用户要求：先在座位站位生成角色，再让相机飞过去。
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    /// <summary>角色预制体：根节点挂 SkinVisualBinder / PlayerAnimatorDriver，子节点挂模型。</summary>
    [Header("角色预制体")]
    [Tooltip("玩家角色预制体（含 SkinVisualBinder 与 PlayerAnimatorDriver）")]
    [SerializeField] private GameObject _playerPrefab;

    /// <summary>皮肤总表，用于按 SkinId 找到模型。</summary>
    [Header("皮肤配置")]
    [Tooltip("皮肤总表资产，用于把 SkinId 换成具体模型")]
    [SerializeField] private SkinCatalog _skinCatalog;

    /// <summary>座位来源。</summary>
    [Header("座位来源")]
    [Tooltip("座位总表，留空会自动在场景里查找")]
    [SerializeField] private SeatRegistry _seatRegistry;

    /// <summary>运行时构建的角色模板：与 _playerPrefab 二选一，优先生效。</summary>
    private GameObject _playerTemplate;

    /// <summary>生成出来的本地角色，供相机与 UI 引用。</summary>
    public GameObject LocalPlayer { get; private set; }

    /// <summary>注入运行时构建的角色模板，代替预制体；用于阶段 1 零资源依赖的搭建方式。</summary>
    public void SetPlayerTemplate(GameObject template)
    {
        _playerTemplate = template;
    }

    /// <summary>注入皮肤总表，供运行时组合根（GameBootstrap）使用。</summary>
    public void SetSkinCatalog(SkinCatalog catalog)
    {
        _skinCatalog = catalog;
    }

    /// <summary>启动时兜底查找座位表，减少场景接线。</summary>
    private void Awake()
    {
        if (_seatRegistry == null)
        {
            _seatRegistry = FindObjectOfType<SeatRegistry>();
        }
    }

    /// <summary>在指定座位生成本地角色；重复调用会先销毁旧角色，避免同屏两个自己。</summary>
    public GameObject SpawnLocalPlayer(SeatMarker seat, byte skinId, string displayName)
    {
        GameObject template = _playerTemplate != null ? _playerTemplate : _playerPrefab;
        if (template == null || seat == null)
        {
            Debug.LogError("[PlayerSpawner] 缺少角色预制体或座位，无法生成角色。", this);
            return null;
        }

        if (LocalPlayer != null)
        {
            Destroy(LocalPlayer);
        }

        LocalPlayer = Instantiate(template, seat.SeatPosition, seat.SeatRotation, transform);

        // 先换装再激活：模板激活时 Animator 已经指向真实模型，避免第一帧找不到动画。
        SkinVisualBinder binder = LocalPlayer.GetComponent<SkinVisualBinder>();
        if (binder != null)
        {
            if (_skinCatalog == null)
            {
                Debug.LogError("[PlayerSpawner] 没有注入皮肤总表，角色将没有模型；请设置 SkinCatalog。", this);
            }

            SkinDefinition skin = _skinCatalog != null ? _skinCatalog.Get(skinId) : null;
            binder.Bind(skin);
            binder.SetName(displayName);
        }
        else
        {
            Debug.LogWarning("[PlayerSpawner] 角色模板上没有 SkinVisualBinder，皮肤与名字牌不会生效。", this);
        }

        LocalPlayer.SetActive(true);
        return LocalPlayer;
    }

    /// <summary>销毁本地角色（退出房间或掉线时调用）。</summary>
    public void DespawnLocalPlayer()
    {
        if (LocalPlayer != null)
        {
            Destroy(LocalPlayer);
            LocalPlayer = null;
        }
    }
}
