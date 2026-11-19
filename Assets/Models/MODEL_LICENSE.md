# 人脸检测模型出处与许可

## 文件

- `Assets/Models/face_detector.onnx`（1,195,330 字节）
- 由 `Assets/Scripts/Focus/BarracudaFaceDetector.cs` 通过 Unity Barracuda 3.0.2 在本机推理。

## 来源

- 项目：Ultra-Light-Fast-Generic-Face-Detector-1MB
- 作者：Linzaer
- 地址：https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB
- 具体文件：`models/onnx/version-slim-320.onnx`
- 下载方式：直接取仓库原始文件（raw.githubusercontent.com），未做任何修改。

## 许可

上述仓库以 MIT 许可证发布（Copyright (c) Linzaer）。MIT 许可允许商业使用、
修改与再分发，要求在副本中保留版权声明与许可声明。仓库根目录的 LICENSE 文件为原文。

## 模型规格（实测）

- 输入：`input`，NHWC 1x240x320x3，像素按 `(value - 127.5) / 128` 归一化。
- 输出：`scores` 形状 1x1x4420x2（背景/人脸两类分数），`boxes` 形状 1x1x4420x4。
- 本工程只用到 `scores`：任意先验框的人脸概率超过阈值即认为"画面里有人脸"。
  没有解析 boxes，因此不提供人脸位置与角度；判定口径是用户约定的宽松标准（脸朝屏幕即可）。

## 更换模型时要注意

- 更换后必须同步修改 `BarracudaFaceDetector` 里的 `InputWidth` / `InputHeight` 常量与输出张量名。
- 带关键点的模型可以做真正的"头部朝向"判断，但引入的算子（如 PRelu 之外的 Gather/ReduceMax 组合）
  在 Barracuda 上不一定支持，换之前先用编辑器探针确认能跑通。
