using UnityEngine;

/// <summary>
/// 组合根上下文：把各层依赖收在一个对象里，由 <see cref="GameBootstrap"/> 填充、流程层读取。
/// 这样流程控制器不用背一长串 Initialize 参数，也不需要到处 FindObjectOfType。
/// 阶段 2 接入联机后，网络层同样从这里取依赖，避免循环引用。
/// </summary>
public class GameContext
{
    /// <summary>登录面板。</summary>
    public LoginPanel Login { get; set; }

    /// <summary>商城面板。</summary>
    public ShopPanel Shop { get; set; }

    /// <summary>房间内 HUD。</summary>
    public RoomHud Hud { get; set; }

    /// <summary>轻提示。</summary>
    public ToastView Toast { get; set; }

    /// <summary>钱包服务。</summary>
    public WalletService Wallet { get; set; }

    /// <summary>皮肤库存（拥有与穿戴状态）。</summary>
    public InventoryStore Inventory { get; set; }

    /// <summary>商城业务逻辑。</summary>
    public ShopService ShopService { get; set; }

    /// <summary>皮肤总表。</summary>
    public SkinCatalog Catalog { get; set; }

    /// <summary>座位总表。</summary>
    public SeatRegistry Seats { get; set; }

    /// <summary>角色生成器。</summary>
    public PlayerSpawner Spawner { get; set; }

    /// <summary>相机导演。</summary>
    public CameraDirector Camera { get; set; }

    /// <summary>联机会话（建房/进房/退房），单机模式下为空。</summary>
    public NetworkSessionService Session { get; set; }

    /// <summary>局域网房间发现，单机模式下为空。</summary>
    public LanDiscoveryService Discovery { get; set; }

    /// <summary>房主迁移花名册，单机模式下为空。</summary>
    public HostMigrationService Migration { get; set; }

    /// <summary>专注判定器（按键模拟器或摄像头实现）。</summary>
    public IFocusDetector Detector { get; set; }
}
