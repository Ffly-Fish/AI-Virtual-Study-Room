using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一次性搭建工具：生成皮肤总表、按桌子摆放 4 个座位、创建 GameBootstrap 并接好引用。
/// 之所以做成编辑器脚本而不是让用户手拖：座位点与引用共二十余处，
/// 手工接容易漏，脚本可以重复执行且结果一致。
/// 菜单入口：「自习室/搭建自习室场景」。
/// </summary>
public static class StudyRoomSetupTool
{
    /// <summary>目标场景路径；工程里只维护这一个场景。</summary>
    private const string ScenePath = "Assets/Scenes/StudyScene.unity";

    /// <summary>皮肤总表资产路径；不存在时自动创建。</summary>
    private const string CatalogPath = "Assets/Data/SkinCatalog.asset";

    /// <summary>界面中文字体（TMP SDF）。</summary>
    private const string FontPath = "Assets/Front/HONORSansCN SDF Dynamic.asset";

    /// <summary>旧字体资产兜底路径：动态补字失败，仅在动态字体缺失时回退。</summary>
    private const string LegacyFontPath = "Assets/Front/HONORSansCN-Regular SDF 1.asset";

    /// <summary>角色共享动画控制器。</summary>
    private const string AnimatorPath = "Assets/Animator/PlayerAnimator.controller";

    /// <summary>座位根节点名，重复执行时按名字替换。</summary>
    private const string SeatGroupName = "SeatGroup";

    /// <summary>入口对象名。</summary>
    private const string BootstrapName = "GameBootstrap";

    /// <summary>5 套皮肤的配置：编号、显示名、资源路径（第 0 套是默认皮肤，默认已解锁）。</summary>
    private static readonly (byte Id, string DisplayName, string ModelPath)[] SkinTable =
    {
        (0, "休闲男装", "Assets/FBX/Casual_Male.fbx"),
        (1, "休闲男装·二", "Assets/FBX/Casual2_Male.fbx"),
        (2, "厨师帽", "Assets/FBX/Chef_Hat.fbx"),
        (3, "和服", "Assets/FBX/Kimono_Male.fbx"),
        (4, "女巫", "Assets/FBX/Witch.fbx"),
    };

