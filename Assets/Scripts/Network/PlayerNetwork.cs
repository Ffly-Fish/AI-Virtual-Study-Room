using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 每个玩家的网络身份：名字、座位号、皮肤、专注状态，以及用于房主迁移的会话号与进房顺序。
/// 本人只写自己的专注状态与旁观标记，其余字段由服务端写；
/// 任何字段变化都通知视觉层，保证别人看到的动作 A/B、皮肤和名字与自己一致。
/// 座位与视觉都在本地按座位号推导，位置不占带宽。
/// </summary>
public class PlayerNetwork : NetworkBehaviour
{
    /// <summary>本机玩家的角色生成（流程层据此收尾入场）。</summary>
    public static event Action<PlayerNetwork> OnLocalPlayerReady;

    /// <summary>本机玩家的角色销毁（退房）。</summary>
    public static event Action OnLocalPlayerGone;

    [Header("视觉资源")]
    [Tooltip("皮肤总表，用来把 SkinId 换成模型")]
    [SerializeField] private SkinCatalog _skinCatalog;

    [Tooltip("所有皮肤共用的动画控制器（PlayerAnimator.controller）")]
    [SerializeField] private RuntimeAnimatorController _animatorController;

    [Header("场景引用")]
    [Tooltip("座位总表，留空会在运行时自动查找")]
    [SerializeField] private SeatRegistry _seatRegistry;

    /// <summary>座位表缓存，避免每次查找。</summary>
    private SeatRegistry _resolvedSeats;

    /// <summary>本地角色模型绑定器。</summary>
    private SkinVisualBinder _binder;

    /// <summary>本地动作驱动。</summary>
    private PlayerAnimatorDriver _animatorDriver;

    /// <summary>头顶名字牌。</summary>
    private PlayerNameTag _nameTag;

    /// <summary>玩家名字，最长 63 字节。</summary>
    private readonly NetworkVariable<FixedString64Bytes> _displayName =
        new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>座位号，1 起始，0 表示还没分配。</summary>
    private readonly NetworkVariable<int> _seatIndex =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>皮肤编号，与 SkinCatalog 的 Id 一致。</summary>
    private readonly NetworkVariable<byte> _skinId =
        new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>是否在看屏幕，本人写、所有人读。</summary>
    private readonly NetworkVariable<bool> _isFocusing =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>是否旁观（没有摄像头权限）：旁观不参与专注计时，但同样占一个座位。</summary>
    private readonly NetworkVariable<bool> _isSpectator =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>进房顺序，越小越早；房主掉线时由这个值决定谁来接管房间。</summary>
    private readonly NetworkVariable<int> _joinOrder =
        new NetworkVariable<int>(int.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// 房主心跳：服务端每 0.5 秒写一次当前时间。
    /// 客户端靠它判断"房主还在不在"，比等传输层超时（实测要 20 秒以上）快得多。
    /// </summary>
    private readonly NetworkVariable<float> _hostBeat =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>收到房主心跳（客户端用），参数是写入时的本地时间。</summary>
    public static event Action OnHostBeat;

    /// <summary>心跳写入间隔（秒）。</summary>
    private const float BeatIntervalSeconds = 0.5f;

    /// <summary>本机计时，用于节流心跳写入。</summary>
    private float _beatTimer;

    /// <summary>本机进程的会话号，房主换人后靠它认出"还是我"。</summary>
    private readonly NetworkVariable<FixedString64Bytes> _sessionId =
        new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>名字。</summary>
    public string DisplayName => _displayName.Value.ToString();

    /// <summary>座位号。</summary>
    public int SeatIndex => _seatIndex.Value;

    /// <summary>皮肤编号。</summary>
    public byte SkinId => _skinId.Value;

    /// <summary>是否专注。</summary>
    public bool IsFocusing => _isFocusing.Value;

    /// <summary>是否旁观。</summary>
    public bool IsSpectator => _isSpectator.Value;

    /// <summary>进房顺序号。</summary>
    public int JoinOrder => _joinOrder.Value;

    /// <summary>会话号。</summary>
    public string SessionId => _sessionId.Value.ToString();

    /// <summary>本机是不是这个角色的主人。</summary>
    public bool IsLocalPlayerOwner => IsSpawned && IsOwner;

    /// <summary>
    /// 服务端每 0.5 秒写一次心跳，只有房主自己的角色对象会写。
    /// 客户端靠它判断房主是否还在，不必等传输层几十秒的超时。
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer || OwnerClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        _beatTimer += Time.unscaledDeltaTime;
        if (_beatTimer < BeatIntervalSeconds)
        {
            return;
        }

        _beatTimer = 0f;
        _hostBeat.Value = Time.unscaledTime;
    }

