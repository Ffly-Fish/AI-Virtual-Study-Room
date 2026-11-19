using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 房主迁移的花名册：记录每个玩家的进房顺序、会话号与座位。
/// Netcode 没有内置的房主迁移，本组件只负责回答两个问题：
/// 1) 房主是不是真的走了（客户端本地连接断开 = 服务端没了）；
/// 2) 如果走了，本机是不是"最早进房的人"，也就是该由谁接管。
/// 真正的重建动作（退房 → 重新 StartHost / 重新搜索加入）交给流程层，
/// 因为那边才知道相机、HUD、角色该怎么收尾。
/// </summary>
public class HostMigrationService : MonoBehaviour
{
    /// <summary>单条花名册记录：进房顺序、会话号、座位号。</summary>
    private struct RosterEntry
    {
        /// <summary>进房顺序，越小越早。</summary>
        public int JoinOrder;

        /// <summary>会话号，跨房主迁移保持不变。</summary>
        public string SessionId;

        /// <summary>座位号，1 起始。</summary>
        public int SeatIndex;
    }

    /// <summary>玩家实体生成时用来做登记的便捷入口（场景里只有一个实例）。</summary>
    public static HostMigrationService Service { get; private set; }

    /// <summary>判定房主掉线后触发，参数 = 本机是否应该接管房间。</summary>
    public event Action<bool> OnHostLost;

    /// <summary>心跳超时阈值（秒）：3 秒收不到房主心跳就认为房主已经走了。</summary>
    private const float BeatTimeoutSeconds = 3f;

    /// <summary>最近一次收到房主心跳的本地时间。</summary>
    private float _lastBeatTime;

    /// <summary>是否已经开始收心跳（没有心跳过说明还没进房，不做判定）。</summary>
    private bool _hasBeat;

    /// <summary>是否已经报过一次掉线，避免同一轮重复触发。</summary>
    private bool _hasRaisedHostLost;

    /// <summary>迁移过程中关掉看门狗，避免自己主动退房又触发一次。</summary>
    public bool WatchdogEnabled { get; set; } = true;

    /// <summary>clientId → 花名册记录。</summary>
    private readonly Dictionary<ulong, RosterEntry> _roster = new Dictionary<ulong, RosterEntry>();

    /// <summary>取单例并订阅心跳；场景切换不会重建它。</summary>
    private void Awake()
    {
        Service = this;
        PlayerNetwork.OnHostBeat += HandleHostBeat;
        ResetWatchdog();
    }

    /// <summary>清掉看门狗的计时与状态，进房/接管后重新开始计时。</summary>
    public void ResetWatchdog()
    {
        _lastBeatTime = Time.unscaledTime;
        _hasBeat = false;
        _hasRaisedHostLost = false;
        WatchdogEnabled = true;
    }

    /// <summary>房主心跳到达：刷新时间戳。</summary>
    private void HandleHostBeat()
    {
        _lastBeatTime = Time.unscaledTime;
        _hasBeat = true;
    }

    /// <summary>
    /// 客户端看门狗：超过阈值没收到房主心跳 → 判定房主掉线并通知流程层。
    /// Netcode 自己的断开检测要靠传输层超时，实测 20 秒以上才报，所以必须自己计时。
    /// </summary>
    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!WatchdogEnabled || _hasRaisedHostLost || manager == null || manager.IsServer || !_hasBeat)
        {
            return;
        }

        if (Time.unscaledTime - _lastBeatTime > BeatTimeoutSeconds)
        {
            _hasRaisedHostLost = true;
            bool takeOver = ShouldTakeOver();
            Debug.Log("[HostMigrationService] 房主心跳超时，本机是否接管：" + takeOver);
            OnHostLost?.Invoke(takeOver);
        }
    }

    /// <summary>退场时清掉单例引用。</summary>
    private void OnDestroy()
    {
        PlayerNetwork.OnHostBeat -= HandleHostBeat;

        if (Service == this)
        {
            Service = null;
        }
    }

    /// <summary>登记/更新一个玩家的花名册信息。</summary>
    public void Track(ulong clientId, int joinOrder, string sessionId, int seatIndex)
    {
        _roster[clientId] = new RosterEntry
        {
            JoinOrder = joinOrder,
            SessionId = sessionId,
            SeatIndex = seatIndex
        };
    }

    /// <summary>玩家离开时从花名册摘掉。</summary>
    public void Untrack(ulong clientId)
    {
        _roster.Remove(clientId);
    }

    /// <summary>清空花名册（换房主后重建时用）。</summary>
    public void ResetRoster()
    {
        _roster.Clear();
    }

    /// <summary>本机的进房顺序，未登记时返回最大值（等于"最晚"，不会接管）。</summary>
    public int LocalJoinOrder
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return int.MaxValue;
            }

            return _roster.TryGetValue(manager.LocalClientId, out RosterEntry entry) ? entry.JoinOrder : int.MaxValue;
        }
    }

    /// <summary>
    /// 本机是否该接管房间：在所有"不是旧房主"的记录里，本机的进房顺序最小。
    /// 并列时用 clientId 兜底，保证同一时刻只有一个人接管。
    /// </summary>
    public bool ShouldTakeOver()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.IsServer)
        {
            return false;
        }

        if (!_roster.TryGetValue(manager.LocalClientId, out RosterEntry mine))
        {
            return false;
        }

        int bestOrder = int.MaxValue;
        ulong bestClientId = ulong.MaxValue;

        foreach (KeyValuePair<ulong, RosterEntry> pair in _roster)
        {
            if (pair.Key == NetworkManager.ServerClientId)
            {
                // 旧房主自己不算候选人。
                continue;
            }

            if (pair.Value.JoinOrder < bestOrder || (pair.Value.JoinOrder == bestOrder && pair.Key < bestClientId))
            {
                bestOrder = pair.Value.JoinOrder;
                bestClientId = pair.Key;
            }
        }

        return manager.LocalClientId == bestClientId;
    }

    /// <summary>按会话号找回座位号，0 表示这个会话以前没坐过。</summary>
    public int GetSeatBySession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return 0;
        }

        foreach (KeyValuePair<ulong, RosterEntry> pair in _roster)
        {
            if (pair.Value.SessionId == sessionId)
            {
                return pair.Value.SeatIndex;
            }
        }

        return 0;
    }
}
