using TMPro;
using Unity.Barracuda;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 组合根：构建界面、装配服务、注入依赖，然后把控制权交给 <see cref="GameFlowController"/>。
/// 场景里只需要挂这一个组件并指定字体与皮肤总表，其余对象都在运行时创建，
/// 目的是让用户不必手工拖二十个引用也能直接按 Play 跑起来。
/// </summary>
[AddComponentMenu("自习室/游戏入口")]
public class GameBootstrap : MonoBehaviour
{
    /// <summary>界面字体：必须包含中文字形，否则所有中文都是方块。</summary>
    [Header("界面资源")]
    [Tooltip("界面使用的中文字体（项目里用 Assets/Front/HONORSansCN-Regular SDF 1）")]
    [SerializeField] private TMP_FontAsset _uiFont;

    /// <summary>皮肤总表资产，包含 5 套皮肤与价格。</summary>
    [Tooltip("皮肤总表资产，决定了商城里能买什么、默认穿什么")]
    [SerializeField] private SkinCatalog _skinCatalog;

    /// <summary>所有皮肤共用的动画控制器。</summary>
    [Header("角色资源")]
    [Tooltip("所有皮肤模型共用的 Animator Controller（PlayerAnimator.controller）")]
    [SerializeField] private RuntimeAnimatorController _animatorController;

    /// <summary>人脸检测模型，指定后默认使用摄像头判定。</summary>
    [Tooltip("人脸检测模型（Assets/Models/face_detector.onnx），指定后默认启用摄像头判定")]
    [SerializeField] private NNModel _faceDetectorModel;

    /// <summary>可选：外部提供的专注判定器，实现 IFocusDetector 即可，优先级高于模型与模拟器。</summary>
    [Tooltip("可选的摄像头判定组件（需实现 IFocusDetector），留空则按模型→模拟器的顺序选择")]
    [SerializeField] private MonoBehaviour _focusDetectorSource;

    /// <summary>场景里的座位总表，留空会自动查找。</summary>
    [Header("场景引用")]
    [Tooltip("座位总表，留空会自动在场景里查找")]
    [SerializeField] private SeatRegistry _seatRegistry;

    /// <summary>相机导演，留空会自动查找或挂到主相机上。</summary>
    [Tooltip("相机导演组件，留空会自动查找；都找不到时会自动挂到主相机上")]
    [SerializeField] private CameraDirector _cameraDirector;

    /// <summary>流程控制器，留空会自动查找或创建。</summary>
    [Tooltip("流程控制器，留空自动创建")]
    [SerializeField] private GameFlowController _flowController;

    /// <summary>运行时构建的角色模板（非场景对象，仅用于 Instantiate）。</summary>
    private GameObject _playerTemplate;

    /// <summary>根画布。</summary>
    private RectTransform _canvasRect;

    /// <summary>启动：建 UI → 装服务 → 注入 → 进登录。</summary>
    private void Awake()
    {
        UiTheme.Font = _uiFont;

        EnsureEventSystem();
        BuildCanvas();

        WalletService wallet = GetOrAddComponent<WalletService>();
        SeatRegistry seats = ResolveSeatRegistry();
        CameraDirector cameraDirector = ResolveCameraDirector();
        PlayerSpawner spawner = GetOrAddComponent<PlayerSpawner>();
        GameFlowController flow = ResolveFlowController();

        NetworkSessionService session = BuildSessionService();
        LanDiscoveryService discovery = session != null ? GetOrAddComponent<LanDiscoveryService>() : null;

        InventoryStore inventory = new InventoryStore(_skinCatalog);
        ShopService shopService = new ShopService(_skinCatalog, wallet, inventory);

        BuildPlayerTemplate();
        spawner.SetPlayerTemplate(_playerTemplate);
        spawner.SetSkinCatalog(_skinCatalog);

        GameContext context = new GameContext
        {
            Login = BuildPanel<LoginPanel>("LoginPanel"),
            Shop = BuildPanel<ShopPanel>("ShopPanel"),
            Hud = BuildPanel<RoomHud>("RoomHud"),
            Toast = BuildPanel<ToastView>("ToastView"),
            Wallet = wallet,
            Inventory = inventory,
            ShopService = shopService,
            Catalog = _skinCatalog,
            Seats = seats,
            Spawner = spawner,
            Camera = cameraDirector,
            Detector = ResolveDetector(),
            Session = session,
            Discovery = discovery,
        };

        context.Login.Build(PrefsStore.LoadPlayerName());
        context.Hud.Build();
        context.Shop.Build(_skinCatalog);
        context.Toast.Build();

        // 初始只留登录面板，其余隐藏，避免第一帧闪出 HUD。
        context.Hud.Hide();
        context.Shop.Hide();

        flow.Initialize(context);
        flow.EnterLogin();
    }