    /// <summary>客户端收到房主心跳：通知迁移服务"房主还活着"。</summary>
    private void HandleHostBeatChanged(float previous, float current)
    {
        if (!IsServer && OwnerClientId == NetworkManager.ServerClientId)
        {
            OnHostBeat?.Invoke();
        }
    }

    /// <summary>生成时定位组件、订阅变化，服务端还需要分配座位。</summary>
    public override void OnNetworkSpawn()
    {
        // 必须在服务端分配座位之前取走：接管房间时服务端会先给一个临时空位，
        // 那个临时值不能覆盖"我原来坐哪儿"。
        int preferredSeat = PlayerIdentity.TakePreferredSeat();

        _binder = GetComponent<SkinVisualBinder>();
        _animatorDriver = GetComponent<PlayerAnimatorDriver>();
        _nameTag = GetComponent<PlayerNameTag>();

        _displayName.OnValueChanged += HandleDisplayNameChanged;
        _seatIndex.OnValueChanged += HandleSeatChanged;
        _skinId.OnValueChanged += HandleSkinChanged;
        _isFocusing.OnValueChanged += HandleFocusingChanged;
        _joinOrder.OnValueChanged += HandleRosterChanged;
        _sessionId.OnValueChanged += HandleRosterChanged;
        _hostBeat.OnValueChanged += HandleHostBeatChanged;

        if (IsServer)
        {
            RoomRegistry registry = FindObjectOfType<RoomRegistry>();
            int seat = registry != null ? registry.AssignSeat(OwnerClientId) : 1;
            _seatIndex.Value = seat;

            // 进房顺序号：房主自己是 1，之后按连接先后递增。
            _joinOrder.Value = registry != null ? registry.TakeJoinOrder() : 1;

            if (seat == 0)
            {
                // 满员：直接把这个人踢掉，客户端会收到断开回调并提示房间已满。
                Debug.LogWarning("[PlayerNetwork] 房间已满，拒绝客户端 " + OwnerClientId);
                NetworkManager.Singleton.DisconnectClient(OwnerClientId, "房间已满");
                return;
            }
        }

        if (IsOwner)
        {
            SubmitProfileServerRpc(
                PrefsStore.LoadPlayerName(),
                (byte)PrefsStore.LoadEquippedSkin(),
                PlayerIdentity.SessionId,
                preferredSeat);
        }

        HandleSeatChanged(0, _seatIndex.Value);
        HandleSkinChanged(0, _skinId.Value);
        HandleDisplayNameChanged(default, _displayName.Value);
        HandleFocusingChanged(false, _isFocusing.Value);
        TrackRoster();
    }

    /// <summary>退场时取消订阅并通知流程层清理。</summary>
    public override void OnNetworkDespawn()
    {
        _displayName.OnValueChanged -= HandleDisplayNameChanged;
        _seatIndex.OnValueChanged -= HandleSeatChanged;
        _skinId.OnValueChanged -= HandleSkinChanged;
        _isFocusing.OnValueChanged -= HandleFocusingChanged;
        _joinOrder.OnValueChanged -= HandleRosterChanged;
        _sessionId.OnValueChanged -= HandleRosterChanged;
        _hostBeat.OnValueChanged -= HandleHostBeatChanged;

        HostMigrationService.Service?.Untrack(OwnerClientId);

        if (IsOwner)
        {
            OnLocalPlayerGone?.Invoke();
        }
    }

