using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 座位分配（只在服务端跑）：每个玩家进房时按"最小空闲座位号"落座，
/// 掉线立刻释放，保证后进来的人能补上空位；座位号写进 PlayerNetwork 同步给所有人，
/// 客户端据此把角色摆到对应座位上，不依赖任何本地顺序。
/// 另外记录"会话号 → 座位"的对应表，房主迁移后原班人马可以回到自己的位子。
/// 注意：本组件挂在 NetworkManager 对象上，不是 NetworkBehaviour，
/// 因为它要在任何玩家实体生成之前就能回答"第几号座位空着"。
/// </summary>
public class RoomRegistry : MonoBehaviour
{
    /// <summary>座位总数，与场景里的 SeatMarker 数量保持一致。</summary>
    [Header("房间规则")]
    [Tooltip("房间最多几个人，必须与场景里 SeatMarker 的数量一致")]
    [SerializeField] private int _maxPlayers = 4;

    /// <summary>已占用的座位号，值为占用的 clientId。</summary>
    private readonly Dictionary<int, ulong> _occupiedSeats = new Dictionary<int, ulong>();

    /// <summary>会话号 → 座位号：房主换人后靠它把老玩家放回原位。</summary>
    private readonly Dictionary<string, int> _seatBySession = new Dictionary<string, int>();

    /// <summary>下一个进房者的顺序号，越小代表进房越早，房主迁移时由它决定谁来接管。</summary>
    private int _nextJoinOrder = 1;

    /// <summary>房间人数上限。</summary>
    public int MaxPlayers => _maxPlayers;

    /// <summary>当前人数（只在主线程读写）。</summary>
    public int PlayerCount => _occupiedSeats.Count;

    /// <summary>
    /// 供后台线程安全读取的人数快照。
    /// 局域网发现的监听线程不是主线程，那边调用 FindObjectOfType 会直接抛异常，
    /// 所以把人数缓存在一个普通 int 里，让回执线程只读这个字段。
    /// </summary>
    public int PlayerCountSnapshot { get; private set; }

    /// <summary>
    /// 是否有可用的网络管理器。
    /// 注意不能用 NetworkManager.IsServer 当门槛：房主自己的玩家对象是在 StartHost 内部生成的，
    /// 那一刻服务端角色标记还没完全生效，用它会误判成"非服务端"而分配不到座位。
    /// 这些方法只会被服务端的生成/断开回调调用，判空就足够。
    /// </summary>
    private static bool HasNetworkManager => NetworkManager.Singleton != null;

    /// <summary>服务端：给某个玩家分配座位，满员返回 0。</summary>
    public int AssignSeat(ulong clientId)
    {
        return AssignSeat(clientId, string.Empty, 0);
    }

    /// <summary>
    /// 服务端：优先给"想要的座位"（房主迁移后回到原位），被占或越界再退到最小空闲座位。
    /// sessionId 会与座位一起记住，下一次迁移继续沿用。
    /// </summary>
    public int AssignSeat(ulong clientId, string sessionId, int preferredSeat)
    {
        if (!HasNetworkManager)
        {
            return 0;
        }

        int seat = 0;

        // 优先：本人指定且当前空着。
        if (preferredSeat >= 1 && preferredSeat <= _maxPlayers && !_occupiedSeats.ContainsKey(preferredSeat))
        {
            seat = preferredSeat;
        }
        else if (!string.IsNullOrEmpty(sessionId)
                 && _seatBySession.TryGetValue(sessionId, out int remembered)
                 && remembered >= 1 && remembered <= _maxPlayers
                 && !_occupiedSeats.ContainsKey(remembered))
        {
            // 次选：这个会话上次坐过的位子还空着。
            seat = remembered;
        }
        else
        {
            for (int candidate = 1; candidate <= _maxPlayers; candidate++)
            {
                if (!_occupiedSeats.ContainsKey(candidate))
                {
                    seat = candidate;
                    break;
                }
            }
        }

        if (seat == 0)
        {
            Debug.LogWarning("[RoomRegistry] 座位已满，无法分配：" + _occupiedSeats.Count + "/" + _maxPlayers);
            return 0;
        }

        _occupiedSeats[seat] = clientId;
        if (!string.IsNullOrEmpty(sessionId))
        {
            _seatBySession[sessionId] = seat;
        }

        PlayerCountSnapshot = _occupiedSeats.Count;
        return seat;
    }

    /// <summary>
    /// 服务端：把某个玩家挪到指定座位（房主迁移后回到原位用）。
    /// 目标位被自己或空位时成功，被别人占着就保持不动。
    /// </summary>
    public bool TryMoveSeat(ulong clientId, int targetSeat, string sessionId)
    {
        if (!HasNetworkManager || targetSeat < 1 || targetSeat > _maxPlayers)
        {
            return false;
        }

        if (_occupiedSeats.TryGetValue(targetSeat, out ulong holder) && holder != clientId)
        {
            return false;
        }

        if (_occupiedSeats.TryGetValue(targetSeat, out holder) && holder == clientId)
        {
            return true;
        }

        ReleaseSeat(clientId);
        _occupiedSeats[targetSeat] = clientId;
        if (!string.IsNullOrEmpty(sessionId))
        {
            _seatBySession[sessionId] = targetSeat;
        }

        PlayerCountSnapshot = _occupiedSeats.Count;
        return true;
    }

    /// <summary>服务端：释放某个玩家的座位。</summary>
    public void ReleaseSeat(ulong clientId)
    {
        if (!HasNetworkManager)
        {
            return;
        }

        int seatToRemove = 0;
        foreach (KeyValuePair<int, ulong> pair in _occupiedSeats)
        {
            if (pair.Value == clientId)
            {
                seatToRemove = pair.Key;
                break;
            }
        }

        if (seatToRemove != 0)
        {
            _occupiedSeats.Remove(seatToRemove);
            PlayerCountSnapshot = _occupiedSeats.Count;
        }
    }

    /// <summary>服务端：取一个"进房顺序号"，越小越早，房主迁移时最早进房的人接管。</summary>
    public int TakeJoinOrder()
    {
        return _nextJoinOrder++;
    }

    /// <summary>服务端：房间是否已满。</summary>
    public bool IsFull => _occupiedSeats.Count >= _maxPlayers;

    /// <summary>掉线即腾座位，后来的人可以补位。</summary>
    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    /// <summary>退场时取消回调。</summary>
    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    /// <summary>玩家掉线：释放他占着的座位。</summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        ReleaseSeat(clientId);
    }

    // TODO: 联机主流程跑通后，把 _maxPlayers 改成从 SeatRegistry 自动读取，避免两处配置不一致。
}
