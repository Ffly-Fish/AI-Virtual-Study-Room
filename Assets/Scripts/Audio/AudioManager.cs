using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音频控制器（单例）。
/// 统一管理背景音乐与音效：
/// 1. 在面板中以「名称 + 音频文件」的形式登记音频资源，供运行时按名称查找；
/// 2. 对外提供「背景音乐播放」与「音效播放」两种模式，参数均为（名称, 音量）。
/// 调用示例：AudioManager.Instance.PlayBackgroundMusic("背景音乐", 0.6f);
/// </summary>
[AddComponentMenu("音频/音频控制器")]
[DefaultExecutionOrder(-100)]
public class AudioManager : MonoBehaviour
{
    /// <summary>
    /// 音频登记项：把「名称」与「音频文件」绑定在一起，
    /// 便于在面板中拖拽资源并为每个资源起一个可读的名字。
    /// </summary>
    [Serializable]
    public struct AudioEntry
    {
        /// <summary>音频名称，播放时按此名称查找，必须唯一</summary>
        [Header("音频名称")]
        [Tooltip("音频名称：播放时通过该名称查找音频；不能为空，且不可与其它条目重复")]
        public string Name;

        /// <summary>实际播放的音频文件</summary>
        [Header("音频文件")]
        [Tooltip("音频文件：从工程中把对应的 AudioClip 拖到这里")]
        public AudioClip Clip;
    }

    /// <summary>全局唯一实例，其它脚本统一通过 AudioManager.Instance 访问</summary>
    public static AudioManager Instance { get; private set; }

    [Header("音频资源列表")]
    [Tooltip("在此登记所有音频：填写名称并拖入音频文件；名称必须唯一，播放时按名称查找")]
    [SerializeField] private List<AudioEntry> _audioEntries = new List<AudioEntry>();

    [Header("背景音乐设置")]
    [Tooltip("切换或停止背景音乐时的淡入淡出时长（秒），填 0 表示立即切换")]
    [SerializeField, Min(0f)] private float _bgmFadeDuration = 0.5f;

    [Header("跨场景常驻")]
    [Tooltip("勾选后切换场景时不销毁本对象，背景音乐可跨场景连续播放")]
    [SerializeField] private bool _dontDestroyOnLoad = true;

    [Header("播放器（可留空）")]
    [Tooltip("背景音乐播放器：留空时在 Awake 中自动创建")]
    [SerializeField] private AudioSource _bgmSource;

    [Tooltip("音效播放器：留空时在 Awake 中自动创建")]
    [SerializeField] private AudioSource _sfxSource;

    /// <summary>名称到音频文件的查找表，Awake 时由面板列表构建</summary>
    private readonly Dictionary<string, AudioClip> _clipMap = new Dictionary<string, AudioClip>();

    /// <summary>当前正在执行的背景音乐淡入淡出协程，切换音乐时需要先打断</summary>
    private Coroutine _bgmFadeCoroutine;

    /// <summary>初始化单例、构建查找表、准备播放器</summary>
    private void Awake()
    {
        // 单例：场景中已存在实例时销毁后出现的重复对象，保证全局唯一
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AudioManager] 场景中存在多个音频控制器，已销毁重复对象。", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 只有根对象才能常驻，避免把子对象拖出层级后报错
        if (_dontDestroyOnLoad && transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }

