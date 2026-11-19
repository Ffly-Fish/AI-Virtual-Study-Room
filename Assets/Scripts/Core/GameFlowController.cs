using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 流程状态：从登录到进入房间、再到房主迁移所需的状态集合。
/// Migrating 是唯一一个"房间已经没了、但还没回登录"的中间态。
/// </summary>
public enum GameState
{
    /// <summary>启动初始化中。</summary>
    Boot = 0,

    /// <summary>等待输入用户名。</summary>
    Login = 1,

    /// <summary>搜索局域网房间。</summary>
    Searching = 2,

    /// <summary>作为房主创建房间。</summary>
    Hosting = 3,

    /// <summary>已找到房间，正在连进去。</summary>
    Joining = 4,

    /// <summary>在座位上生成角色。</summary>
    Spawning = 5,

    /// <summary>相机推镜过场。</summary>
    CameraIntro = 6,

    /// <summary>已在房间内，开始判定与计时。</summary>
    InRoom = 7,

    /// <summary>房主掉线，正在接管或重新加入房间。</summary>
    Migrating = 8
}

/// <summary>
/// 流程控制器：状态机 + 判定主循环。
/// 它不创建 UI、不建场景对象，只按状态驱动依赖，所有依赖来自 <see cref="GameContext"/>。
/// </summary>
public class GameFlowController : MonoBehaviour
{
    /// <summary>专注采样频率，默认 8 次/秒；太高会浪费功耗，太低动作跟随迟钝。</summary>
    [Header("判定节奏")]
    [Tooltip("每秒采样摄像头/按键的次数，默认 8 次")]
    [SerializeField, Min(1f)] private float _samplesPerSecond = 8f;

    /// <summary>搜索房间的等待时长（秒）；阶段 1 没有局域网，仅用于表现"正在找房间"。</summary>
    [Tooltip("搜索房间的等待秒数，超时后自建房间；阶段 1 是固定等待")]
    [SerializeField, Min(0f)] private float _searchSeconds = 1.5f;

    /// <summary>依赖集合，由 GameBootstrap 注入。</summary>
    private GameContext _context;

    /// <summary>去抖器：把抖动的原始判定压成稳定的专注状态。</summary>
    private readonly FocusFilter _focusFilter = new FocusFilter();

    /// <summary>金币计时器：只在专注时累加。</summary>
    private readonly FocusTracker _focusTracker = new FocusTracker();

    /// <summary>采样计时器。</summary>
    private float _sampleTimer;

    /// <summary>当前玩家占用的座位编号，退出时清空。</summary>
    private int _seatIndex;

    /// <summary>搜索协程句柄，退出时用于打断。</summary>
    private Coroutine _searchRoutine;

    /// <summary>本机玩家的网络身份，联机时所有同步都走它；单机为空。</summary>
    private PlayerNetwork _localPlayerNetwork;

    /// <summary>局域网探测到的房主地址，空表示没找到房间。</summary>
    private string _foundHostAddress;

    /// <summary>房主模式下缓存下来的座位分配器，供后台发现线程读取人数快照。</summary>
    private RoomRegistry _hostRegistry;

    /// <summary>最近一次写入角色的专注状态，避免每帧重复写 Animator。</summary>
    private bool _isFocusing;

    /// <summary>迁移协程句柄，防止同一次掉线触发两遍迁移。</summary>
    private Coroutine _migrationRoutine;

    /// <summary>判定器连续不可用的累计时长（秒），超过阈值就转旁观。</summary>
    private float _unavailableSeconds;

    /// <summary>本次是否以旁观模式进入（拿不到摄像头时只观察、不计专注）。</summary>
    private bool _isSpectator;

    /// <summary>判定器连续不可用多少秒后转旁观，避免刚启动那一两帧误判。</summary>
    private const float SpectatorThresholdSeconds = 3f;

    /// <summary>当前状态。</summary>
    public GameState State { get; private set; } = GameState.Boot;

    /// <summary>状态变化事件，UI 与调试用。</summary>
    public event Action<GameState> OnStateChanged;