    /// <summary>菜单入口：搭建或刷新自习室场景。</summary>
    [MenuItem("自习室/搭建自习室场景")]
    public static void RunSetup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[StudyRoomSetupTool] 播放模式下不执行搭建，请先退出 Play。");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        SkinCatalog catalog = EnsureCatalog();
        SeatRegistry registry = BuildSeats();
        TagMainCamera();
        CameraDirector director = EnsureCameraDirector();
        EnsureBootstrap(catalog, registry, director);
        HideLegacyCanvas();
        HideLegacyActors();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("[StudyRoomSetupTool] SETUP DONE：皮肤总表、4 个座位、GameBootstrap 与相机已就绪。");
    }

    /// <summary>确保皮肤总表资产存在并写满 5 套皮肤；已存在则只补缺失项。</summary>
    private static SkinCatalog EnsureCatalog()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }

        SkinCatalog catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SkinCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        SerializedObject serialized = new SerializedObject(catalog);
        SerializedProperty skins = serialized.FindProperty("_skins");
        skins.arraySize = SkinTable.Length;

        for (int i = 0; i < SkinTable.Length; i++)
        {
            SerializedProperty element = skins.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("Id").intValue = SkinTable[i].Id;
            element.FindPropertyRelative("DisplayName").stringValue = SkinTable[i].DisplayName;
            element.FindPropertyRelative("PriceCoins").intValue = 1;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SkinTable[i].ModelPath);
            element.FindPropertyRelative("ModelPrefab").objectReferenceValue = model;

            if (model == null)
            {
                Debug.LogWarning($"[StudyRoomSetupTool] 找不到模型 {SkinTable[i].ModelPath}，该皮肤会缺失外观。");
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    /// <summary>按长桌的包围盒摆 4 个座位：长边两侧各 2 个，全部朝向桌子。</summary>
    private static SeatRegistry BuildSeats()
    {
        GameObject existing = GameObject.Find(SeatGroupName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        Renderer tableRenderer = FindRenderer("Table_Long");
        Renderer floorRenderer = FindRenderer("4m_Floor");

        float floorY = floorRenderer != null ? floorRenderer.bounds.max.y : 0f;

        Bounds tableBounds;
        if (tableRenderer != null)
        {
            tableBounds = tableRenderer.bounds;
        }
        else
        {
            // 找不到桌子时退到原点附近的 2x2 布局，保证流程能跑，位置由用户再调整。
            Debug.LogWarning("[StudyRoomSetupTool] 场景里没找到 Table_Long，座位改为按原点 2x2 粗略摆放。");
            tableBounds = new Bounds(Vector3.zero, new Vector3(2f, 0.75f, 1f));
        }

        GameObject group = new GameObject(SeatGroupName);
        SeatRegistry registry = group.AddComponent<SeatRegistry>();

        bool alongX = tableBounds.size.x >= tableBounds.size.z;
        float longAxis = Mathf.Max(tableBounds.size.x, tableBounds.size.z);

        for (int i = 0; i < 4; i++)
        {
            int sideIndex = i / 2;
            int indexOnSide = i % 2;
            float sideSign = sideIndex == 0 ? 1f : -1f;
            float alongRatio = indexOnSide == 0 ? -0.22f : 0.22f;

            Vector3 seatPosition = tableBounds.center;
            if (alongX)
            {
                seatPosition.x += longAxis * alongRatio;
                seatPosition.z += sideSign * (tableBounds.size.z * 0.5f + 0.45f);
            }
            else
            {
                seatPosition.z += longAxis * alongRatio;
                seatPosition.x += sideSign * (tableBounds.size.x * 0.5f + 0.45f);
            }

            seatPosition.y = floorY;

            Vector3 faceDirection = tableBounds.center - seatPosition;
            faceDirection.y = 0f;
            Quaternion seatRotation = faceDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(faceDirection)
                : Quaternion.identity;

            GameObject seatObject = new GameObject($"Seat_{i + 1}");
            seatObject.transform.SetParent(group.transform, false);
            seatObject.transform.SetPositionAndRotation(seatPosition, seatRotation);

            GameObject seatPoint = new GameObject("SeatPoint");
            seatPoint.transform.SetParent(seatObject.transform, false);

            GameObject cameraPoint = new GameObject("CameraPoint");
            cameraPoint.transform.SetParent(seatObject.transform, false);

            // 相机：放到桌子对面（座位正前方）4.6 米、离地 3.0 米的斜上方，并向侧向偏移 1.6 米避开桌面花瓶。
            // 本作房间是约 2 倍尺度（桌面高 1.38 米、角色身高 3.4 米），座位贴着桌子、背后就是墙，
            // 因此只能从桌对侧回看角色——这样既能看到角色全身与桌面，也能看到同桌的其他玩家。
            Vector3 forwardDirection = seatRotation * Vector3.forward;
            Vector3 sideDirection = seatRotation * Vector3.right;
            Vector3 cameraPosition = seatPosition + forwardDirection * 4.6f + Vector3.up * 3.0f + sideDirection * 1.6f;
            Vector3 lookTarget = seatPosition + Vector3.up * 1.75f;
            cameraPoint.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(lookTarget - cameraPosition));

            SeatMarker marker = seatObject.AddComponent<SeatMarker>();
            SerializedObject markerSerialized = new SerializedObject(marker);
            markerSerialized.FindProperty("_seatIndex").intValue = i + 1;
            markerSerialized.FindProperty("_seatPoint").objectReferenceValue = seatPoint.transform;
            markerSerialized.FindProperty("_cameraPoint").objectReferenceValue = cameraPoint.transform;
            markerSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject registrySerialized = new SerializedObject(registry);
        SerializedProperty markers = registrySerialized.FindProperty("_markers");
        markers.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            markers.GetArrayElementAtIndex(i).objectReferenceValue = group.transform.Find($"Seat_{i + 1}").GetComponent<SeatMarker>();
        }

        registrySerialized.ApplyModifiedPropertiesWithoutUndo();
        return registry;
    }

    /// <summary>按名字关键字找场景里第一个渲染器（用于量取桌子与地板的包围盒）。</summary>
    private static Renderer FindRenderer(string nameKeyword)
    {
        return Object.FindObjectsOfType<Renderer>()
            .Where(r => r.gameObject.name.IndexOf(nameKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z)
            .FirstOrDefault();
    }

    /// <summary>确保场景相机带 MainCamera 标签，否则 Camera.main 为 null，过场拿不到相机。</summary>
    private static void TagMainCamera()
    {
        if (Camera.main != null)
        {
            return;
        }

        Camera[] cameras = Object.FindObjectsOfType<Camera>();
        if (cameras.Length == 0)
        {
            Debug.LogError("[StudyRoomSetupTool] 场景里没有相机。");
            return;
        }

        cameras[0].tag = "MainCamera";
        Debug.Log($"[StudyRoomSetupTool] 已把 {cameras[0].name} 标记为 MainCamera。");
    }

    /// <summary>找主相机上的相机导演，没有就挂一个。</summary>
    private static CameraDirector EnsureCameraDirector()
    {
        CameraDirector director = Object.FindObjectOfType<CameraDirector>();
        if (director != null)
        {
            return director;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return null;
        }

        return mainCamera.gameObject.AddComponent<CameraDirector>();
    }

    /// <summary>创建或更新入口对象，并把字体、皮肤表、动画控制器、座位表、相机导演接上。</summary>
    private static void EnsureBootstrap(SkinCatalog catalog, SeatRegistry registry, CameraDirector director)
    {
        GameObject bootstrapObject = GameObject.Find(BootstrapName);
        if (bootstrapObject == null)
        {
            bootstrapObject = new GameObject(BootstrapName);
        }

        GameBootstrap bootstrap = bootstrapObject.GetComponent<GameBootstrap>();
        if (bootstrap == null)
        {
            bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
        }

        SerializedObject serialized = new SerializedObject(bootstrap);
        TMP_FontAsset uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (uiFont == null)
        {
            uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyFontPath);
        }

        serialized.FindProperty("_uiFont").objectReferenceValue = uiFont;
        serialized.FindProperty("_skinCatalog").objectReferenceValue = catalog;
        serialized.FindProperty("_animatorController").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorPath);
        serialized.FindProperty("_seatRegistry").objectReferenceValue = registry;
        serialized.FindProperty("_cameraDirector").objectReferenceValue = director;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (serialized.FindProperty("_uiFont").objectReferenceValue == null)
        {
            Debug.LogWarning($"[StudyRoomSetupTool] 找不到中文字体 {FontPath}，界面中文会显示为方块。");
        }
    }

    /// <summary>把工程里旧的测试用 Canvas 停用，避免和运行时构建的界面重叠（不删除，随时可复原）。</summary>
    /// <summary>
    /// 停用场景里遗留的演示角色：这些对象自带世界空间"用户名"画布，
    /// 会和本作的玩家角色、名字牌重叠，且不参与流程，因此直接隐藏而不是删除。
    /// </summary>
    private static void HideLegacyActors()
    {
        string[] legacyNames = { "Cowboy_Female" };
        foreach (string legacyName in legacyNames)
        {
            GameObject legacy = GameObject.Find(legacyName);
            if (legacy != null && legacy.activeSelf)
            {
                legacy.SetActive(false);
                Debug.Log($"[StudyRoomSetupTool] 已停用遗留演示角色：{legacyName}");
            }
        }
    }

    private static void HideLegacyCanvas()
    {
        foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                continue;
            }

            if (canvas.transform.root.name == BootstrapName)
            {
                continue;
            }

            if (canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(false);
                Debug.Log($"[StudyRoomSetupTool] 已停用旧界面 {canvas.name}（需要时可在层级里重新勾选）。");
            }
        }
    }
}
