using System;
using UnityEngine;

/// <summary>
/// 商城业务逻辑：把"金币够不够、有没有买过、能不能穿"的判断集中在一处。
/// UI 只负责把点击转成这里的调用，不自己判断价格，避免两处规则打架。
/// </summary>
public class ShopService
{
    /// <summary>皮肤总表，校验皮肤编号是否合法。</summary>
    private readonly SkinCatalog _catalog;

    /// <summary>钱包，负责扣款。</summary>
    private readonly WalletService _wallet;

    /// <summary>库存，负责拥有与穿戴状态。</summary>
    private readonly InventoryStore _inventory;

    /// <summary>金币或穿戴状态变化时触发，UI 据此刷新。</summary>
    public event Action OnChanged;

    /// <summary>构造商城服务；任一依赖为空时服务仍可创建，但调用会返回 false。</summary>
    public ShopService(SkinCatalog catalog, WalletService wallet, InventoryStore inventory)
    {
        _catalog = catalog;
        _wallet = wallet;
        _inventory = inventory;
    }

    /// <summary>当前金币，供 UI 显示。</summary>
    public int Coins => _wallet != null ? _wallet.Coins : 0;

    /// <summary>库存，供 UI 查询拥有/穿戴状态。</summary>
    public InventoryStore Inventory => _inventory;

    /// <summary>购买皮肤：已拥有、皮肤不存在、金币不足都返回 false，且不改变任何状态。</summary>
    public bool TryPurchase(byte skinId)
    {
        if (_catalog == null || _inventory == null || _wallet == null)
        {
            Debug.LogWarning("[ShopService] 依赖缺失，无法购买。");
            return false;
        }

        SkinDefinition skin = _catalog.Get(skinId);
        if (skin == null)
        {
            Debug.LogWarning($"[ShopService] 皮肤编号 {skinId} 不存在。");
            return false;
        }

        if (_inventory.IsOwned(skinId))
        {
            return false;
        }

        if (!_wallet.TrySpend(skin.PriceCoins))
        {
            return false;
        }

        _inventory.Unlock(skinId);
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>穿戴皮肤：未拥有或皮肤不存在时返回 false。</summary>
    public bool TryEquip(byte skinId)
    {
        if (_catalog == null || _inventory == null)
        {
            return false;
        }

        if (_catalog.Get(skinId) == null)
        {
            return false;
        }

        bool equipped = _inventory.Equip(skinId);
        if (equipped)
        {
            OnChanged?.Invoke();
        }

        return equipped;
    }

    /// <summary>购买成功后顺手穿上，省去玩家再点一次（用户希望"买了就能用"）。</summary>
    public bool TryPurchaseAndEquip(byte skinId)
    {
        if (!TryPurchase(skinId))
        {
            return false;
        }

        return TryEquip(skinId);
    }
}