        BuildClipMap();
        EnsureAudioSources();
    }

    /// <summary>销毁时清空静态引用，避免域重载或切换场景后残留失效实例</summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 播放背景音乐（循环播放；重复播放同一首时只调整音量，不打断播放进度）。
    /// 若已有其它背景音乐在播放，会按 _bgmFadeDuration 交叉淡入淡出。
    /// </summary>
    /// <param name="audioName">面板中登记的音频名称</param>
    /// <param name="volume">音量，0~1，超出范围会被自动截断</param>
    public void PlayBackgroundMusic(string audioName, float volume = 1f)
    {
        AudioClip clip = GetClip(audioName);
        if (clip == null)
        {
            return;
        }

        StopBgmFadeCoroutine();

        float targetVolume = Mathf.Clamp01(volume);

        // 正在播放同一首：仅更新音量，避免从 0 重新播放
        if (_bgmSource.clip == clip && _bgmSource.isPlaying)
        {
            _bgmSource.volume = targetVolume;
            return;
        }

        _bgmFadeCoroutine = StartCoroutine(CrossFadeBackgroundMusic(clip, targetVolume));
    }

    /// <summary>
    /// 播放一次音效（可叠加多个音效，不打断背景音乐）。
    /// </summary>
    /// <param name="audioName">面板中登记的音频名称</param>
    /// <param name="volume">音量，0~1，超出范围会被自动截断</param>
    public void PlaySoundEffect(string audioName, float volume = 1f)
    {
        AudioClip clip = GetClip(audioName);
        if (clip == null)
        {
            return;
        }

        // PlayOneShot 的第二个参数是音量缩放，最终音量 = 播放器音量 × 缩放
        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>停止背景音乐（按 _bgmFadeDuration 淡出后停止）</summary>
    public void StopBackgroundMusic()
    {
        if (!_bgmSource.isPlaying)
        {
            return;
        }

        StopBgmFadeCoroutine();
        _bgmFadeCoroutine = StartCoroutine(FadeOutBackgroundMusic());
    }

    /// <summary>按名称取出音频文件，找不到时打印警告并返回 null</summary>
    /// <param name="audioName">面板中登记的音频名称</param>
    private AudioClip GetClip(string audioName)
    {
        if (string.IsNullOrWhiteSpace(audioName))
        {
            Debug.LogWarning("[AudioManager] 播放音频时传入的名称为空。", this);
            return null;
        }

        if (_clipMap.TryGetValue(audioName, out AudioClip clip) && clip != null)
        {
            return clip;
        }

        Debug.LogWarning($"[AudioManager] 未找到名为「{audioName}」的音频，请检查面板中的音频资源列表。", this);
        return null;
    }

    /// <summary>用面板列表构建名称 → 音频文件的查找表，并对非法数据给出警告</summary>
    private void BuildClipMap()
    {
        _clipMap.Clear();

        if (_audioEntries == null)
        {
            _audioEntries = new List<AudioEntry>();
            return;
        }

        for (int i = 0; i < _audioEntries.Count; i++)
        {
            AudioEntry entry = _audioEntries[i];
            string displayIndex = (i + 1).ToString();

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Debug.LogWarning($"[AudioManager] 音频资源列表第 {displayIndex} 项的名称为空，该音频无法被播放。", this);
                continue;
            }

            if (entry.Clip == null)
            {
                Debug.LogWarning($"[AudioManager] 音频「{entry.Name}」没有拖入音频文件，将被忽略。", this);
                continue;
            }

            if (_clipMap.ContainsKey(entry.Name))
            {
                Debug.LogWarning($"[AudioManager] 音频名称「{entry.Name}」重复，只保留第一条。", this);
                continue;
            }

            _clipMap.Add(entry.Name, entry.Clip);
        }
    }

    /// <summary>确保两个播放器可用：缺失时自动创建子对象并挂载 AudioSource</summary>
    private void EnsureAudioSources()
    {
        if (_bgmSource == null)
        {
            _bgmSource = CreateChildAudioSource("BGM Source");
        }

        if (_sfxSource == null)
        {
            _sfxSource = CreateChildAudioSource("SFX Source");
        }

        // 背景音乐循环播放
        _bgmSource.loop = true;
        _bgmSource.playOnAwake = false;
        _bgmSource.spatialBlend = 0f; // 2D 播放，不受相机与物体位置影响

        // 音效不循环，由 PlayOneShot 控制每次播放
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;
        _sfxSource.spatialBlend = 0f;
    }

    /// <summary>在本对象下创建一个带 AudioSource 的子对象</summary>
    /// <param name="sourceName">子对象名称</param>
    private AudioSource CreateChildAudioSource(string sourceName)
    {
        GameObject sourceObject = new GameObject(sourceName);
        sourceObject.transform.SetParent(transform, false);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        return source;
    }

    /// <summary>
    /// 背景音乐交叉淡入淡出：旧音乐淡出 → 切换音频 → 新音乐淡入。
    /// 使用 unscaledDeltaTime，保证游戏暂停（Time.timeScale = 0）时淡变仍能正常推进。
    /// </summary>
    /// <param name="clip">要播放的新音乐</param>
    /// <param name="targetVolume">淡入结束后的目标音量</param>
    private IEnumerator CrossFadeBackgroundMusic(AudioClip clip, float targetVolume)
    {
        float fadeDuration = Mathf.Max(0f, _bgmFadeDuration);

        // 旧音乐淡出
        if (_bgmSource.isPlaying && fadeDuration > 0f)
        {
            float startVolume = _bgmSource.volume;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _bgmSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
                yield return null;
            }
        }

        _bgmSource.Stop();
        _bgmSource.clip = clip;
        _bgmSource.loop = true;
        _bgmSource.volume = fadeDuration > 0f ? 0f : targetVolume;
        _bgmSource.Play();

        // 新音乐淡入
        if (fadeDuration > 0f)
        {
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _bgmSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeDuration);
                yield return null;
            }
        }

        _bgmSource.volume = targetVolume;
        _bgmFadeCoroutine = null;
    }

    /// <summary>背景音乐淡出后停止，并把音量恢复到淡出前的值</summary>
    private IEnumerator FadeOutBackgroundMusic()
    {
        float fadeDuration = Mathf.Max(0f, _bgmFadeDuration);
        float startVolume = _bgmSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _bgmSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
            yield return null;
        }

        _bgmSource.Stop();
        _bgmSource.volume = startVolume;
        _bgmFadeCoroutine = null;
    }

    /// <summary>打断并清空当前正在执行的背景音乐淡变协程</summary>
    private void StopBgmFadeCoroutine()
    {
        if (_bgmFadeCoroutine == null)
        {
            return;
        }

        StopCoroutine(_bgmFadeCoroutine);
        _bgmFadeCoroutine = null;
    }
}
