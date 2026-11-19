using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 临时编辑器探针（验证完成后删除）：打印皮肤预制体结构、场景光照与相机设置，
/// 用于定位"角色没有 Animator"和"房间偏暗"两个问题。
/// </summary>
public static class TempSkinProbe
{
    /// <summary>采集并打印诊断信息。</summary>
    public static void Run()
    {
        StringBuilder builder = new StringBuilder("PROBESTART\n");

        SkinCatalog catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>("Assets/Data/SkinCatalog.asset");
        if (catalog != null)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                SkinDefinition skin = catalog.GetAt(i);
                GameObject prefab = skin != null ? skin.ModelPrefab : null;
                if (prefab == null)
                {
                    builder.AppendLine($"skin[{i}] 无模型");
                    continue;
                }

                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                SkinnedMeshRenderer[] skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                builder.AppendLine($"skin[{i}] {prefab.name} children={prefab.transform.childCount} renderers={renderers.Length} skinned={skinned.Length} animator={(animator != null ? animator.name : "null")}");
                if (animator != null)
                {
                    builder.AppendLine($"    avatar={(animator.avatar != null ? animator.avatar.name : "null")} controller={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null")}");
                }

                if (renderers.Length > 0)
                {
                    builder.AppendLine($"    bounds-size={renderers[0].bounds.size} center={renderers[0].bounds.center} scale={prefab.transform.localScale}");
                }
            }
        }
        else
        {
            builder.AppendLine("皮肤总表缺失");
        }

        builder.AppendLine($"ambient-mode={RenderSettings.ambientMode} ambient={RenderSettings.ambientLight} intensity={RenderSettings.ambientIntensity} skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "null")}");

        Light[] lights = Object.FindObjectsOfType<Light>(true);
        foreach (Light light in lights)
        {
            builder.AppendLine($"light {light.name} type={light.type} intensity={light.intensity} color={light.color} shadows={light.shadows} range={light.range}");
        }

        Volume[] volumes = Object.FindObjectsOfType<Volume>(true);
        foreach (Volume volume in volumes)
        {
            string comps = "-";
            if (volume.profile != null)
            {
                comps = string.Join(",", System.Array.ConvertAll(volume.profile.components.ToArray(), c => c.GetType().Name));
            }

            builder.AppendLine($"volume {volume.name} weight={volume.weight} profile={(volume.profile != null ? volume.profile.name : "null")} comps={comps}");
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            builder.AppendLine($"camera {camera.name} pos={camera.transform.position} rot={camera.transform.eulerAngles} fov={camera.fieldOfView} clear={camera.clearFlags} far={camera.farClipPlane}");
            builder.AppendLine($"camera-comps={string.Join(",", System.Array.ConvertAll(camera.GetComponents<Component>(), c => c.GetType().Name))}");
        }

        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            builder.AppendLine($"root {root.name} active={root.activeSelf} children={root.transform.childCount} pos={root.transform.position} scale={root.transform.localScale}");
        }

        GameObject cowboy = GameObject.Find("Cowboy_Female");
        if (cowboy != null)
        {
            Renderer[] cowboyRenderers = cowboy.GetComponentsInChildren<Renderer>(true);
            if (cowboyRenderers.Length > 0)
            {
                Bounds cb = cowboyRenderers[0].bounds;
                for (int i = 1; i < cowboyRenderers.Length; i++)
                {
                    cb.Encapsulate(cowboyRenderers[i].bounds);
                }

                builder.AppendLine($"cowboy center={cb.center} size={cb.size}");
            }
        }

        GameObject chairNode = GameObject.Find("StudyRoom_1/Chair_Brown.001");
        if (chairNode != null)
        {
            MeshFilter chairFilter = chairNode.GetComponent<MeshFilter>();
            if (chairFilter != null && chairFilter.sharedMesh != null)
            {
                Vector3[] verts = chairFilter.sharedMesh.vertices;
                Matrix4x4 toWorld = chairNode.transform.localToWorldMatrix;
                System.Collections.Generic.Dictionary<string, int> buckets = new System.Collections.Generic.Dictionary<string, int>();
                foreach (Vector3 v in verts)
                {
                    Vector3 w = toWorld.MultiplyPoint3x4(v);
                    string key = $"{Mathf.Round(w.x * 2f) / 2f},{Mathf.Round(w.z * 2f) / 2f}";
                    buckets.TryGetValue(key, out int count);
                    buckets[key] = count + 1;
                }

                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> list =
                    new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(buckets);
                list.Sort((lhs, rhs) => rhs.Value.CompareTo(lhs.Value));
                builder.AppendLine($"chair verts={verts.Length} top-buckets={string.Join(" | ", list.ConvertAll(kv => kv.Key + ":" + kv.Value).GetRange(0, Mathf.Min(12, list.Count)))}");
            }
        }

        GameObject room = GameObject.Find("StudyRoom_1");
        if (room != null)
        {
            foreach (Transform child in room.transform)
            {
                Renderer[] childRenderers = child.GetComponentsInChildren<Renderer>(true);
                if (childRenderers.Length == 0)
                {
                    builder.AppendLine($"room-child {child.name} no-renderer");
                    continue;
                }

                Bounds childBounds = childRenderers[0].bounds;
                for (int i = 1; i < childRenderers.Length; i++)
                {
                    childBounds.Encapsulate(childRenderers[i].bounds);
                }

                builder.AppendLine($"room-child {child.name} center={childBounds.center} size={childBounds.size}");
            }
        }

        SeatMarker[] markers = Object.FindObjectsOfType<SeatMarker>(true);
        foreach (SeatMarker marker in markers)
        {
            builder.AppendLine($"seat{marker.SeatIndex} pos={marker.SeatPosition} cam={marker.CameraPosition}");
        }

        Debug.Log(builder.ToString());
    }
}
