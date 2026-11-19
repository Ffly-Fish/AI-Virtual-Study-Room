using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 局域网房间发现：房主监听 UDP 探测包并回执，其他玩家广播探测包，
/// 收到回执就用回执里的 IP 去连。全局域网约定只有一个房间，所以不必做房间列表。
/// 广播在部分路由器/防火墙下会被丢，按用户约定不提供手动输 IP 的兜底。
/// </summary>
public class LanDiscoveryService : MonoBehaviour
{
    /// <summary>UDP 端口（与 Unity Transport 的端口一致，方便同一套防火墙规则）。</summary>
    public const ushort Port = 47777;

    /// <summary>探测包内容，带版本号避免新旧版本互相误认。</summary>
    private const string ProbeMessage = "STUDYROOM_DISCOVER_V1";

    /// <summary>回执包内容。</summary>
    private const string ReplyMessage = "STUDYROOM_ROOM_V1";

    /// <summary>回执里携带的玩家数前缀，形如 "STUDYROOM_ROOM_V1|2"。</summary>
    private const char CountSeparator = '|';

    /// <summary>
    /// 房主监听用的 socket。必须在调用线程里同步创建（在后台线程里创建会出现
    /// "线程还没建好就被 Stop 掉 → socket 泄漏 → 端口被永久占用"的竞态）。
    /// </summary>
    private UdpClient _listener;

    /// <summary>房主模式下的监听线程，退出时必须停掉，否则端口会被占住。</summary>
    private Thread _listenThread;

    /// <summary>监听线程的运行标记。</summary>
    private volatile bool _isListening;

    /// <summary>当前房间里的玩家数，回执时读它。</summary>
    private Func<int> _playerCountProvider;

    /// <summary>是否正在监听（房主）。</summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// 开始监听（房主）。返回 false 表示端口被占用，通常意味着本机已经有一个房主实例
    /// （同一台机器重复开两个客户端时会出现），调用方应把它当成"房间已存在"处理。
    /// </summary>
    public bool StartHostListener(Func<int> playerCountProvider)
    {
        StopHostListener();
        _playerCountProvider = playerCountProvider;

        try
        {
            // 独占绑定：同机第二个实例必须在这里失败，才能走"加入已有房间"的分支。
            // 若允许共用端口，两个实例会同时以为自己是房主，谁也连不上谁。
            _listener = new UdpClient { EnableBroadcast = true, ExclusiveAddressUse = true };
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[LanDiscoveryService] 无法监听 UDP " + Port + "：" + exception.Message, this);
            CloseListener();
            return false;
        }

        _isListening = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "LanDiscoveryHost" };
        _listenThread.Start();
        return true;
    }

    /// <summary>停止监听（房主退出或迁移时）。关闭 socket 并等线程退出，避免端口泄漏。</summary>
    public void StopHostListener()
    {
        _isListening = false;
        CloseListener();

        Thread thread = _listenThread;
        _listenThread = null;
        if (thread != null && thread.IsAlive)
        {
            // 等一小会儿让后台线程从 Receive 里退出来，超时就放弃（守护线程不会阻止退出）。
            thread.Join(200);
        }
    }

    /// <summary>广播探测，最多等 seconds 秒，找到房间就回调地址。</summary>
    public IEnumerator Search(float seconds, Action<string> onFound, Action onNotFound)
    {
        using (UdpClient client = new UdpClient())
        {
            client.EnableBroadcast = true;
            DisableConnectionReset(client);
            byte[] probe = Encoding.UTF8.GetBytes(ProbeMessage);
            IPEndPoint broadcast = new IPEndPoint(IPAddress.Broadcast, Port);

            try
            {
                client.Send(probe, probe.Length, broadcast);

                // 同一台机器上同时开两个实例自测时，广播包不一定回到本机，
                // 再补发一次回环地址，保证"本机既当房主又当客户端"的验证能跑通。
                IPEndPoint loopback = new IPEndPoint(IPAddress.Loopback, Port);
                client.Send(probe, probe.Length, loopback);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LanDiscoveryService] 广播探测失败：" + exception.Message, this);
                onNotFound?.Invoke();
                yield break;
            }

            float deadline = Time.unscaledTime + Mathf.Max(0.2f, seconds);
            while (Time.unscaledTime < deadline)
            {
                if (client.Available > 0)
                {
                    string replyText = string.Empty;
                    string replyAddress = string.Empty;
                    bool received = false;

                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] reply = client.Receive(ref remote);
                        replyText = Encoding.UTF8.GetString(reply);
                        replyAddress = remote.Address.ToString();
                        received = true;
                    }
                    catch (SocketException)
                    {
                        // 目标端口没人监听时 Windows 会回 ICMP 端口不可达，Receive 抛 ConnectionReset。
                        // 这属于"还没人开房"的正常情况，忽略即可。
                    }

                    if (received && replyText.StartsWith(ReplyMessage, StringComparison.Ordinal))
                    {
                        onFound?.Invoke(replyAddress);
                        yield break;
                    }
                }

                yield return null;
            }

            onNotFound?.Invoke();
        }
    }

    /// <summary>
    /// 关掉 Windows 的 UDP 连接重置行为（SIO_UDP_CONNRESET）。
    /// 否则向"没有监听者的端口"发过包之后，同一个 socket 的 Receive 会持续抛 ConnectionReset，
    /// 局域网里还没人开房时必然踩到。
    /// </summary>
    private static void DisableConnectionReset(UdpClient client)
    {
        try
        {
            // 0x9800000C 是 SIO_UDP_CONNRESET，只能通过 IOControl 关闭。
            client.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null);
        }
        catch (Exception)
        {
            // 非 Windows 平台没有这个控制码，忽略即可。
        }
    }

    /// <summary>退房时清理。</summary>
    private void OnDestroy()
    {
        StopHostListener();
    }

    /// <summary>关闭监听 socket（可重复调用）。</summary>
    private void CloseListener()
    {
        UdpClient listener = _listener;
        _listener = null;
        if (listener == null)
        {
            return;
        }

        try
        {
            listener.Close();
        }
        catch (Exception)
        {
            // 关闭已失效的 socket 会抛异常，这里无关紧要。
        }
    }

    /// <summary>房主的后台循环：收到探测就回一个回执。</summary>
    private void ListenLoop()
    {
        UdpClient listener = _listener;
        if (listener == null)
        {
            return;
        }

        while (_isListening)
        {
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref remote);
                string text = Encoding.UTF8.GetString(data);
                if (!text.StartsWith(ProbeMessage, StringComparison.Ordinal))
                {
                    continue;
                }

                // 回执里带上人数，客户端可以先显示"找到 2 人房间"再连接。
                int count = _playerCountProvider != null ? _playerCountProvider() : 0;
                byte[] reply = Encoding.UTF8.GetBytes(ReplyMessage + CountSeparator + count);
                listener.Send(reply, reply.Length, remote);
            }
            catch (Exception exception)
            {
                // socket 被关闭时会抛异常，属于正常退出路径。
                if (!_isListening)
                {
                    break;
                }

                Debug.LogWarning("[LanDiscoveryService] 监听循环异常：" + exception.Message);
            }
        }

        listener.Close();
    }
}
