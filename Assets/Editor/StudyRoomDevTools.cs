using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 开发期自动化入口：Unity 失焦时无法用快捷键进入/退出播放，
/// 这里改用工程根目录的标记文件触发，脚本重载后依然可用。
/// 只处理标记文件，平时不产生任何运行时行为。
/// </summary>
[InitializeOnLoad]
public static class StudyRoomDevTools
{
    /// <summary>进入播放的标记文件（放在工程根，避免被 Unity 当作资源导入）。</summary>
    private const string PlayMarker = ".dev_play";

    /// <summary>退出播放的标记文件。</summary>
    private const string StopMarker = ".dev_stop";

    /// <summary>打包 Windows 版的标记文件：命令行/自动化里无法点菜单时用它触发打包。</summary>
    private const string BuildWindowsMarker = ".dev_build_windows";

    /// <summary>工程根目录（Assets 的上一级）。</summary>
    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    /// <summary>脚本重载后挂到编辑器更新回调上。</summary>
    static StudyRoomDevTools()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    /// <summary>检查标记文件并执行对应动作。</summary>
    private static void Tick()
    {
        if (!EditorApplication.isPlaying && File.Exists(Path.Combine(ProjectRoot, PlayMarker)))
        {
            File.Delete(Path.Combine(ProjectRoot, PlayMarker));
            EnterPlay();
        }

        if (EditorApplication.isPlaying && File.Exists(Path.Combine(ProjectRoot, StopMarker)))
        {
            File.Delete(Path.Combine(ProjectRoot, StopMarker));
            EditorApplication.isPlaying = false;
            Debug.Log("[StudyRoomDevTools] 已退出播放。");
        }

        if (!EditorApplication.isPlaying && File.Exists(Path.Combine(ProjectRoot, BuildWindowsMarker)))
        {
            File.Delete(Path.Combine(ProjectRoot, BuildWindowsMarker));
            Debug.Log("[StudyRoomDevTools] 收到打包请求，开始打 Windows 版。");
            StudyRoomBuilder.BuildWindows();
        }
    }

    /// <summary>菜单入口：进入播放（先确保打开 StudyScene）。</summary>
    [MenuItem("自习室/开发工具/进入播放")]
    public static void EnterPlay()
    {
        if (EditorApplication.isPlaying)
        {
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/StudyScene.unity")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/StudyScene.unity");
        }

        EditorApplication.isPlaying = true;
        Debug.Log("[StudyRoomDevTools] 已进入播放。");
    }

    /// <summary>菜单入口：退出播放。</summary>
    [MenuItem("自习室/开发工具/退出播放")]
    public static void StopPlay()
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }
    }
}
// touch 1795108909070
