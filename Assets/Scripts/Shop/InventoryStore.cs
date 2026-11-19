using UnityEngine;

/// <summary>
/// 已购皮肤与当前穿戴的记录（位掩码 + SkinId），全部落在 PlayerPrefs。
/// 之所以用位掩码而不是字符串列表：皮肤数量固定且很少，掩码读写更快也更不容易写坏。
/// </summary>
public class InventoryStore
{
    /// <summary>默认皮肤掩码：第 0 位恒为 1，保证任何时候都有皮肤可穿。</summary>
    private const int DefaultOwnedMask = 1;

    /// <summary>已购掩码（内存副本），每次变更立即写存档。</summary>
    private int _ownedMask;

    /// <summary>当前穿戴的 SkinId。</summary>
    private byte _equippedId;

    /// <summary>构造时从存档恢复；传入目录用于校验穿戴值是否仍存在。</summary>
    public InventoryStore(SkinCatalog catalog)
    {
        _ownedMask = PrefsStore.LoadOwnedSkinMask() | DefaultOwnedMask;
        _equippedId = PrefsStore.LoadEquippedSkin();

        // 存档里的皮肤可能被删掉或改了编号，这里回退到默认皮肤，避免换装时找不到模型。
        if (catalog != null && catalog.Get(_equippedId) == null)
        {
            _equippedId = catalog.DefaultSkinId;
            PrefsStore.SaveEquippedSkin(_equippedId);
        }
    }

    /// <summary>当前穿戴的 SkinId。</summary>
    public byte EquippedId => _equippedId;

    /// <summary>判断某个皮肤是否已拥有；编号超过 31 位时按未拥有处理（掩码只有 32 位）。</summary>
    public bool IsOwned(byte skinId)
    {
        if (skinId >= 32)
        {
            return false;
        }

        return (_ownedMask & (1 << skinId)) != 0;
    }

    /// <summary>把皮肤标记为已购并落盘；已拥有时直接返回，不重复写存档。</summary>
    public void Unlock(byte skinId)
    {
        if (skinId >= 32)
        {
            Debug.LogWarning($"[InventoryStore] 皮肤编号 {skinId} 超出掩码范围（0-31），无法记录。");
            return;
        }

        int bit = 1 << skinId;
        if ((_ownedMask & bit) != 0)
        {
            return;
        }

        _ownedMask |= bit;
        PrefsStore.SaveOwnedSkinMask(_ownedMask);
    }

    /// <summary>穿戴指定皮肤；未拥有时拒绝并警告，防止绕过商城直接改存档。</summary>
    public bool Equip(byte skinId)
    {
        if (!IsOwned(skinId))
        {
            Debug.LogWarning($"[InventoryStore] 皮肤 {skinId} 尚未购买，拒绝穿戴。");
            return false;
        }

        _equippedId = skinId;
        PrefsStore.SaveEquippedSkin(skinId);
        return true;
    }
}
