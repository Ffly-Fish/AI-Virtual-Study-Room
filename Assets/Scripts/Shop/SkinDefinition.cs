using System;
using UnityEngine;

/// <summary>
/// 一套皮肤的静态定义：编号、显示名、价格、整套模型预制体。
/// 用户明确要求"整套 FBX 替换"，所以这里只放模型，不做部件挂点。
/// </summary>
[Serializable]
public class SkinDefinition
{
    /// <summary>皮肤编号，写进网络变量 <c>SkinId</c>，必须在 0-255 内且全表唯一。</summary>
    [Header("皮肤编号")]
    [Tooltip("皮肤唯一编号（0-255），会上报给其他玩家；0 必须是默认皮肤")]
    public byte Id;

    /// <summary>商城卡片与名字牌上显示的皮肤名。</summary>
    [Header("显示名称")]
    [Tooltip("商城卡片上显示的中文皮肤名，例如「休闲男装」")]
    public string DisplayName = "未命名皮肤";

    /// <summary>价格：用户设定每套 1 金币，仍做成可配置，方便以后调价。</summary>
    [Header("价格")]
    [Tooltip("购买该皮肤需要的金币数量，默认 1")]
    public int PriceCoins = 1;

    /// <summary>整套角色模型预制体（含骨骼与 SkinnedMeshRenderer），由 SkinVisualBinder 实例化并接管 Animator。</summary>
    [Header("模型预制体")]
    [Tooltip("整套角色模型的预制体（含 Animator 的 FBX 模型），换装时整只替换")]
    public GameObject ModelPrefab;
}
