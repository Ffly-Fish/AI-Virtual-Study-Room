using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 临时联机诊断：自动填名字、点进入，然后周期性打印会话状态、人数、座位与角色。
/// 仅用于阶段 2 自测，联调完成后整目录删除。
/// </summary>
public class TempNetReporter : MonoBehaviour
{
    /// <summary>进入播放后等待界面构建完成的秒数。</summary>
    private const float Delay = 3f;

    /// <summary>自动挂载，避免改场景。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        GameObject host = new GameObject("TempNetReporter");
        host.AddComponent<TempNetReporter>();
    }

    /// <summary>启动即开始驱动。</summary>
    private void Start()
    {
        // 座位机位探针挂在同一个对象上：它的 AutoInstall 在部分播放态下不生效，
        // 由这里显式挂载最稳，两个诊断的生命周期也一起收尾。
        if (GetComponent<TempSeatProbe>() == null)
        {
            gameObject.AddComponent<TempSeatProbe>();
            Debug.Log("NETREPORT-ATTACH-PROBE ok");
        }
        else
        {
            Debug.Log("NETREPORT-ATTACH-PROBE exists");
        }

        StartCoroutine(Drive());
    }

    /// <summary>等界面就绪 → 填名字 → 点进入 → 周期打印。</summary>
    private IEnumerator Drive()
    {
        yield return new WaitForSeconds(Delay);

        GameObject login = GameObject.Find("UiCanvas/LoginPanel");
        if (login == null)
        {
            Debug.LogError("NETREPORT-NO-LOGIN");
            yield break;
        }

        TMP_InputField input = login.GetComponentInChildren<TMP_InputField>(true);
        if (input != null)
        {
            input.text = "测试玩家" + Random.Range(10, 99);
        }

        Button enter = FindButton(login.transform, "EnterButton");
        if (enter == null)
        {
            Debug.LogError("NETREPORT-NO-BUTTON");
            yield break;
        }

        Debug.Log("NETREPORT-CLICK-ENTER");
        enter.onClick.Invoke();

        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSeconds(2f);
            LogState(i);

            // 第 2 拍之后停掉本地流程（它会用摄像头判定覆盖专注状态），
            // 随后手动切换专注，用来看房主那边收到的是不是同一个值。
            if (i == 2)
            {
                GameFlowController flow = FindObjectOfType<GameFlowController>();
                if (flow != null && !NetworkManager.Singleton.IsHost)
                {
                    flow.enabled = false;
                    Debug.Log("NETREPORT-FLOW-OFF");
                }
            }

            if (i == 3 || i == 7)
            {
                SetLocalFocus(true);
            }

            if (i == 5 || i == 9)
            {
                SetLocalFocus(false);
            }
        }

        // 写到 persistentDataPath：编辑器与独立包的相对路径解析不同，写到固定可写目录才能都拿到。
        string shotPath = System.IO.Path.Combine(Application.persistentDataPath, "net_shot.png");
        ScreenCapture.CaptureScreenshot(shotPath);
        Debug.Log("NETREPORT-SHOT " + shotPath);
        Debug.Log("NETREPORT-SHOT");
    }

    /// <summary>切换本机角色的专注状态（只有 Owner 能写这个网络变量）。</summary>
    private static void SetLocalFocus(bool focusing)
    {
        foreach (PlayerNetwork player in FindObjectsOfType<PlayerNetwork>())
        {
            if (!player.IsLocalPlayerOwner)
            {
                continue;
            }

            player.SetFocusing(focusing);
            Debug.Log("NETREPORT-SET-FOCUS " + focusing);
            return;
        }
    }

    /// <summary>按名字找按钮，找不到名字就取第一个。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button.name == buttonName)
            {
                return button;
            }
        }

        return buttons.Length > 0 ? buttons[0] : null;
    }

    /// <summary>打印当前联机状态。</summary>
    private static void LogState(int index)
    {
        GameFlowController flow = FindObjectOfType<GameFlowController>();
        NetworkManager manager = NetworkManager.Singleton;
        PlayerNetwork local = null;
        foreach (PlayerNetwork player in FindObjectsOfType<PlayerNetwork>())
        {
            if (player.IsLocalPlayerOwner)
            {
                local = player;
                break;
            }
        }

        int players = manager != null && manager.IsListening ? manager.ConnectedClientsIds.Count : 0;
        string state = flow != null ? flow.State.ToString() : "null";
        string role = manager == null ? "none" : (manager.IsHost ? "host" : (manager.IsClient ? "client" : "off"));
        string seat = local != null ? local.SeatIndex.ToString() : "-";
        string pos = local != null ? local.transform.position.ToString("F2") : "-";
        Camera camera = Camera.main;
        string camPos = camera != null ? camera.transform.position.ToString("F2") : "-";

        Debug.Log($"NETREPORT-{index} state={state} role={role} players={players} seat={seat} local={local != null} pos={pos} cam={camPos}");

        // 逐个列出场上所有玩家，验证名字/座位/皮肤/专注这几个网络变量是否真的同步到了另一端。
        foreach (PlayerNetwork player in FindObjectsOfType<PlayerNetwork>())
        {
            Debug.Log($"NETREPORT-PLAYER name={player.DisplayName} seat={player.SeatIndex} skin={player.SkinId} focus={player.IsFocusing} owner={player.IsLocalPlayerOwner}");
        }
    }
}
