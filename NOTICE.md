# SyncPlayer 参考与版权说明

SyncPlayer 项目源码使用 MIT 许可证，版权声明见 `LICENSE`。开发过程中使用了 OpenAI Codex 辅助。以下项目提供了接口或交互设计参考；列出参考来源不表示其作者认可或参与了 SyncPlayer。

## PotSync — Byaidu

来源：https://github.com/Byaidu/PotSync

参考其 `potsync.py` 中通过 Windows 消息枚举和控制 PotPlayer 的方法，在 C# 中实现窗口查询、播放、暂停与跳转。消息编号作为播放器接口标识使用。同步事件保留 `progress`、`event` 以及 `type`、`cur`、`total`、`state` 等命名。

SyncPlayer 的中转连接、加入审批、主控交换和往返测量为本项目实现，不保证与 PotSync 互通。发布包未包含 PotSync 的 Python 源文件。此前审阅的本地参考副本未包含许可证文件；SyncPlayer 的 MIT 许可证不适用于该参考项目。

## BiuBiuClick — huhai463127310 / 栩风

来源：https://github.com/huhai463127310/BiuBiuClick

参考其多窗口控制流程，以及 `WindowHelper.cs`、`KeyController.cs`、`MouseHook.cs` 中的窗口枚举和输入观察思路。SyncPlayer 使用自己的实现，未将这些类、图像匹配代码、界面素材或程序集纳入发布包。

本地参考副本使用 MIT 许可证，版权为 Copyright (c) 2021 栩风。原许可证：https://github.com/huhai463127310/BiuBiuClick/blob/main/LICENSE

## PotPlayerControl — ld3l

来源：https://github.com/ld3l/PotPlayerControl

PotSync 将该项目列为 PotPlayer 控制接口的参考来源，在此保留对上游接口资料的致谢。发布包未包含其 Java 库或源文件。

## 运行库与界面素材

Windows 独立运行版本包含 Microsoft .NET 和 Windows Forms 运行库。它们保留各自的许可证和第三方条款，见发布包的 `licenses` 文件夹。SyncPlayer 的 MIT 许可证不替代这些条款。

运行库来源：https://github.com/dotnet/runtime 和 https://github.com/dotnet/winforms

语言按钮图标依据用户提供的参考图，经图像生成工具处理后使用。

PotPlayer 需由用户另行安装。发布包不包含播放器、测试视频或其他参考媒体。
