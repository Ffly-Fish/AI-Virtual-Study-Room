using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// 会话管理：包住 Netcode 的启停，向上只暴露"建房 / 进房 / 退房"三件事和几个事件。
/// 流程层不需要知道 NetworkManager 的细节，换传输层时只改这里。
/// </summary>
public class NetworkSessionService : MonoBehaviour
{
    /// <summary>
    /// 游戏数据用的 UDP 端口（Unity Transport）。
    /// 注意别和局域网发现的 47777 混用：发现端口由 UdpClient 独占监听，
    /// 传输层再绑同一个端口会直接失败。
    /// </summary>
    public const ushort TransportPort = 7777;

    /// <summary>本机作为主机启动成功（房间已开）。</summary>
    public event Action OnHostStarted;

    /// <summary>本机作为客户端连上主机。</summary>
    public event Action OnJoined;

    /// <summary>连接断开（含被主机踢下线）。</summary>
    public event Action OnDisconnected;

    /// <summary>连接或启动失败，参数是给玩家看的原因。</summary>
    public event Action<string> OnFailed;

    /// <summary>Netcode 管理器，为空时说明场景里没有装配，任何操作都直接失败。</summary>
    private NetworkManager _manager;

    /// <summary>传输层，主机与客户端都要用它设置地址。</summary>
    private UnityTransport _transport;

    /// <summary>是否已经初始化过。</summary>
    private bool _isInitialized;

    /// <summary>是否已启动成功。</summary>
    public bool IsRunning => _manager != null && _manager.IsListening;

    /// <summary>本机是否是主机（房主）。</summary>
    public bool IsHost => _manager != null && _manager.IsHost;

    /// <summary>本机是否是纯客户端。</summary>
    public bool IsClient => _manager != null && _manager.IsClient && !_manager.IsHost;

    /// <summary>接入场景里的 NetworkManager，重复调用安全。</summary>
    public void Initialize(NetworkManager manager, UnityTransport transport)
    {
        if (_isInitialized)
        {
            return;
        }

        _manager = manager;
        _transport = transport;
        _isInitialized = true;

        if (_manager == null || _transport == null)
        {
            Debug.LogError("[NetworkSessionService] 场景里缺少 NetworkManager 或 UnityTransport，联机不可用。", this);
            return;
        }

        _manager.OnClientConnectedCallback += HandleClientConnected;
        _manager.OnClientDisconnectCallback += HandleClientDisconnect;
    }

    /// <summary>建房：本机既是服务端也是客户端。</summary>
    public bool StartHost()
    {
        if (!EnsureReady())
        {
            return false;
        }

        _transport.SetConnectionData("0.0.0.0", TransportPort, "0.0.0.0");
        bool started = _manager.StartHost();
        if (!started)
        {
            OnFailed?.Invoke("房间创建失败，请稍后重试");
            return false;
        }

        OnHostStarted?.Invoke();
        return true;
    }

    /// <summary>进房：连到探测到的地址。</summary>
    public bool JoinRoom(string address)
    {
        if (!EnsureReady())
        {
            return false;
        }

        _transport.SetConnectionData(address, TransportPort);
        bool started = _manager.StartClient();
        if (!started)
        {
            OnFailed?.Invoke("加入房间失败，请检查网络");
            return false;
        }

        return true;
    }

    /// <summary>退房：主机或客户端都走同一条路。</summary>
    public void Leave()
    {
        if (_manager == null || !_manager.IsListening)
        {
            return;
        }

        _manager.Shutdown();
    }

    /// <summary>清理事件订阅，避免场景重载后重复回调。</summary>
    private void OnDestroy()
    {
        if (_manager != null)
        {
            _manager.OnClientConnectedCallback -= HandleClientConnected;
            _manager.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }

    /// <summary>检查管理器是否就绪，缺就报错并回调失败。</summary>
    private bool EnsureReady()
    {
        if (_manager == null || _transport == null)
        {
            Debug.LogError("[NetworkSessionService] 尚未初始化，无法联机。", this);
            OnFailed?.Invoke("联机组件未就绪");
            return false;
        }

        return true;
    }

    /// <summary>连接成功回调：客户端连上主机即通知流程。</summary>
    private void HandleClientConnected(ulong clientId)
    {
        if (_manager.IsClient && !_manager.IsHost && clientId == _manager.LocalClientId)
        {
            OnJoined?.Invoke();
        }
    }

    /// <summary>断开回调：区分"自己掉线"和"别人掉线"。</summary>
    private void HandleClientDisconnect(ulong clientId)
    {
        if (clientId != _manager.LocalClientId)
        {
            return;
        }

        OnDisconnected?.Invoke();
    }
}
