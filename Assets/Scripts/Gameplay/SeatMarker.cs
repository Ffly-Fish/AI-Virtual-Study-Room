using UnityEngine;

/// <summary>
/// 单个座位的标记：一个座位对应"角色站位"与"相机观察位"两个点。
/// 用户要求每个座位各有自己的相机观察点，所以这里把两者分开挂在同一个物体下，
/// 由场景里摆空对象决定，代码不猜坐标。
/// </summary>
public class SeatMarker : MonoBehaviour
{
    /// <summary>座位编号（1 起），必须唯一，落座顺序与房主分配规则都靠它。</summary>
    [Header("座位编号")]
    [Tooltip("座位编号，从 1 开始且不可重复；最多 4 个座位")]
    [SerializeField] private int _seatIndex = 1;

    /// <summary>角色生成点：脚底位置，朝向即角色初始朝向。</summary>
    [Header("角色站位")]
    [Tooltip("角色生成在这里（脚底位置），自身朝向就是角色朝向；留空则用本物体位置")]
    [SerializeField] private Transform _seatPoint;

    /// <summary>相机推镜终点：决定玩家坐下来后面的视角。</summary>
    [Header("相机观察位")]
    [Tooltip("相机推镜的落点（含朝向），留空则复用角色站位并抬高 1.6 米")]
    [SerializeField] private Transform _cameraPoint;

    /// <summary>座位编号，供 SeatRegistry 与网络层使用。</summary>
    public int SeatIndex => _seatIndex;

    /// <summary>角色生成位置。</summary>
    public Vector3 SeatPosition => _seatPoint != null ? _seatPoint.position : transform.position;

    /// <summary>角色生成朝向。</summary>
    public Quaternion SeatRotation => _seatPoint != null ? _seatPoint.rotation : transform.rotation;

    /// <summary>相机落点位置：没配 CameraPoint 时用"站位 + 1.6 米"兜底，避免相机贴地。</summary>
    public Vector3 CameraPosition => _cameraPoint != null
        ? _cameraPoint.position
        : SeatPosition + Vector3.up * 1.6f;

    /// <summary>相机落点朝向：没配 CameraPoint 时沿用座位朝向。</summary>
    public Quaternion CameraRotation => _cameraPoint != null ? _cameraPoint.rotation : SeatRotation;

    /// <summary>检查该座位是否可用；配置缺失只警告不抛异常，缺相机点会自动降级。</summary>
    public bool Validate()
    {
        bool ok = true;

        if (_seatIndex <= 0)
        {
            Debug.LogWarning($"[SeatMarker] {name} 的座位编号必须从 1 开始，当前是 {_seatIndex}。", this);
            ok = false;
        }

        if (_seatPoint == null)
        {
            Debug.LogWarning($"[SeatMarker] {name} 没有指定角色站位，已退化到本物体位置。", this);
        }

        if (_cameraPoint == null)
        {
            Debug.LogWarning($"[SeatMarker] {name} 没有指定相机观察位，已退化到站位上方 1.6 米。", this);
        }

        return ok;
    }

    /// <summary>在 Scene 视图里画出座位与相机落点，方便用户对着桌子摆位置。</summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.62f, 1f, 0.9f);
        Vector3 seatPos = _seatPoint != null ? _seatPoint.position : transform.position;
        Gizmos.DrawWireSphere(seatPos, 0.15f);
        Gizmos.DrawRay(seatPos, (_seatPoint != null ? _seatPoint.forward : transform.forward) * 0.6f);

        Gizmos.color = new Color(0.3f, 0.84f, 0.66f, 0.9f);
        Vector3 camPos = _cameraPoint != null ? _cameraPoint.position : seatPos + Vector3.up * 1.6f;
        Quaternion camRot = _cameraPoint != null ? _cameraPoint.rotation : (_seatPoint != null ? _seatPoint.rotation : transform.rotation);
        Gizmos.DrawWireCube(camPos, Vector3.one * 0.12f);
        Gizmos.DrawRay(camPos, camRot * Vector3.forward * 0.8f);
        Gizmos.DrawLine(seatPos, camPos);
    }
}
