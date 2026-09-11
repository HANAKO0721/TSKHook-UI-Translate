# 从源码构建

本仓库包含 TSKHook UI 发布版 v0.1.0 的对应源码。插件内部版本为 0.3.4，游戏加载日志与 DLL 文件版本沿用该版本号。

## 准备环境

1. 在 Windows 上安装 [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)。
2. 按照 [TSKModding/TSKHook](https://github.com/TSKModding/TSKHook) 的说明安装并运行 TSKHook v1.1.6，准备好游戏目录中的 `BepInEx/core/`、`BepInEx/interop/` 和 `BepInEx/plugins/TSKHook.dll`。
3. 下载该压缩包


## 发布整理记录

2026-09-11：保留原有 C# 实现；构建项目改为通过 `GamePath` 引用已安装的上游 DLL，构建脚本使用本机安装的 .NET SDK。发布包、仓库及源码按根目录 [LICENSE](LICENSE) 中的 GPL-2.0 许可发布。

