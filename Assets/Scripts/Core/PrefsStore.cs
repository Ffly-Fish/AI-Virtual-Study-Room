using UnityEngine;

/// <summary>
/// 本机存档的唯一读写入口。
/// 把所有 PlayerPrefs 的键集中在这里，避免各处散落字符串字面量导致键名漂移、读写错位。
/// 只负责"键叫什么、存取什么类型"，业务规则（何时加币、何时解锁）不放在这里。
/// </summary>
public static class PrefsStore
{
    /// <summary>用户名键：登录面板回填上次输入，不参与联机校验。</summary>
    private const string KeyPlayerName = "player_name";

    /// <summary>金币键：按设备存，换用户名不清零（用户明确要求）。</summary>
    private const string KeyCoins = "coins";

    /// <summary>已购皮肤位掩码键：第 n 位为 1 表示 SkinId = n 的皮肤已购买。</summary>
    private const string KeyOwnedSkins = "owned_skins";

    /// <summary>当前穿戴皮肤键：存 SkinId，读取时由 SkinCatalog 校验存在性。</summary>
    private const string KeyEquippedSkin = "equipped_skin";

    /// <summary>读取上次使用的用户名；没有存档时返回空串，由登录面板决定默认值。</summary>
    public static string LoadPlayerName()
    {
        return PlayerPrefs.GetString(KeyPlayerName, string.Empty);
    }

    /// <summary>写入用户名。空串也照写，方便用户清空后下次仍显示空。</summary>
    public static void SavePlayerName(string playerName)
    {
        // 这里只做存档，不裁剪首尾空格：显示层需要原样回填用户输入。
        PlayerPrefs.SetString(KeyPlayerName, playerName ?? string.Empty);
        PlayerPrefs.Save();
    }

    /// <summary>读取金币，负值视为 0（防御手改注册表造成的脏数据）。</summary>
    public static int LoadCoins()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(KeyCoins, 0));
    }

    /// <summary>写入金币并立即落盘，避免异常退出丢币。</summary>
    public static void SaveCoins(int coins)
    {
        PlayerPrefs.SetInt(KeyCoins, Mathf.Max(0, coins));
        PlayerPrefs.Save();
    }

    /// <summary>读取已购皮肤掩码，默认只解锁默认皮肤（第 0 位）。</summary>
    public static int LoadOwnedSkinMask()
    {
        return PlayerPrefs.GetInt(KeyOwnedSkins, 1);
    }

    /// <summary>写入已购皮肤掩码。</summary>
    public static void SaveOwnedSkinMask(int mask)
    {
        PlayerPrefs.SetInt(KeyOwnedSkins, mask);
        PlayerPrefs.Save();
    }

    /// <summary>读取当前穿戴的 SkinId；越界值由调用方（InventoryStore）回退到默认皮肤。</summary>
    public static byte LoadEquippedSkin()
    {
        return (byte)Mathf.Clamp(PlayerPrefs.GetInt(KeyEquippedSkin, 0), 0, 255);
    }

    /// <summary>写入当前穿戴的 SkinId。</summary>
    public static void SaveEquippedSkin(byte skinId)
    {
        PlayerPrefs.SetInt(KeyEquippedSkin, skinId);
        PlayerPrefs.Save();
    }
}
