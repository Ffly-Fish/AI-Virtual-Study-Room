using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 座位总表：把场景里所有 <see cref="SeatMarker"/> 收集起来，按编号索引。
/// 座位是"场景布置"而不是"运行时生成"的，所以这里只做收集与校验，不创建对象。
/// </summary>
public class SeatRegistry : MonoBehaviour
{
    /// <summary>手动指定的座位列表；留空则在 Awake 时从子物体里自动收集。</summary>
    [Header("座位标记")]
    [Tooltip("场景里的座位标记，可从子物体自动收集；必须按编号唯一")]
    [SerializeField] private List<SeatMarker> _markers = new List<SeatMarker>();

    /// <summary>编号 -> 座位，运行期索引表。</summary>
    private readonly Dictionary<int, SeatMarker> _byIndex = new Dictionary<int, SeatMarker>();

    /// <summary>座位总数。</summary>
    public int Count => _byIndex.Count;

    /// <summary>启动时建立索引：列表为空则自动收集子物体，保证忘记拖引用也能跑。</summary>
    private void Awake()
    {
        if (_markers == null || _markers.Count == 0)
        {
            _markers = new List<SeatMarker>(GetComponentsInChildren<SeatMarker>(true));
        }

        Rebuild();
    }

    /// <summary>按编号取座位；不存在返回 null，调用方必须判空（房间已满时就是 null）。</summary>
    public SeatMarker GetMarker(int seatIndex)
    {
        return _byIndex.TryGetValue(seatIndex, out SeatMarker marker) ? marker : null;
    }

    /// <summary>按编号升序返回全部座位，用于"取最小空闲座位号"。</summary>
    public List<SeatMarker> GetSortedMarkers()
    {
        List<SeatMarker> result = new List<SeatMarker>(_byIndex.Values);
        result.Sort((a, b) => a.SeatIndex.CompareTo(b.SeatIndex));
        return result;
    }

    /// <summary>重新建立索引并做唯一性校验；重复编号只保留第一个并警告。</summary>
    public void Rebuild()
    {
        _byIndex.Clear();

        foreach (SeatMarker marker in _markers)
        {
            if (marker == null)
            {
                continue;
            }

            marker.Validate();

            if (_byIndex.ContainsKey(marker.SeatIndex))
            {
                Debug.LogWarning($"[SeatRegistry] 座位编号 {marker.SeatIndex} 重复，已忽略 {marker.name}。", marker);
                continue;
            }

            _byIndex.Add(marker.SeatIndex, marker);
        }

        if (_byIndex.Count == 0)
        {
            Debug.LogError("[SeatRegistry] 场景里没有任何座位标记，玩家将无法生成。请在场景里摆 SeatMarker。", this);
        }
    }
}
