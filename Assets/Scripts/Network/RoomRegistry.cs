using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 座位分配（只在服务端跑）：每个玩家进房时按"最小空闲座位号"落座，
/// 掉线立刻释放，保证后进来的人能补上空位；座位号写进 PlayerNetwork 同步给所有人，
/// 客户端据此把角色摆到对应座位上，不依赖任何本地顺序。
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

    /// <summary>服务端是否正在运行，避免各处重复判空。</summary>
    private static bool IsServerRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>服务端：给某个玩家分配座位，满员返回 0。</summary>
    public int AssignSeat(ulong clientId)
    {
        if (!IsServerRunning)
        {
            return 0;
        }

        for (int seat = 1; seat <= _maxPlayers; seat++)
        {
            if (!_occupiedSeats.ContainsKey(seat))
            {
                _occupiedSeats[seat] = clientId;
                PlayerCountSnapshot = _occupiedSeats.Count;
                return seat;
            }
        }

        return 0;
    }

    /// <summary>服务端：释放某个玩家的座位。</summary>
    public void ReleaseSeat(ulong clientId)
    {
        if (!IsServerRunning)
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