    /// <summary>注入依赖并挂上判定器事件；由 GameBootstrap 在构建完 UI 后调用。</summary>
    public void Initialize(GameContext context)
    {
        _context = context;

        _focusTracker.OnCoinEarned += HandleCoinEarned;

        if (_context.Hud != null)
        {
            _context.Hud.OnShopRequested += HandleShopRequested;
            _context.Hud.OnExitRequested += HandleExitRequested;
        }

        if (_context.Login != null)
        {
            _context.Login.OnEnterRequested += HandleEnterRequested;
        }

        if (_context.Shop != null)
        {
            _context.Shop.OnBuyRequested += HandleBuyRequested;
            _context.Shop.OnEquipRequested += HandleEquipRequested;
            _context.Shop.OnCloseRequested += HandleShopClosed;
        }

        if (_context.Camera != null)
        {
            _context.Camera.OnIntroFinished += HandleIntroFinished;
        }

        PlayerNetwork.OnLocalPlayerReady += HandleLocalPlayerReady;
        PlayerNetwork.OnLocalPlayerGone += HandleLocalPlayerGone;

        if (_context.Session != null)
        {
            _context.Session.OnFailed += HandleNetworkFailed;
            _context.Session.OnDisconnected += HandleNetworkDisconnected;

            // 心跳看门狗：比传输层断开检测快 20 秒以上，先到先触发。
            if (_context.Migration != null)
            {
                _context.Migration.OnHostLost += HandleHostLost;
            }
        }
    }

    /// <summary>退场时取消静态事件订阅，避免场景重载后重复回调。</summary>
    private void OnDestroy()
    {
        PlayerNetwork.OnLocalPlayerReady -= HandleLocalPlayerReady;
        PlayerNetwork.OnLocalPlayerGone -= HandleLocalPlayerGone;

        if (_context?.Session != null)
        {
            _context.Session.OnFailed -= HandleNetworkFailed;
            _context.Session.OnDisconnected -= HandleNetworkDisconnected;

            if (_context.Migration != null)
            {
                _context.Migration.OnHostLost -= HandleHostLost;
            }
        }
    }

    /// <summary>进入登录状态，界面切到登录面板。</summary>
    public void EnterLogin()
    {
        _context?.Hud?.Hide();
        _context?.Shop?.Hide();
        _context?.Login?.Show();
        _context?.Login?.SetInteractable(true);

        string savedName = PrefsStore.LoadPlayerName();
        _context?.Login?.SetHint(string.Empty);
        SetState(GameState.Login);
    }

    /// <summary>退出当前状态：销毁角色、停掉搜索、回到登录。</summary>
    public void ReturnToLogin()
    {
        if (_searchRoutine != null)
        {
            StopCoroutine(_searchRoutine);
            _searchRoutine = null;
        }

        _context?.Camera?.SkipIntro();
        _context?.Spawner?.DespawnLocalPlayer();

        if (_context?.Session != null && _context.Session.IsRunning)
        {
            _context.Session.Leave();
        }

        _context?.Discovery?.StopHostListener();
        _context?.Migration?.ResetRoster();
        _localPlayerNetwork = null;
        _seatIndex = 0;
        _isSpectator = false;
        _unavailableSeconds = 0f;
        _migrationRoutine = null;
        ApplyFocusing(false);
        EnterLogin();
    }

    /// <summary>每帧推进采样与金币计时；只在房间内生效。</summary>
    private void Update()
    {
        if (State != GameState.InRoom || _context == null)
        {
            return;
        }

        if (_context.Detector == null)
        {
            if (!_isSpectator)
            {
                MarkSpectator("没有可用的专注判定器，本次以旁观模式进入");
            }

            return;
        }

        // 摄像头可能被权限/占用卡住：连续不可用一小段时间就直接转旁观，不再空转推理。
        if (!_context.Detector.IsAvailable)
        {
            _unavailableSeconds += Time.deltaTime;
            if (_unavailableSeconds >= SpectatorThresholdSeconds && !_isSpectator)
            {
                MarkSpectator("未获得摄像头权限，本次以旁观模式进入");
            }

            return;
        }

        _unavailableSeconds = 0f;

        if (_isSpectator)
        {
            return;
        }

        float interval = 1f / Mathf.Max(1f, _samplesPerSecond);
        _sampleTimer += Time.deltaTime;

        // 采样频率低于帧率：攒够一个间隔才做一次推理，避免每帧都跑模型。
        if (_sampleTimer >= interval)
        {
            _sampleTimer = 0f;
            FocusSample sample = _context.Detector.Detect();
            if (_focusFilter.Update(sample))
            {
                ApplyFocusing(_focusFilter.IsFocusing);
            }
        }

        _focusTracker.Tick(Time.deltaTime, _focusFilter.IsFocusing);
        _context.Hud?.SetProgress(_focusTracker.Progress01);
    }

