# 开发指南

[返回项目介绍](../README.md) · [使用指南](USAGE.md)

## 构建与测试

需要 .NET SDK 10.0.402、Visual Studio 的 WinUI 应用程序开发工作负载，以及 NuGet 网络连接。脚本通过 `vswhere` 查找现有 MSBuild，缓存保存在工作区。Windows SDK 构建工具通过固定 NuGet 依赖获得，无需因为传统 Windows Kits 目录缺失而重装 Visual Studio。

从工作区根目录运行 PowerShell：

```powershell
./scripts/Build.ps1 -Configuration Debug
./scripts/Test.ps1 -NoBuild
./scripts/Build.ps1 -Configuration Release
./scripts/Test.ps1 -Configuration Release -NoBuild
./scripts/Publish.ps1 -NoBuild
./scripts/Smoke.ps1
```

本地发布目录为 `artifacts/publish/win-x64`，运行其中的 `GameDailyHub.App.exe`，保留同目录依赖文件。

提交的 `packages.lock.json` 锁定依赖。还原默认使用锁定模式。修改依赖时，显式重新还原并核对锁文件。

GitHub Actions 在推送 `v1.2.3` 格式的版本标签时，使用 Windows runner 编译 Release x64，创建正式 GitHub Release，并上传包含完整发布目录的 `GameDailyHub-win-x64.zip`。发布包使用共享运行库，运行环境要求见 [下载与安装](../README.md#下载与安装)。工作流使用仓库自带的 `GITHUB_TOKEN`，无需配置额外密钥。

提交并推送待发布代码后，执行以下命令发布对应版本（替换为实际版本号）：

```powershell
git tag v1.2.3
git push origin v1.2.3
```

测试项目包含模拟适配器单元测试及测试专用进程程序，真实验证 Job Object、子进程、PID 创建时间、标准句柄继承、输出捕获和实例复用。该程序只用于测试，产品界面没有模拟成功数据。`Smoke.ps1` 构建 `asInvoker` 界面测试版，使用独立中间目录、输出目录和数据目录，启动前检查内嵌权限清单；管理员程序会在启动前被拦截。测试版仅接受冒烟入口，不读写登录启动设置，不触发 UAC。

验证记录见 [VERIFICATION.md](VERIFICATION.md)，涵盖六个页面、按需显示和配置保存。界面测试结果来自普通权限测试版；交付版通过静态提取内嵌清单验证 `requireAdministrator`，测试不启动交付版。发布构建显式包含 WinUI 项目 PRI/XBF 资源。

受限执行环境可能阻止 NuGet 网络、VSTest 宿主通信或进程枚举，需要在具备对应权限的 Windows 终端执行。详细验证记录见 [验证记录](VERIFICATION.md)。

源码结构：`src/GameDailyHub.Core`（模型、契约、编排），`src/GameDailyHub.Windows`（适配器、进程、配置、日志），`src/GameDailyHub.App`（WinUI / MVVM），`tests/GameDailyHub.Tests`（测试）。

应用图标位于 `src/GameDailyHub.App/Assets`，包含 SVG、512px PNG 和 16–256px 多尺寸 ICO，应用于 EXE、窗口与导航栏。修改 `scripts/Generate-Icon.ps1` 中的矢量图形后运行该脚本，即可同步生成全部图标资源，再重新构建发布。

## 已知验证边界

目前未提供助手与游戏的本机安装路径，因此真实游戏日常联调尚未执行。实际图像识别、账号登录、工具更新后的日志及游戏适配需在配置后验证。管理员交付版的实际启动、登录任务注册/删除与旧项迁移、纯净 Windows 10 部署需手动验收；测试期间未触发 UAC 或修改用户的登录启动设置。

官方资料及接入决策见 [第三方集成说明](INTEGRATIONS.md)。