    /// <summary>
    /// 本人上报名字、皮肤、会话号与期望座位。
    /// 期望座位用于房主迁移后回到原位：新服务端据此把老玩家放回原来的椅子。
    /// </summary>
    [ServerRpc]
    private void SubmitProfileServerRpc(FixedString64Bytes displayName, byte skinId, FixedString64Bytes sessionId, int preferredSeat)
    {
        _displayName.Value = displayName;
        _skinId.Value = skinId;
        _sessionId.Value = sessionId;

        if (preferredSeat > 0 && preferredSeat != _seatIndex.Value)
        {
            RoomRegistry registry = FindObjectOfType<RoomRegistry>();
            if (registry != null && registry.TryMoveSeat(OwnerClientId, preferredSeat, sessionId.ToString()))
            {
                _seatIndex.Value = preferredSeat;
            }
        }
    }

    /// <summary>本人：上报专注状态，其他人靠这个播放动作 A/B。</summary>
    public void SetFocusing(bool isFocusing)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _isFocusing.Value = isFocusing;
    }

    /// <summary>本人：标记本次是否旁观（没有摄像头权限时不参与专注计时）。</summary>
    public void SetSpectator(bool isSpectator)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _isSpectator.Value = isSpectator;
    }

    /// <summary>名字变化：写名字牌。</summary>
    private void HandleDisplayNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        string name = current.ToString();
        if (_nameTag == null)
        {
            _nameTag = GetComponent<PlayerNameTag>();
        }

        _nameTag?.SetName(name);
    }

    /// <summary>座位变化：所有人把角色摆到同一个座位，主人再通知流程层飞相机。</summary>
    private void HandleSeatChanged(int previous, int current)
    {
        if (current <= 0)
        {
            return;
        }

        if (_resolvedSeats == null)
        {
            _resolvedSeats = _seatRegistry != null ? _seatRegistry : FindObjectOfType<SeatRegistry>();
        }

        SeatMarker marker = _resolvedSeats != null ? _resolvedSeats.GetMarker(current) : null;
        if (marker != null)
        {
            transform.SetPositionAndRotation(marker.SeatPosition, marker.SeatRotation);
        }

        if (IsOwner)
        {
            // 记住自己的座位号：房主迁移后新服务端会问"你原来坐哪儿"。
            PlayerIdentity.RememberSeat(current);
            TrackRoster();
            OnLocalPlayerReady?.Invoke(this);
        }
    }

    /// <summary>皮肤变化：换模型（整套 FBX 替换）。</summary>
    private void HandleSkinChanged(byte previous, byte current)
    {
        if (_binder == null)
        {
            _binder = GetComponent<SkinVisualBinder>();
        }

        if (_binder == null || _skinCatalog == null)
        {
            return;
        }

        _binder.SetSharedController(_animatorController);
        _binder.Bind(_skinCatalog.Get(current));
    }

    /// <summary>专注状态变化：播放坐下/站起，并切换名字牌的状态色。</summary>
    private void HandleFocusingChanged(bool previous, bool current)
    {
        if (_animatorDriver == null)
        {
            _animatorDriver = GetComponent<PlayerAnimatorDriver>();
        }

        if (_nameTag == null)
        {
            _nameTag = GetComponent<PlayerNameTag>();
        }

        _animatorDriver?.SetFocusing(current);
        _nameTag?.SetFocusing(current);
    }

    /// <summary>会话号或进房顺序变化：刷新房主迁移用的花名册缓存。</summary>
    private void HandleRosterChanged(int previous, int current)
    {
        TrackRoster();
    }

    /// <summary>会话号变化同样要刷新花名册（复用上面的回调签名）。</summary>
    private void HandleRosterChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        TrackRoster();
    }

    /// <summary>把自己登记到房主迁移服务的缓存里，房主掉线后靠这份缓存决定接任者。</summary>
    private void TrackRoster()
    {
        if (!IsSpawned)
        {
            return;
        }

        HostMigrationService.Service?.Track(OwnerClientId, _joinOrder.Value, SessionId, _seatIndex.Value);
    }
}