    /// <summary>切后台/息屏时强制按"不专注"处理，符合用户要求。</summary>
    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            ApplyFocusing(false);
        }
    }

    /// <summary>失去焦点（例如切到别的窗口）也按不专注处理。</summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ApplyFocusing(false);
        }
    }

    /// <summary>登录面板点了进入：记名字、进搜索状态。</summary>
    private void HandleEnterRequested(string playerName)
    {
        PrefsStore.SavePlayerName(playerName);
        _context.Login?.SetInteractable(false);
        _context.Login?.SetHint("正在搜索附近的房间…", false);
        SetState(GameState.Searching);

        if (_searchRoutine != null)
        {
            StopCoroutine(_searchRoutine);
        }

        bool networked = _context != null && _context.Session != null && _context.Discovery != null;
        _searchRoutine = StartCoroutine(networked ? NetworkSearchRoutine() : SearchRoutine());
    }

    /// <summary>单机搜索：等待固定时长后直接入座（没有网络组件时的兜底）。</summary>
    private IEnumerator SearchRoutine()
    {
        yield return new WaitForSeconds(_searchSeconds);

        _searchRoutine = null;
        SetState(GameState.Hosting);
        _context.Login?.Hide();
        EnterRoom();
    }

    /// <summary>
    /// 联机搜索：先广播 1.5 秒找房间，找到就加入，找不到就自己开房并开始回应探测。
    /// 全局域网只允许一个房间，所以"找不到"就等于"我是房主"。
    /// </summary>
    private IEnumerator NetworkSearchRoutine()
    {
        _foundHostAddress = null;
        _isSpectator = false;
        _unavailableSeconds = 0f;
        _context.Migration?.ResetRoster();
        yield return _context.Discovery.Search(
            _searchSeconds,
            address => _foundHostAddress = address,
            () => _foundHostAddress = null);

        if (!string.IsNullOrEmpty(_foundHostAddress))
        {
            SetState(GameState.Joining);
            _context.Login?.SetHint("找到房间，正在加入…", false);
            if (!_context.Session.JoinRoom(_foundHostAddress))
            {
                ReturnToLogin();
                yield break;
            }
        }
        else
        {
            SetState(GameState.Hosting);
            _context.Login?.SetHint("没有找到房间，正在创建…", false);

            // 先抢局域网发现端口：抢不到说明本机已经有一个房主实例（重复开两个客户端时）。
            // 这时不能再开第二个房，应该重新搜索并加入已有的那个房间。
            _hostRegistry = FindObjectOfType<RoomRegistry>();
            if (!_context.Discovery.StartHostListener(GetCachedPlayerCount))
            {
                _context.Login?.SetHint("本机已有房间，正在加入…", false);
                yield return _context.Discovery.Search(
                    _searchSeconds,
                    address => _foundHostAddress = address,
                    () => _foundHostAddress = null);

                if (string.IsNullOrEmpty(_foundHostAddress) || !_context.Session.JoinRoom(_foundHostAddress))
                {
                    _context.Toast?.Show("本机房间端口被占用，请只保留一个实例");
                    ReturnToLogin();
                    yield break;
                }

                SetState(GameState.Joining);
            }
            else if (!_context.Session.StartHost())
            {
                _context.Discovery.StopHostListener();
                ReturnToLogin();
                yield break;
            }
        }

        _searchRoutine = null;

        // 角色由服务端生成、座位分配好后通过网络变量同步回来，这里等它触发 OnLocalPlayerReady。
        float deadline = Time.unscaledTime + 10f;
        while (Time.unscaledTime < deadline && _localPlayerNetwork == null)
        {
            yield return null;
        }

        if (_localPlayerNetwork == null)
        {
            _context.Toast?.Show("进房超时，请重试");
            _context.Session.Leave();
            ReturnToLogin();
        }
    }

    /// <summary>当前房间人数，用于回执里告诉别人房间还剩几个位置。</summary>
    private int CountRoomPlayers()
    {
        RoomRegistry registry = FindObjectOfType<RoomRegistry>();
        return registry != null ? registry.PlayerCount : 1;
    }

    /// <summary>
    /// 供局域网发现的后台线程回调：只读一个普通 int 快照。
    /// 后台线程里不能碰任何 Unity 场景 API（FindObjectOfType 会抛"can only be called from the main thread"）。
    /// </summary>
    private int GetCachedPlayerCount()
    {
        return _hostRegistry != null ? _hostRegistry.PlayerCountSnapshot : 1;
    }

    /// <summary>本机角色就位：隐藏登录、开 HUD、飞相机。</summary>
    private void HandleLocalPlayerReady(PlayerNetwork playerNetwork)
    {
        if (playerNetwork == null || State == GameState.InRoom)
        {
            return;
        }

        _localPlayerNetwork = playerNetwork;
        _seatIndex = playerNetwork.SeatIndex;

        // 没有判定器（例如主动走旁观）时先标记旁观，避免空转采样。
        if (_context.Detector == null)
        {
            MarkSpectator("没有可用的专注判定器，本次以旁观模式进入");
        }
        else
        {
            playerNetwork.SetSpectator(false);
        }
        _context?.Login?.Hide();
        _context?.Hud?.Show();
        _context?.Hud?.SetCoins(_context.Wallet != null ? _context.Wallet.Coins : 0);
        _context?.Hud?.SetProgress(_focusTracker.Progress01);
        ApplyFocusing(false);
        SetState(GameState.CameraIntro);

        SeatMarker seat = _context?.Seats != null ? _context.Seats.GetMarker(playerNetwork.SeatIndex) : null;
        if (seat != null)
        {
            _context.Camera?.FlyToSeat(seat);
        }
        else
        {
            HandleIntroFinished();
        }
    }

    /// <summary>本机角色消失（退房或掉线）：回到登录。</summary>
    private void HandleLocalPlayerGone()
    {
        _localPlayerNetwork = null;
    }

    /// <summary>联机失败：提示并回登录。</summary>
    private void HandleNetworkFailed(string reason)
    {
        _context?.Toast?.Show(reason);
        _context?.Login?.SetInteractable(true);
        ReturnToLogin();
    }

    /// <summary>
    /// 与房间断开：还在房间里说明是房主掉了，先尝试接管或重新加入；
    /// 其余情况（登录、搜索、刚连上就失败）直接回登录。
    /// 注意迁移流程自己调 Leave 也会进到这里，用 State 挡掉。
    /// </summary>
    private void HandleNetworkDisconnected()
    {
        if (State == GameState.Login || State == GameState.Migrating)
        {
            return;
        }

        bool wasInRoom = State == GameState.InRoom || State == GameState.CameraIntro || State == GameState.Spawning;
        if (!wasInRoom || _context?.Session == null)
        {
            _context?.Toast?.Show("与房间断开连接");
            ReturnToLogin();
            return;
        }

        BeginMigration(_context.Migration != null && _context.Migration.ShouldTakeOver());
    }

    /// <summary>
    /// 房主心跳超时：与 HandleNetworkDisconnected 共用同一套迁移流程，
    /// 靠 _migrationRoutine 去重，所以两条链路谁先到都不会重复接管。
    /// </summary>
    private void HandleHostLost(bool takeOver)
    {
        if (State == GameState.Login || State == GameState.Migrating || _migrationRoutine != null)
        {
            return;
        }

        BeginMigration(takeOver);
    }

    /// <summary>进入迁移流程：迁移期间关掉看门狗，避免自己主动退房又触发一次。</summary>
    private void BeginMigration(bool takeOver)
    {
        if (_migrationRoutine != null)
        {
            return;
        }

        if (_context.Migration != null)
        {
            _context.Migration.WatchdogEnabled = false;
        }

        _migrationRoutine = StartCoroutine(MigrateRoutine(takeOver));
    }

    /// <summary>房主掉线后的重建：先彻底退房，再按"是否接任"走两条分支。</summary>
    private IEnumerator MigrateRoutine(bool takeOver)
    {
        SetState(GameState.Migrating);
        _context.Camera?.SkipIntro();
        _context.Toast?.Show(takeOver ? "房主已离开，正在接管房间…" : "房主已离开，正在重新加入…");

        // 先把本地连接与发现端口收干净，否则新房间抢不到端口。
        _localPlayerNetwork = null;
        _context.Session?.Leave();
        _context.Discovery?.StopHostListener();

        // 等一帧让 Netcode 把连接状态清完，再决定是否重新开监听。
        yield return new WaitForSecondsRealtime(1f);

        if (takeOver)
        {
            yield return TakeOverHostRoutine();
        }
        else
        {
            yield return RejoinRoutine();
        }

        _migrationRoutine = null;

        // 迁移结束重新开始计时：新房间里心跳会重新到达，看门狗从零开始。
        _context.Migration?.ResetWatchdog();
    }

    /// <summary>接管：抢发现端口 → 开房 → 等角色重新生成（座位靠会话号回到原位）。</summary>
    private IEnumerator TakeOverHostRoutine()
    {
        _context.Migration?.ResetRoster();
        _hostRegistry = FindObjectOfType<RoomRegistry>();

        // 旧房主刚退出，端口可能还占着一小会儿，重试几次再放弃。
        bool listening = false;
        for (int attempt = 0; attempt < 5 && !listening; attempt++)
        {
            listening = _context.Discovery != null && _context.Discovery.StartHostListener(GetCachedPlayerCount);
            if (!listening)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        if (!listening || !_context.Session.StartHost())
        {
            // 抢不到端口说明已经有别人接管了，退回到"重新加入"这条路。
            yield return RejoinRoutine();
            yield break;
        }

        _context.Toast?.Show("你已成为新房主");
        yield return WaitLocalPlayerRoutine(10f);

        if (_localPlayerNetwork == null)
        {
            _context.Toast?.Show("接管房间失败，请重新进入");
            ReturnToLogin();
        }
    }

    /// <summary>非接任者：重新广播找新房主并加入，最多试三次。</summary>
    private IEnumerator RejoinRoutine()
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            _foundHostAddress = null;
            if (_context.Discovery != null)
            {
                yield return _context.Discovery.Search(
                    _searchSeconds,
                    address => _foundHostAddress = address,
                    () => _foundHostAddress = null);
            }

            if (!string.IsNullOrEmpty(_foundHostAddress) && _context.Session.JoinRoom(_foundHostAddress))
            {
                SetState(GameState.Joining);
                yield return WaitLocalPlayerRoutine(10f);
                if (_localPlayerNetwork != null)
                {
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(1f);
        }

        _context.Toast?.Show("与房间断开连接");
        ReturnToLogin();
    }

    /// <summary>等本机角色重新生成，超时返回 null。</summary>
    private IEnumerator WaitLocalPlayerRoutine(float timeoutSeconds)
    {
        float deadline = Time.unscaledTime + timeoutSeconds;
        while (Time.unscaledTime < deadline && _localPlayerNetwork == null)
        {
            yield return null;
        }
    }

    /// <summary>转为旁观：停掉判定与计时，只保留观察与网络状态同步。</summary>
    private void MarkSpectator(string message)
    {
        _isSpectator = true;
        _localPlayerNetwork?.SetSpectator(true);
        ApplyFocusing(false);
        _context?.Toast?.Show(message);
    }

    /// <summary>入座：取最小空闲座位 → 生成角色 → 相机推镜。</summary>
    private void EnterRoom()
    {
        SeatMarker seat = FindFreeSeat();
        if (seat == null)
        {
            _context.Login?.Show();
            _context.Login?.SetInteractable(true);
            _context.Login?.SetHint("房间里没有可用座位，请检查座位标记。");
            _context.Toast?.Show("没有可用座位");
            SetState(GameState.Login);
            return;
        }

        _seatIndex = seat.SeatIndex;

        int equippedId = _context.Inventory != null ? _context.Inventory.EquippedId : 0;
        string playerName = PrefsStore.LoadPlayerName();
        GameObject player = _context.Spawner?.SpawnLocalPlayer(seat, (byte)equippedId, playerName);

        _context.Hud?.Show();
        _context.Hud?.SetCoins(_context.Wallet != null ? _context.Wallet.Coins : 0);
        _context.Hud?.SetProgress(_focusTracker.Progress01);
        ApplyFocusing(false);
        SetState(GameState.Spawning);

        if (player == null)
        {
            _context.Toast?.Show("角色生成失败");
            return;
        }

        // 用户要求先出角色、再飞相机，所以顺序固定：Spawn → CameraIntro。
        SetState(GameState.CameraIntro);
        _context.Camera?.FlyToSeat(seat);
    }

    /// <summary>取编号最小的空闲座位；阶段 1 只有自己，所以永远是 1 号位。</summary>
    private SeatMarker FindFreeSeat()
    {
        if (_context.Seats == null)
        {
            return null;
        }

        foreach (SeatMarker marker in _context.Seats.GetSortedMarkers())
        {
            if (marker != null)
            {
                return marker;
            }
        }

        return null;
    }

    /// <summary>相机过场结束，正式进入房间。</summary>
    private void HandleIntroFinished()
    {
        if (State != GameState.CameraIntro)
        {
            return;
        }

        SetState(GameState.InRoom);
        _context.Toast?.Show("开始自习吧，看屏幕就能累积金币");
    }

    /// <summary>把专注状态同步到角色动画、名字牌图标与 HUD。</summary>
    private void ApplyFocusing(bool isFocusing)
    {
        _isFocusing = isFocusing;

        if (_localPlayerNetwork != null)
        {
            // 联机时专注状态要同步给所有人，本人这里也由网络变量回调统一驱动动画。
            _localPlayerNetwork.SetFocusing(isFocusing);
            _context?.Hud?.SetFocusing(isFocusing);
            return;
        }

        GameObject player = _context?.Spawner?.LocalPlayer;
        if (player != null)
        {
            PlayerAnimatorDriver driver = player.GetComponent<PlayerAnimatorDriver>();
            if (driver != null)
            {
                driver.SetFocusing(isFocusing);
            }

            SkinVisualBinder binder = player.GetComponent<SkinVisualBinder>();
            if (binder != null)
            {
                binder.SetFocusTag(isFocusing);
            }
        }

        _context?.Hud?.SetFocusing(isFocusing);
    }

    /// <summary>专注累计满一段，发金币并提示。</summary>
    private void HandleCoinEarned(int coinCount)
    {
        if (_context?.Wallet == null || coinCount <= 0)
        {
            return;
        }

        _context.Wallet.AddCoins(coinCount);
        RefreshCoinDisplays();
        _context.Toast?.Show($"专注奖励 +{coinCount} 金币");

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySoundEffect("获得金币");
        }
    }

    /// <summary>打开商城并刷新到最新金币/库存状态。</summary>
    private void HandleShopRequested()
    {
        if (_context?.Shop == null)
        {
            return;
        }

        _context.Shop.Show();
        _context.Shop.Refresh(_context.ShopService != null ? _context.ShopService.Coins : 0, _context.Inventory);
    }

    /// <summary>关闭商城。</summary>
    private void HandleShopClosed()
    {
        _context?.Shop?.Hide();
    }

    /// <summary>当前使用的专注判定器（诊断与联机上报都需要读它）。</summary>
    public IFocusDetector Detector => _context?.Detector;

    /// <summary>购买：成功即穿上并提示，失败按原因给出不同文案。</summary>
    private void HandleBuyRequested(byte skinId)
    {
        if (_context?.ShopService == null)
        {
            return;
        }

        bool bought = _context.ShopService.TryPurchaseAndEquip(skinId);
        if (bought)
        {
            RefreshPlayerSkin();
            _context.Toast?.Show("购买成功，已为你换上");
        }
        else
        {
            _context.Toast?.Show("金币不足或已经拥有");
        }

        RefreshCoinDisplays();
    }

    /// <summary>穿戴已拥有的皮肤。</summary>
    private void HandleEquipRequested(byte skinId)
    {
        if (_context?.ShopService == null)
        {
            return;
        }

        if (_context.ShopService.TryEquip(skinId))
        {
            RefreshPlayerSkin();
            _context.Toast?.Show("已换上新皮肤");
        }

        RefreshCoinDisplays();
    }

    /// <summary>
    /// 金币总额有变化时同时刷新商城与左上角 HUD。
    /// 之前只刷新商城，买完东西 HUD 会停在旧数值上，和商城显示的金额对不上。
    /// </summary>
    private void RefreshCoinDisplays()
    {
        int coins = _context.ShopService != null ? _context.ShopService.Coins : 0;
        _context.Shop?.Refresh(coins, _context.Inventory);
        _context.Hud?.SetCoins(coins);
    }

    /// <summary>换装后重新绑定模型，并把当前专注状态补写一次，避免新模型站着不动。</summary>
    private void RefreshPlayerSkin()
    {
        GameObject player = _context?.Spawner?.LocalPlayer;
        if (player == null)
        {
            return;
        }

        SkinVisualBinder binder = player.GetComponent<SkinVisualBinder>();
        SkinDefinition skin = _context.Catalog != null ? _context.Catalog.Get((byte)_context.Inventory.EquippedId) : null;
        binder?.Bind(skin);
        ApplyFocusing(_isFocusing);
    }

    /// <summary>HUD 上点了退出。</summary>
    private void HandleExitRequested()
    {
        ReturnToLogin();
    }

    /// <summary>切换状态并广播，便于调试时看状态流转。</summary>
    private void SetState(GameState nextState)
    {
        if (State == nextState)
        {
            return;
        }

        State = nextState;
        OnStateChanged?.Invoke(nextState);
    }
}