    /// <summary>没有 EventSystem 时创建一个，否则按钮点不动。</summary>
    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        eventSystemObject.transform.SetParent(transform, false);
    }

    /// <summary>创建全屏 Overlay 画布，按 1920x1080 适配，保证在手机与 PC 上比例一致。</summary>
    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("UiCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRect = canvasObject.GetComponent<RectTransform>();
    }

    /// <summary>在画布下创建面板对象并挂上对应组件（不调用 Build，顺序由外部控制）。</summary>
    private T BuildPanel<T>(string nodeName) where T : Component
    {
        GameObject panelObject = new GameObject(nodeName, typeof(RectTransform));
        panelObject.transform.SetParent(_canvasRect, false);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        UiTheme.Stretch(rect);

        return panelObject.AddComponent<T>();
    }

    /// <summary>定位座位总表；找不到就报错，因为没有座位角色无处生成。</summary>
    private SeatRegistry ResolveSeatRegistry()
    {
        if (_seatRegistry != null)
        {
            return _seatRegistry;
        }

        _seatRegistry = FindObjectOfType<SeatRegistry>();
        if (_seatRegistry == null)
        {
            Debug.LogError("[GameBootstrap] 场景里没有 SeatRegistry，请先用菜单「自习室/搭建自习室场景」生成座位。", this);
        }

        return _seatRegistry;
    }

    /// <summary>定位相机导演；没有就挂到主相机上，避免过场缺失导致流程卡在 CameraIntro。</summary>
    private CameraDirector ResolveCameraDirector()
    {
        if (_cameraDirector != null)
        {
            return _cameraDirector;
        }

        _cameraDirector = FindObjectOfType<CameraDirector>();
        if (_cameraDirector != null)
        {
            return _cameraDirector;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[GameBootstrap] 场景里没有主相机，相机过场无法播放。", this);
            return null;
        }

        _cameraDirector = mainCamera.gameObject.AddComponent<CameraDirector>();
        return _cameraDirector;
    }

    /// <summary>定位流程控制器；没有就挂在本对象上，保持与入口同一生命周期。</summary>
    private GameFlowController ResolveFlowController()
    {
        if (_flowController != null)
        {
            return _flowController;
        }

        _flowController = FindObjectOfType<GameFlowController>();
        if (_flowController == null)
        {
            _flowController = gameObject.AddComponent<GameFlowController>();
        }

        return _flowController;
    }

    /// <summary>取专注判定器：优先用面板指定且实现了接口的组件，否则用按键模拟器。</summary>
    private IFocusDetector ResolveDetector()
    {
        if (_focusDetectorSource is IFocusDetector externalDetector)
        {
            return externalDetector;
        }

        if (_focusDetectorSource != null)
        {
            Debug.LogWarning("[GameBootstrap] 指定的判定组件没有实现 IFocusDetector，已退回按键模拟。", this);
        }

        if (_faceDetectorModel != null)
        {
            BarracudaFaceDetector cameraDetector = GetComponentInChildren<BarracudaFaceDetector>();
            if (cameraDetector == null)
            {
                cameraDetector = gameObject.AddComponent<BarracudaFaceDetector>();
            }

            cameraDetector.SetModel(_faceDetectorModel);
            return cameraDetector;
        }

        SimulatedFocusDetector simulated = GetComponentInChildren<SimulatedFocusDetector>();
        if (simulated == null)
        {
            simulated = gameObject.AddComponent<SimulatedFocusDetector>();
        }

        return simulated;
    }

    /// <summary>
    /// 构建角色模板：根节点只放逻辑组件，模型由 SkinVisualBinder 换装时实例化。
    /// 模板保持非激活，生成时先换装再激活，避免 Animator 在模型为空时报错。
    /// </summary>
    private void BuildPlayerTemplate()
    {
        _playerTemplate = new GameObject("PlayerTemplate");
        _playerTemplate.transform.SetParent(transform, false);
        _playerTemplate.SetActive(false);

        Transform skinRoot = new GameObject("SkinRoot").transform;
        skinRoot.SetParent(_playerTemplate.transform, false);

        _playerTemplate.AddComponent<PlayerNameTag>();
        _playerTemplate.AddComponent<PlayerAnimatorDriver>();

        SkinVisualBinder binder = _playerTemplate.AddComponent<SkinVisualBinder>();
        binder.SetSkinRoot(skinRoot);
        binder.SetSharedController(_animatorController);
    }

    /// <summary>取组件，没有就添加；用于把服务挂在入口对象上，省去场景接线。</summary>
    /// <summary>
    /// 装配联机会话。场景里没有 NetworkManager 时返回空，流程层会自动退回单机模式，
    /// 这样只打开 StudyScene 直接 Play 依然能玩。
    /// </summary>
    private NetworkSessionService BuildSessionService()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogWarning("[GameBootstrap] 场景里没有 NetworkManager，本次以单机模式运行（不会联机）。");
            return null;
        }

        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = manager.gameObject.AddComponent<UnityTransport>();
        }

        NetworkSessionService session = GetOrAddComponent<NetworkSessionService>();
        session.Initialize(manager, transport);
        return session;
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }
}
