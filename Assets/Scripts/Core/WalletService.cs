using System;
using UnityEngine;

/// <summary>
/// 本机金币账户。金币只在客户端结算、不进网络（用户确认的口径），
/// 因此这里不做任何服务端校验，读档/存档全部走 <see cref="PrefsStore"/>。
/// 每次变动都会发 <see cref="OnCoinsChanged"/>，表现层只订阅不反写。
/// </summary>
public class WalletService : MonoBehaviour
{
    /// <summary>金币变化事件：参数为变化后的总额，供 HUD 刷新。</summary>
    public event Action<int> OnCoinsChanged;

    /// <summary>当前金币总数，只读给表现层。</summary>
    public int Coins { get; private set; }

    /// <summary>进入场景时从存档恢复金币，缺省 0。</summary>
    private void Awake()
    {
        Coins = PrefsStore.LoadCoins();
    }

    /// <summary>应用退出或切后台时落盘，防止异常退出丢币。</summary>
    private void OnApplicationQuit()
    {
        PrefsStore.SaveCoins(Coins);
    }

    /// <summary>切到后台时也落盘：Android 上被系统杀掉前不保证走 OnApplicationQuit。</summary>
    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            PrefsStore.SaveCoins(Coins);
        }
    }

    /// <summary>加币（专注满一段时由 FocusTracker 调用）。amount 必须为正，否则忽略并警告。</summary>
    public void AddCoins(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"[WalletService] AddCoins 收到非正数 {amount}，已忽略。");
            return;
        }

        Coins += amount;
        PrefsStore.SaveCoins(Coins);
        OnCoinsChanged?.Invoke(Coins);
    }

    /// <summary>尝试扣币：余额不足返回 false 且不改变任何状态，调用方据此给"金币不足"提示。</summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"[WalletService] TrySpend 收到非正数 {amount}，已忽略。");
            return false;
        }

        if (Coins < amount)
        {
            return false;
        }

        Coins -= amount;
        PrefsStore.SaveCoins(Coins);
        OnCoinsChanged?.Invoke(Coins);
        return true;
    }
}
