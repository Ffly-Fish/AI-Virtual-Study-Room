using System;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// 玩家身份：一次进程运行内固定不变的会话号。
/// Netcode 的 ClientId 在房主迁移后会重新分配，座位与皮肤要跨"换房主"保持，
/// 就必须有一个不随连接变化的标识，这里用启动时生成一次的 GUID。
/// </summary>
public static class PlayerIdentity
{
    /// <summary>本机会话号，进程内首次访问时生成，之后不再变化。</summary>
    public static FixedString64Bytes SessionId { get; } = CreateSessionId();

    /// <summary>
    /// 本机上一次坐过的座位号（0 表示无所谓）。
    /// 房主迁移后新服务端会问"你原来坐哪儿"，靠它把老玩家放回原位，所以必须放在进程级。
    /// </summary>
    public static int PreferredSeat { get; private set; }

    /// <summary>记下本机当前坐的座位号（由 PlayerNetwork 在座位变化时写入）。</summary>
    public static void RememberSeat(int seatIndex)
    {
        if (seatIndex > 0)
        {
            PreferredSeat = seatIndex;
        }
    }

    /// <summary>
    /// 取走"上次座位"并清空。
    /// 只取一次是关键：接管房间时服务端会先临时分配一个空位，
    /// 如果那次临时分配又写回这个字段，原来的座位就永远找不回来了。
    /// </summary>
    public static int TakePreferredSeat()
    {
        int seat = PreferredSeat;
        PreferredSeat = 0;
        return seat;
    }

    /// <summary>用 GUID 的前 32 个字符作为会话号（FixedString64Bytes 放得下）。</summary>
    private static FixedString64Bytes CreateSessionId()
    {
        string raw = Guid.NewGuid().ToString("N");
        FixedString64Bytes result = default;
        result.CopyFromTruncated(raw);
        return result;
    }
}
