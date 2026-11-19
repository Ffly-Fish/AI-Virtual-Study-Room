using System;
using Unity.Barracuda;
using UnityEngine;

/// <summary>
/// 基于摄像头的人脸判定器：用 Barracuda 跑 UltraFace 轻量检测模型，
/// 只要画面里稳定出现一张人脸就认为玩家在屏幕前，检测在本机完成、画面不出设备。
/// 选 UltraFace 而不是带关键点的模型，是因为它只有卷积与激活，能在 Barracuda 上跑通；
/// 没有关键点就用"有人脸"作为专注依据（用户约定的宽松标准：脸朝屏幕即可）。
/// 模型来源与许可见 Assets/Models/MODEL_LICENSE.md。
/// </summary>
public class BarracudaFaceDetector : MonoBehaviour, IFocusDetector
{
    /// <summary>模型输入宽度，必须与 onnx 的输入一致。</summary>
    private const int InputWidth = 320;

    /// <summary>模型输入高度，必须与 onnx 的输入一致。</summary>
    private const int InputHeight = 240;

    /// <summary>模型输入通道数（RGB）。</summary>
    private const int InputChannels = 3;

    /// <summary>推理时把像素值归一化到的除数（UltraFace 的训练约定）。</summary>
    private const float NormalizeScale = 128f;

    /// <summary>归一化时的像素均值（UltraFace 的训练约定）。</summary>
    private const float NormalizeMean = 127.5f;

    [Header("模型")]
    [Tooltip("人脸检测模型（Assets/Models/face_detector.onnx，输入 320x240，输出 scores/boxes）")]
    [SerializeField] private NNModel _model;

    [Header("检测参数")]
    [Tooltip("判定为“画面里有人脸”的最低置信度，太低会把背景误判成人脸")]
    [Range(0.5f, 0.99f)]
    [SerializeField] private float _scoreThreshold = 0.85f;

    [Tooltip("每秒检测次数，8 次足够跟上注意力变化，又不至于发热")]
    [Range(1, 15)]
    [SerializeField] private int _samplesPerSecond = 8;

    [Header("摄像头")]
    [Tooltip("优先使用的摄像头名包含的关键字，留空则自动选前置摄像头")]
    [SerializeField] private string _preferredCameraKeyword = "";

    /// <summary>推理后端：CSharpBurst 在 Android 上不需要额外插件，Reference 只用于排查。</summary>
    private IWorker _worker;

    /// <summary>摄像头纹理，检测与释放都要用。</summary>
    private WebCamTexture _webcamTexture;

    /// <summary>每帧读取的像素缓存，避免反复分配。</summary>
    private Color32[] _pixelBuffer;

    /// <summary>张量输入缓存，避免反复分配。</summary>
    private float[] _inputBuffer;

    /// <summary>上一次采样时间（不受 timeScale 影响，暂停也能恢复）。</summary>
    private float _lastSampleTime;

    /// <summary>最近一次采样结果，Detect 直接返回它，保证一个采样周期内结果稳定。</summary>
    private FocusSample _latestSample;

    /// <summary>摄像头与模型是否都就绪。</summary>
    private bool _isReady;

    /// <summary>是否已经把"模型缺失/摄像头不可用"的警告打出来过，避免每帧刷屏。</summary>
    private bool _hasWarned;

    /// <summary>摄像头与模型都可用时才认为判定可用；不可用时流程应转入旁观模式。</summary>
    public bool IsAvailable => _isReady;

    /// <summary>外部注入模型（GameBootstrap 在运行时装配时使用）。</summary>
    public void SetModel(NNModel model)
    {
        _model = model;
        _isReady = false;
        _hasWarned = false;
        TryInitialize();
    }

    /// <summary>
    /// 启动时准备推理解释后端；只在场景里已经指定模型时才立刻初始化，
    /// 运行时注入模型的情况由 SetModel 负责，避免先报一次"没有模型"的假警告。
    /// </summary>
    private void Awake()
    {
        _latestSample = FocusSample.Empty(Time.unscaledTime);
        if (_model != null)
        {
            TryInitialize();
        }
    }

    /// <summary>按固定频率采样，避免每帧推理把手机烤热。</summary>
    private void Update()
    {
        if (!_isReady)
        {
            return;
        }

        float interval = 1f / Mathf.Max(1, _samplesPerSecond);
        if (Time.unscaledTime - _lastSampleTime < interval)
        {
            return;
        }

        _lastSampleTime = Time.unscaledTime;
        _latestSample = SampleOnce();
    }

    /// <summary>返回最近一次采样结果；没有可用画面时返回空样本。</summary>
    public FocusSample Detect()
    {
        if (!_isReady && _model != null)
        {
            TryInitialize();
        }

        if (!_isReady)
        {
            WarnOnce("[BarracudaFaceDetector] 判定器不可用（缺少模型或摄像头），本帧按无人脸处理。");
            return FocusSample.Empty(Time.unscaledTime);
        }

        return _latestSample;
    }

    /// <summary>退出时释放摄像头与推理后端，否则 Android 上摄像头会被一直占用。</summary>
    public void Release()
    {
        if (_webcamTexture != null)
        {
            if (_webcamTexture.isPlaying)
            {
                _webcamTexture.Stop();
            }

            Destroy(_webcamTexture);
            _webcamTexture = null;
        }

        _worker?.Dispose();
        _worker = null;
        _isReady = false;
    }

