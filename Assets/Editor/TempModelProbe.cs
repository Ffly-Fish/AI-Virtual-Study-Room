using Unity.Barracuda;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 临时探针（验证完成后删除）：检查人脸检测 ONNX 模型能否被 Barracuda 加载与执行，
/// 打印输入输出张量的名字与形状，用于确认该模型可以在本机跑通。
/// </summary>
public static class TempModelProbe
{
    /// <summary>模型路径。</summary>
    private const string ModelPath = "Assets/Models/face_detector.onnx";

    /// <summary>执行探测。</summary>
    public static void Run()
    {
        Debug.Log("MODELPROBE-START path=" + ModelPath);

        NNModel asset = AssetDatabase.LoadAssetAtPath<NNModel>(ModelPath);
        if (asset == null)
        {
            Debug.LogError("MODELPROBE-ASSET-NULL 没有把 onnx 导入成 NNModel");
            return;
        }

        Model model = ModelLoader.Load(asset);
        Debug.Log("MODELPROBE-MODEL inputs=" + model.inputs.Count + " outputs=" + model.outputs.Count);

        int[] inputShape = model.inputs[0].shape;
        Debug.Log("MODELPROBE-IN " + model.inputs[0].name + " [" + string.Join(",", inputShape) + "]");

        for (int i = 0; i < model.outputs.Count; i++)
        {
            Debug.Log("MODELPROBE-OUTNAME " + model.outputs[i]);
        }

        int height = inputShape[inputShape.Length - 3];
        int width = inputShape[inputShape.Length - 2];
        int channels = inputShape[inputShape.Length - 1];
        Debug.Log("MODELPROBE-SHAPE height=" + height + " width=" + width + " channels=" + channels);

        try
        {
            using (IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharp, model))
            {
                float[] buffer = new float[height * width * channels];
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = 0.5f;
                }

                using (Tensor input = new Tensor(1, height, width, channels, buffer))
                {
                    worker.Execute(input);
                    for (int i = 0; i < model.outputs.Count; i++)
                    {
                        Tensor output = worker.PeekOutput(model.outputs[i]);
                        if (output != null)
                        {
                            Debug.Log("MODELPROBE-TENSOR " + model.outputs[i] + " " + output.shape.ToString() + " len=" + output.length);
                        }
                    }
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError("MODELPROBE-RUN-FAILED " + exception.GetType().Name + " " + exception.Message);
            Debug.LogError("MODELPROBE-STACK " + exception.StackTrace);
        }

        Debug.Log("MODELPROBE-DONE width-agnostic");
    }
}
