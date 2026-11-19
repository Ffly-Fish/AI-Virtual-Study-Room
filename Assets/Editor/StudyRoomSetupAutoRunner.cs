using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 自动执行钩子：Unity 编译完成后检查标记文件，存在就跑一次「搭建自习室场景」并删除标记。
/// 用途是让"改完脚本 → 回到编辑器自动装配场景"这条链路无需人工点菜单；
/// 标记文件缺失时每个编译周期只做一次 File.Exists 判断，开销可以忽略。
/// </summary>
[InitializeOnLoad]
public static class StudyRoomSetupAutoRunner
{
    /// <summary>标记文件路径：由外部创建表示"请跑一次搭建"。</summary>
    private const string RequestMarker = "Assets/Editor/.studyroom_setup_request";

    /// <summary>静态构造在每次脚本重编译后触发。</summary>
    static StudyRoomSetupAutoRunner()
    {
        EditorApplication.delayCall += TryRunOnce;
    }

    /// <summary>有标记就执行搭建；无论成功与否都先删标记，避免反复执行。</summary>
    private static void TryRunOnce()
    {
        if (!File.Exists(RequestMarker))
        {
            return;
        }

        File.Delete(RequestMarker);
        Debug.Log("[StudyRoomSetupAutoRunner] 检测到搭建请求，开始执行。");

        try
        {
            StudyRoomSetupTool.RunSetup();
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[StudyRoomSetupAutoRunner] 搭建失败：{exception}");
        }
    }
}
