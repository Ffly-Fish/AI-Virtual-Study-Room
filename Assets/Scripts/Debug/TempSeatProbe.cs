using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// 临时座位机位探针：进房后把主相机依次摆到 4 个座位的观察点，
/// 每个机位截一张图存到 persistentDataPath，用来核对镜头有没有被墙或椅子挡住。
/// 仅在阶段 1/2 联调时使用，验证完随 Debug 目录一起删除。
/// </summary>
public class TempSeatProbe : MonoBehaviour
{
    /// <summary>自动挂载，避免改场景。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        GameObject host = new GameObject("TempSeatProbe");
        host.AddComponent<TempSeatProbe>();
    }

    /// <summary>启动后等进房，再逐个机位拍照。</summary>
    private void Start()
    {
        StartCoroutine(Probe());
    }

    /// <summary>等流程进入 InRoom，然后遍历座位拍照。</summary>
    private IEnumerator Probe()
    {
        GameFlowController flow = FindObjectOfType<GameFlowController>();
        Debug.Log("SEATPROBE-BEGIN flowNull=" + (flow == null) + " state=" + (flow != null ? flow.State.ToString() : "-"));
        float deadline = Time.unscaledTime + 25f;
        while (Time.unscaledTime < deadline && (flow == null || flow.State != GameState.InRoom))
        {
            yield return null;
        }

        Debug.Log("SEATPROBE-WAIT-END state=" + (flow != null ? flow.State.ToString() : "-"));
        SeatMarker[] markers = FindObjectsOfType<SeatMarker>();
        System.Array.Sort(markers, (left, right) => left.SeatIndex.CompareTo(right.SeatIndex));
        Debug.Log("SEATPROBE-MARKERS=" + markers.Length);
        Camera camera = Camera.main;
        if (markers.Length == 0 || camera == null)
        {
            Debug.LogWarning("SEATPROBE-NO-SCENE");
            yield break;
        }

        // 角色根节点用 PlayerAnimatorDriver 反查：PlayerSpawner 的实例可能是另一个副本（组合根里动态挂载）。
        PlayerAnimatorDriver driver = FindObjectOfType<PlayerAnimatorDriver>();
        Transform playerRoot = driver != null ? driver.transform : null;
        foreach (SeatMarker marker in markers)
        {
            // 把本地角色挪到当前座位，否则拍出来的是"别的座位上的玩家"，无法判断构图。
            if (playerRoot != null)
            {
                playerRoot.SetPositionAndRotation(marker.SeatPosition, marker.SeatRotation);
            }
            Debug.Log("SEATPROBE-MOVE player=" + (playerRoot != null ? playerRoot.position.ToString("F2") : "null"));

            camera.transform.SetPositionAndRotation(marker.CameraPosition, marker.CameraRotation);
            yield return new WaitForSeconds(0.3f);

            // 用离屏 RenderTexture 同步取图：屏幕截图依赖"当帧真的渲染过"，
            // 编辑器失焦或 WaitForEndOfFrame 不触发时会一直卡住，离屏渲染不受影响。
            const int width = 1280;
            const int height = 720;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            shot.Apply();

            camera.targetTexture = previous;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(target);

            string path = "Temp/seat" + marker.SeatIndex + ".png";
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Object.Destroy(shot);
            Debug.Log("SEATPROBE " + marker.SeatIndex + " cam=" + marker.CameraPosition.ToString("F2") + " -> " + path);
            yield return new WaitForSeconds(0.2f);
        }

        Debug.Log("SEATPROBE-DONE");
    }
}
// touch 1795101223350