    /// <summary>组件销毁时兜底释放。</summary>
    private void OnDestroy()
    {
        Release();
    }

    /// <summary>组件停用时停止采样，避免切后台还在读摄像头。</summary>
    private void OnDisable()
    {
        _isReady = false;
    }

    /// <summary>组件重新启用时尝试恢复（摄像头可能已经被系统回收，需要重建）。</summary>
    private void OnEnable()
    {
        if (_webcamTexture == null || _worker == null)
        {
            TryInitialize();
        }
        else
        {
            _isReady = true;
        }
    }

    /// <summary>准备模型与摄像头，任一步失败就保持不可用并警告一次。</summary>
    private void TryInitialize()
    {
        if (_model == null)
        {
            return;
        }

        if (_worker == null)
        {
            try
            {
                Model runtimeModel = ModelLoader.Load(_model);
                _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, runtimeModel);
            }
            catch (Exception exception)
            {
                Debug.LogError("[BarracudaFaceDetector] 模型加载失败：" + exception.Message, this);
                return;
            }

            _inputBuffer = new float[InputWidth * InputHeight * InputChannels];
        }

        if (_webcamTexture == null)
        {
            if (!TryStartCamera())
            {
                return;
            }
        }

        _isReady = true;
    }

    /// <summary>挑选摄像头并开始播放，返回是否成功。</summary>
    private bool TryStartCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            WarnOnce("[BarracudaFaceDetector] 设备上没有可用摄像头，已退回不可用状态（流程会转入旁观）。");
            return false;
        }

        int index = 0;
        if (!string.IsNullOrEmpty(_preferredCameraKeyword))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].name.IndexOf(_preferredCameraKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    index = i;
                    break;
                }
            }
        }
        else
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing)
                {
                    index = i;
                    break;
                }
            }
        }

        _webcamTexture = new WebCamTexture(devices[index].name, InputWidth, InputHeight, 15);
        _webcamTexture.Play();
        return true;
    }

    /// <summary>跑一次推理并换算成样本；失败返回空样本，不抛异常打断主循环。</summary>
    private FocusSample SampleOnce()
    {
        if (_webcamTexture == null || !_webcamTexture.isPlaying || _webcamTexture.width <= 16)
        {
            return FocusSample.Empty(Time.unscaledTime);
        }

        try
        {
            FillInputBuffer();

            using (Tensor input = new Tensor(1, InputHeight, InputWidth, InputChannels, _inputBuffer))
            {
                _worker.Execute(input);

                Tensor scores = _worker.PeekOutput("scores");
                if (scores == null)
                {
                    return FocusSample.Empty(Time.unscaledTime);
                }

                float best = BestFaceConfidence(scores);
                bool hasFace = best >= _scoreThreshold;
                return new FocusSample
                {
                    IsFacePresent = hasFace,
                    YawDegrees = 0f,
                    Confidence = best,
                    Timestamp = Time.unscaledTime,
                };
            }
        }
        catch (Exception exception)
        {
            WarnOnce("[BarracudaFaceDetector] 推理失败，本帧按无人脸处理：" + exception.Message);
            return FocusSample.Empty(Time.unscaledTime);
        }
    }

    /// <summary>
    /// 把 scores 张量（形状 1x1x4420x2）换算成最高的人脸置信度。
    /// 4420 个先验框里每个框两个类别分数，取第 1 类（人脸）的 softmax 概率。
    /// </summary>
    private static float BestFaceConfidence(Tensor scores)
    {
        float[] data = scores.Flatten().ToReadOnlyArray();
        int anchorCount = data.Length / 2;
        float best = 0f;
        for (int i = 0; i < anchorCount; i++)
        {
            float background = data[i * 2];
            float face = data[i * 2 + 1];
            float max = Mathf.Max(background, face);
            float sum = Mathf.Exp(background - max) + Mathf.Exp(face - max);
            float probability = Mathf.Exp(face - max) / sum;
            if (probability > best)
            {
                best = probability;
            }
        }

        return best;
    }

    /// <summary>把摄像头画面按最近邻缩放到模型输入尺寸并归一化。</summary>
    private void FillInputBuffer()
    {
        int sourceWidth = _webcamTexture.width;
        int sourceHeight = _webcamTexture.height;
        int pixelCount = sourceWidth * sourceHeight;
        if (_pixelBuffer == null || _pixelBuffer.Length != pixelCount)
        {
            _pixelBuffer = new Color32[pixelCount];
        }

        _webcamTexture.GetPixels32(_pixelBuffer);

        for (int y = 0; y < InputHeight; y++)
        {
            int sourceY = y * sourceHeight / InputHeight;
            int rowStart = y * InputWidth * InputChannels;
            for (int x = 0; x < InputWidth; x++)
            {
                int sourceX = x * sourceWidth / InputWidth;
                Color32 pixel = _pixelBuffer[sourceY * sourceWidth + sourceX];
                int index = rowStart + x * InputChannels;
                _inputBuffer[index] = (pixel.r - NormalizeMean) / NormalizeScale;
                _inputBuffer[index + 1] = (pixel.g - NormalizeMean) / NormalizeScale;
                _inputBuffer[index + 2] = (pixel.b - NormalizeMean) / NormalizeScale;
            }
        }
    }

    /// <summary>只警告一次，避免设备没有摄像头时每帧刷日志。</summary>
    private void WarnOnce(string message)
    {
        if (_hasWarned)
        {
            return;
        }

        _hasWarned = true;
        Debug.LogWarning(message, this);
    }
}
