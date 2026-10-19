# GameDailyHub

Windows 上的多游戏日常助手管理器。把游戏启动、助手启动和日常任务集中到一个窗口，按顺序完成多款游戏的日常。

**[下载最新版](https://github.com/jerrita/GameDailyHub/releases/latest)** · [使用指南](docs/USAGE.md) · [问题反馈](https://github.com/jerrita/GameDailyHub/issues)

## 功能

- **统一启动**：在游戏卡片上打开游戏、打开助手，或执行自动日常。
- **批量日常**：选择要运行的游戏并调整顺序，一键开始；运行中可停止或跳过当前任务。
- **定时执行**：每天在指定时间运行任务队列，也可设置登录 Windows 后启动 Hub。
- **配置与记录**：导入、导出 Hub 和助手配置，查看各游戏的执行历史与日志。

日常任务由下列助手执行，任务内容在各助手中配置。

## 支持的游戏

| 游戏 | 日常助手 | 使用方式 |
|---|---|---|
| 原神 | [BetterGI](https://github.com/babalae/better-genshin-impact) | 安装版 |
| 崩坏：星穹铁道 | [March7thAssistant](https://github.com/moesnow/March7thAssistant) | 安装版 / 源码运行 |
| 鸣潮 | [OK-WW](https://github.com/ok-oldking/ok-wuthering-waves) | 安装版 / 源码运行 |
| 绝区零 | [ZenlessZoneZero-OneDragon](https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon) | 安装版 / 源码运行 |
| 明日方舟：终末地 | [MaaEnd](https://github.com/MaaEnd/MaaEnd) | 安装版，支持在 Hub 内一键安装 |

## 下载与安装

适用于 **Windows 10 22H2 / Windows 11，x64**。

1. 前往 [Releases](https://github.com/jerrita/GameDailyHub/releases/latest)，下载 `GameDailyHub-win-x64.zip`。
2. 将整个 ZIP 解压到准备长期使用的文件夹，保留文件夹内的全部文件。
3. 运行 `GameDailyHub.App.exe`，按 Windows 提示授予管理员权限。

若环境缺失请安装：

- [.NET 10 Runtime（x64）](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)
- [Windows App Runtime 2.5.1 或兼容更新版本（x64）](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

使用源码运行时，还需安装 [Git（Windows x64）](https://git-scm.com/install/windows)，加入 PATH 后重启 Hub。uv 已随包提供。

## 快速开始

1. **准备助手**：从上表对应项目下载助手；MaaEnd 也可在「游戏 → 明日方舟：终末地」点击「一键安装」。先在助手中配置游戏路径、登录与日常任务，并单独运行一次确认可用。
2. **填写路径**：打开 Hub 的「游戏」页面，选择游戏，填写游戏程序和助手程序路径。留空时会尝试自动查找，找不到时手动选择。BetterGI 还需填写一条龙配置名称；March7th 工作流留空可执行默认日常。
3. **启动使用**：在「总览」中点击「启动游戏」「启动助手」或「自动助手」，执行对应操作。
4. **批量执行**：在「任务队列」启用需要运行的游戏并调整顺序，点击「开始全部日常」。需要每天自动执行时，再到「定时」设置时间并开启计划。

设置会自动保存。各助手的配置方法、源码运行步骤与导入导出说明见[使用指南](docs/USAGE.md)。

## 使用注意事项

- 自动日常前请关闭已打开的助手，并核对助手自身的定时、循环、任务后关闭游戏或关机等设置，避免重复执行或中断后续任务。
- 出现「待确认」时，请检查助手界面和游戏内结果，再选择成功、失败或跳过；确认前队列会等待。
- 定时运行需要保持 Hub 开启。错过的计划不会在下次启动或唤醒后补跑。
- 游戏分辨率、画面设置和登录状态请按对应助手的要求配置，游戏或助手更新后先检查能否正常执行。
- 助手配置导出文件可能包含账号、密码和通知密钥，请妥善保管。

## 文档与反馈

- [使用指南](docs/USAGE.md)：助手配置、源码运行、任务控制、数据备份与定时设置。
- [开发指南](docs/DEVELOPMENT.md)：本地构建、测试、发布与项目结构。
- [第三方集成说明](docs/INTEGRATIONS.md)：助手接入依据、命令参数与兼容性细节。

遇到问题可提交 [Issue](https://github.com/jerrita/GameDailyHub/issues)，请附上 Hub 版本、助手版本、复现步骤和相关日志，并移除其中的个人信息。
