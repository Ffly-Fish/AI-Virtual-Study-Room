using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 临时触发器（验证完成后删除）：检测标记文件后调用探针与场景搭建。
/// </summary>
[InitializeOnLoad]
public static class TempProbeRunner
{
    /// <summary>探针标记文件。</summary>
    private const string ProbeMarker = "Assets/Editor/.probe_request";

    /// <summary>场景搭建标记文件（StudyRoomSetupAutoRunner 也监听它）。</summary>
    private const string SetupMarker = "Assets/Editor/.studyroom_setup_request";

    /// <summary>编辑器加载后延迟执行。</summary>
    static TempProbeRunner()
    {
        EditorApplication.delayCall += TryRun;
    }

    /// <summary>按标记执行，跑完删除标记。</summary>
    private static void TryRun()
    {
        if (File.Exists(ProbeMarker))
        {
            File.Delete(ProbeMarker);
            try
            {
                TempSkinProbe.Run();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("PROBE 执行失败：" + exception);
            }
        }

        if (File.Exists(SetupMarker))
        {
            Debug.Log("PROBE 场景搭建标记存在，交给 StudyRoomSetupAutoRunner 处理");
        }
    }
}
// touch 1795096691207
