using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 流程状态：从登录到进入房间单机跑通所需的最小状态集合。
/// 阶段 2 会补 Migrating / Reconnect 两个支路。
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

    /// <summary>在座位上生成角色。</summary>
    Spawning = 4,

    /// <summary>相机推镜过场。</summary>
    CameraIntro = 5,

    /// <summary>已在房间内，开始判定与计时。</summary>
    InRoom = 6
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

    /// <summary>最近一次写入角色的专注状态，避免每帧重复写 Animator。</summary>
    private bool _isFocusing;

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
        _seatIndex = 0;
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

        _searchRoutine = StartCoroutine(SearchRoutine());
    }

    /// <summary>模拟搜索：等待固定时长后自建房间（阶段 1 单机；阶段 2 改为监听局域网应答）。</summary>
    private IEnumerator SearchRoutine()
    {
        yield return new WaitForSeconds(_searchSeconds);

        _searchRoutine = null;
        SetState(GameState.Hosting);
        _context.Login?.Hide();
        EnterRoom();
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
