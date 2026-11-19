using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 临时自动化开关：轮询工程根目录的标记文件，切换播放或创建 ParrelSync 克隆。
/// 用标记文件而不是 SendKeys，是因为编辑器失焦时按键会被其它前台窗口吃掉。
/// 标记放在工程根（不在 Assets 下），ParrelSync 克隆共用 Assets，放进去会两边同时触发。
/// 阶段 2 自测完成后删除本文件。
/// </summary>
[InitializeOnLoad]
public static class TempPlayToggle
{
    /// <summary>工程根目录（Assets 的上一级）。</summary>
    private static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;

    /// <summary>切换播放的标记文件。</summary>
    private static readonly string PlayRequestPath = Path.Combine(ProjectRoot, ".play_request");

    /// <summary>创建克隆的标记文件。</summary>
    private static readonly string CloneRequestPath = Path.Combine(ProjectRoot, ".clone_request");

    /// <summary>打 Windows 独立包的标记文件。</summary>
    private static readonly string BuildRequestPath = Path.Combine(ProjectRoot, ".build_request");

    /// <summary>注册每帧回调。</summary>
    static TempPlayToggle()
    {
        EditorApplication.update += Tick;
    }

    /// <summary>发现标记就执行对应动作并删掉标记。</summary>
    private static void Tick()
    {
        if (File.Exists(PlayRequestPath))
        {
            File.Delete(PlayRequestPath);
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
            Debug.Log("[TempPlayToggle] isPlaying=" + EditorApplication.isPlaying);
        }

        if (File.Exists(BuildRequestPath))
        {
            File.Delete(BuildRequestPath);
            BuildWindowsPlayer();
        }

        if (File.Exists(CloneRequestPath))
        {
            File.Delete(CloneRequestPath);
            try
            {
                ParrelSync.Project clone = ParrelSync.ClonesManager.CreateCloneFromCurrent();
                Debug.Log("[TempPlayToggle] clone=" + (clone != null ? clone.projectPath : "null"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[TempPlayToggle] 创建克隆失败：" + exception.Message);
            }
        }
    }

    /// <summary>
    /// 打一个 Windows 独立包到工程外的 Build 目录。
    /// 本机 Unity 许可证（Sentinel LDK）只允许同时开一个编辑器，无法用 ParrelSync 双开，
    /// 所以联机验证改为"编辑器 + 独立包"两个进程。
    /// </summary>
    private static void BuildWindowsPlayer()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[TempPlayToggle] 播放中不打包，请先停止 Play。");
            return;
        }

        string buildDirectory = Path.Combine(Directory.GetParent(ProjectRoot).FullName, "StudyRoomBuild");
        Directory.CreateDirectory(buildDirectory);
        string executablePath = Path.Combine(buildDirectory, "StudyRoom.exe");

        UnityEditor.Build.Reporting.BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/StudyScene.unity" },
            executablePath,
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);

        Debug.Log("[TempPlayToggle] build=" + report.summary.result + " size=" + report.summary.totalSize + " path=" + executablePath);
    }
}
// touch 1795101882243
