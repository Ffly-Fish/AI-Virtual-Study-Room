using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 一键打包工具：菜单在「自习室」下。
/// 场景列表直接读 EditorBuildSettings，避免两处配置不一致（少一个场景就会进游戏黑屏）。
/// </summary>
public static class StudyRoomBuilder
{
    /// <summary>桌面版输出目录（工程根的上一级，免得被当成工程资源导入）。</summary>
    private const string WindowsOutputFolder = "../StudyRoomBuild";

    /// <summary>安卓包输出路径。</summary>
    private const string AndroidOutputPath = "../StudyRoomBuild/StudyRoom.apk";

    /// <summary>打包 Windows 64 位版，产物在 E:/UnityProject/StudyRoomBuild/StudyRoom.exe。</summary>
    [MenuItem("自习室/打包 Windows 版")]
    public static void BuildWindows()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, WindowsOutputFolder));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "StudyRoom.exe");
        Report(BuildPipeline.BuildPlayer(CollectScenes(), path, BuildTarget.StandaloneWindows64, BuildOptions.None), path);
    }

    /// <summary>打包安卓 APK（IL2CPP + ARM64，权限见 Assets/Plugins/Android/AndroidManifest.xml）。</summary>
    [MenuItem("自习室/打包 Android 版")]
    public static void BuildAndroid()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, AndroidOutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.dataPath);
        Report(BuildPipeline.BuildPlayer(CollectScenes(), path, BuildTarget.Android, BuildOptions.None), path);
    }

    /// <summary>取 Build Settings 里的启用场景，空表时直接报错，避免打出空包。</summary>
    private static string[] CollectScenes()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        string[] paths = new string[scenes.Length];
        int count = 0;
        foreach (EditorBuildSettingsScene scene in scenes)
        {
            if (scene != null && scene.enabled)
            {
                paths[count++] = scene.path;
            }
        }

        if (count == 0)
        {
            throw new InvalidOperationException("Build Settings 里没有启用任何场景，无法打包。");
        }

        Array.Resize(ref paths, count);
        return paths;
    }

    /// <summary>统一打印打包结果，失败时把第一条错误打进 Console 便于定位。</summary>
    private static void Report(BuildReport report, string path)
    {
        if (report == null)
        {
            Debug.LogError("[StudyRoomBuilder] 打包没有返回结果，请检查 Console。");
            return;
        }

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log("[StudyRoomBuilder] 打包成功：" + path + "，大小 " + (report.summary.totalSize / 1024 / 1024) + " MB");
            return;
        }

        Debug.LogError("[StudyRoomBuilder] 打包失败：" + report.summary.result
                       + "，错误数 " + report.summary.totalErrors + "，输出 " + path);
    }
}
