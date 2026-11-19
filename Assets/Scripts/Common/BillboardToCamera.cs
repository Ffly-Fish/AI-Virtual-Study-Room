using UnityEngine;

/// <summary>
/// 让物体始终正对主相机的广告牌（Billboard）组件。
/// 挂载后每帧在 LateUpdate 中调整自身世界旋转，使物体正面始终朝向相机，
/// 常用于 3D 场景中的文字标签、血条、图标、名字牌等。
/// </summary>
[AddComponentMenu("渲染/始终面向主相机")]
[DisallowMultipleComponent]
public class BillboardToCamera : MonoBehaviour
{
    /// <summary>
    /// 朝向模式：决定物体用哪个轴去对准相机。
    /// 两种模式相差 180°，选错会出现文字镜像（背面朝人）。
    /// </summary>
    public enum FacingMode
    {
        /// <summary>与相机旋转保持一致：适合平面类物体（Quad、TextMesh、TextMeshPro、Sprite）</summary>
        MatchCameraRotation = 0,

        /// <summary>让物体正面（本地 +Z）指向相机：适合有体积的立体模型</summary>
        LookAtCamera = 1
    }

    [Header("目标相机")]
    [Tooltip("需要朝向的相机；留空时自动使用场景中的主相机 Camera.main")]
    [SerializeField] private Camera _targetCamera;

    [Header("朝向模式")]
    [Tooltip("匹配相机旋转：平面/文字类物体适用；直视相机：立体模型适用；两者正面相差 180°")]
    [SerializeField] private FacingMode _facingMode = FacingMode.MatchCameraRotation;

    [Header("保持竖直")]
    [Tooltip("勾选后只绕 Y 轴旋转，物体不会随相机俯仰而前后倾倒，适合始终站立的标签")]
    [SerializeField] private bool _keepUpright = true;

    [Header("旋转偏移")]
    [Tooltip("在朝向结果上额外叠加的欧拉角偏移（单位：度），用于美术资源正面不朝向本地 Z 轴时的校正")]
    [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

    [Header("平滑跟随")]
    [Tooltip("旋转跟随速度，填 0 表示每帧瞬间对准；数值越大跟随越迅速")]
    [SerializeField, Min(0f)] private float _smoothSpeed = 0f;

    /// <summary>是否已经提示过“场景中找不到相机”，避免每帧重复刷日志</summary>
    private bool _hasWarnedMissingCamera;

    /// <summary>相机移动通常发生在 Update，因此在 LateUpdate 中对齐，避免画面出现一帧延迟</summary>
    private void LateUpdate()
    {
        Camera cameraToFace = ResolveCamera();
        if (cameraToFace == null)
        {
            return;
        }

        if (!TryCalculateTargetRotation(cameraToFace, out Quaternion targetRotation))
        {
            return;
        }

        // 平滑速度 0 表示立即对准；否则用指数衰减做与帧率无关的插值
        transform.rotation = _smoothSpeed > 0f
            ? Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-_smoothSpeed * Time.deltaTime))
            : targetRotation;
    }

    /// <summary>取得要朝向的相机：优先使用面板指定的相机，否则回退到主相机</summary>
    private Camera ResolveCamera()
    {
        if (_targetCamera != null)
        {
            return _targetCamera;
        }

        _targetCamera = Camera.main;

        if (_targetCamera == null && !_hasWarnedMissingCamera)
        {
            _hasWarnedMissingCamera = true;
            Debug.LogWarning("[BillboardToCamera] 场景中没有找到主相机，且未指定目标相机，物体将保持原旋转。", this);
        }

        return _targetCamera;
    }

    /// <summary>
    /// 计算目标旋转。
    /// </summary>
    /// <param name="cameraToFace">要朝向的相机</param>
    /// <param name="targetRotation">计算出的世界旋转</param>
    /// <returns>无法计算（如物体与相机重合、竖直模式下相机正对头顶）时返回 false，保持当前旋转</returns>
    private bool TryCalculateTargetRotation(Camera cameraToFace, out Quaternion targetRotation)
    {
        Transform cameraTransform = cameraToFace.transform;
        Vector3 selfPosition = transform.position;

        if (_facingMode == FacingMode.MatchCameraRotation)
        {
            if (_keepUpright)
            {
                // 只取相机前方向在水平面上的投影，物体便不会跟着俯仰倾斜
                if (!TryFlatten(cameraTransform.forward, out Vector3 flatForward))
                {
                    targetRotation = transform.rotation;
                    return false;
                }

                targetRotation = Quaternion.LookRotation(flatForward, Vector3.up);
            }
            else
            {
                // 与相机旋转完全一致：本地 -Z 正对相机，平面类物体的正面朝向画面
                targetRotation = cameraTransform.rotation;
            }
        }
        else
        {
            Vector3 toCamera = cameraTransform.position - selfPosition;

            if (_keepUpright)
            {
                if (!TryFlatten(toCamera, out Vector3 flatDirection))
                {
                    targetRotation = transform.rotation;
                    return false;
                }

                targetRotation = Quaternion.LookRotation(flatDirection, Vector3.up);
            }
            else
            {
                if (toCamera.sqrMagnitude < 0.000001f)
                {
                    targetRotation = transform.rotation;
                    return false;
                }

                targetRotation = Quaternion.LookRotation(toCamera, Vector3.up);
            }
        }

        // 偏移在物体本地空间中叠加，方便校正美术资源的朝向轴
        targetRotation *= Quaternion.Euler(_rotationOffset);
        return true;
    }

    /// <summary>把方向压平到水平面（去掉 Y 分量）并归一化</summary>
    /// <param name="direction">原始方向</param>
    /// <param name="flattened">压平后的方向；压平后长度过短时无法确定朝向</param>
    /// <returns>是否得到了有效的水平方向</returns>
    private static bool TryFlatten(Vector3 direction, out Vector3 flattened)
    {
        flattened = new Vector3(direction.x, 0f, direction.z);

        if (flattened.sqrMagnitude < 0.000001f)
        {
            flattened = Vector3.zero;
            return false;
        }

        flattened.Normalize();
        return true;
    }
}
