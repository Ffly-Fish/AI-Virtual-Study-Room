using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 皮肤总表（ScriptableObject 资产）。
/// 用户要求"5 套皮肤、每套 1 金币"，所以这里只做静态配置，不碰金币与已购状态。
/// </summary>
[CreateAssetMenu(fileName = "SkinCatalog", menuName = "自习室/皮肤总表", order = 1)]
public class SkinCatalog : ScriptableObject
{
    /// <summary>全部皮肤，顺序即商城列表顺序；第 0 个是默认皮肤。</summary>
    [Header("皮肤列表")]
    [Tooltip("按顺序显示在商城里；第 0 个必须是默认皮肤且价格无效（默认已解锁）")]
    [SerializeField] private List<SkinDefinition> _skins = new List<SkinDefinition>();

    /// <summary>皮肤总数，供商城循环用。</summary>
    public int Count => _skins.Count;

    /// <summary>按编号查皮肤；找不到返回 null，由调用方回退到默认皮肤。</summary>
    public SkinDefinition Get(byte skinId)
    {
        for (int i = 0; i < _skins.Count; i++)
        {
            if (_skins[i] != null && _skins[i].Id == skinId)
            {
                return _skins[i];
            }
        }

        return null;
    }

    /// <summary>按列表下标取皮肤，越界返回 null。</summary>
    public SkinDefinition GetAt(int index)
    {
        if (index < 0 || index >= _skins.Count)
        {
            return null;
        }

        return _skins[index];
    }

    /// <summary>默认皮肤编号（列表第 0 个）；表为空时返回 0。</summary>
    public byte DefaultSkinId => _skins.Count > 0 && _skins[0] != null ? _skins[0].Id : (byte)0;

    /// <summary>编辑器里改表后校验：编号重复、编号越界、模型为空都只警告不阻断，避免资产打不开。</summary>
    private void OnValidate()
    {
        for (int i = 0; i < _skins.Count; i++)
        {
            if (_skins[i] == null)
            {
                Debug.LogWarning($"[SkinCatalog] 第 {i} 项为空，商城会跳过该项。", this);
                continue;
            }

            if (_skins[i].ModelPrefab == null)
            {
                Debug.LogWarning($"[SkinCatalog] 皮肤 {_skins[i].DisplayName} 没有模型预制体，换装会失败。", this);
            }

            for (int j = i + 1; j < _skins.Count; j++)
            {
                if (_skins[j] != null && _skins[j].Id == _skins[i].Id)
                {
                    Debug.LogWarning($"[SkinCatalog] 皮肤编号 {_skins[i].Id} 重复，网络同步会认错皮肤。", this);
                }
            }
        }
    }
}
