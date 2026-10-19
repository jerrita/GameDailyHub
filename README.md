# GameDailyHub

原生 WinUI 3 二游启动与一条龙管理器。固定支持原神 / BetterGI、崩坏：星穹铁道 / March7thAssistant、鸣潮 / OK-WW、绝区零 / ZenlessZoneZero-OneDragon、明日方舟：终末地 / MaaEnd。

## 运行

发布目录为 `artifacts/publish/win-x64`。运行 `GameDailyHub.App.exe` 时由 Windows 请求管理员权限，助手和游戏继承 Hub 的权限。请保留同目录依赖文件。

目标系统为 Windows 10 22H2、Windows 11 x64。发布包使用共享运行库，若环境缺失请安装 [.NET 10 Runtime（x64）](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0) 和 [Windows App Runtime 2.5.1 或兼容更新版本（x64）](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)，无需安装开发用 SDK。当前本机验证环境为 Windows 11 25H2；其他系统及纯净机器仍需兼容性验证。

首次启动打开「游戏」，游戏程序和助手程序路径留空时自动查找默认安装位置，也可手动指定。改动在离开页面、关闭应用或调整定时与外观时自动保存。BetterGI 另需填写一条龙配置名称；March7th 的工作流可留空运行默认日常。

- **启动助手**：打开该游戏的助手界面，使用空参数。March7th 使用 Launcher；相同程序已运行时尝试激活现有窗口。
- **启动游戏**：打开该游戏的客户端，使用独立的游戏启动参数。
- **自动助手**：执行该游戏已配置的日常任务，使用独立日常配置与全局串行队列。
- **开始全部日常**：按「任务队列」中启用任务的顺序执行。
- **停止任务**：取消当前批次，清理本次自动化创建的进程。
- **跳过当前任务**：停止当前项、完成清理后继续下一项。等待人工判定期间同样生效，也可勾选已停止后使用「确认跳过」。

所有日常入口共用串行引擎；忙碌时拒绝重复提交。其他游戏卡片仍可独立普通启动。

配置项的取舍见 [配置精简决策](docs/SETTINGS.md)。

## 配置助手

先在各助手内完成游戏路径、登录和任务内容配置，再配置 Hub。Hub 默认只读取第三方配置进行校验。

| 游戏 | 任务助手 | 助手图形启动器 | 日常官方入口 |
|---|---|---|---|
| 原神 | `BetterGI.exe` | 可留空 | `--startOneDragon "配置名称"` |
| 崩坏：星穹铁道 | `March7th Assistant.exe` | `March7th Launcher.exe` | `main`；命名工作流使用 `--workflow-name "名称"` |
| 鸣潮 | 本机 `ok-ww.exe` | 按实际安装情况选择 | `-t 1` |
| 绝区零 | `OneDragon-RuntimeLauncher.exe` 或 `OneDragon-Launcher.exe` | 可留空 | 安装版 `--onedragon`；源码调用官方应用入口 |
| 明日方舟：终末地 | `MaaEnd.exe`（MXU 版） | 可留空 | `--autostart --quit-after-run`；指定实例附加 `--instance "名称"` |

助手路径留空时，依次检查 `%LocalAppData%/Programs`、`%ProgramFiles%`、`%ProgramFiles(x86)%`、`%LocalAppData%` 和 Hub 所在目录下的 `BetterGI/BetterGI.exe`、`March7thAssistant/March7th Assistant.exe`、`ok-ww/ok-ww.exe`、`ZenlessZoneZero-OneDragon/OneDragon-RuntimeLauncher.exe` 或 `OneDragon-Launcher.exe`。找到后显示并保存实际路径；手动填写的路径始终优先。March7th 为解压使用，可放在 Hub 同目录的 `March7thAssistant` 文件夹，或手动选择。未找到时提示选择已安装的助手。

常用设置只显示两个程序路径与适用的任务名称。自定义参数、工作目录、超时重试和日志路径归入「高级」；命令参数、日志正则和 March7th 专属配置仅在相应场景出现。选择官方入口时自动生成参数，工作目录和日志目录沿用助手默认值。March7th 同目录存在 Launcher 时自动补全界面入口，保留手动选择。游戏启动参数始终单独保存。

### 源码运行

March7thAssistant、OK-WW、绝区零一条龙可在「游戏」选择安装版或源码运行；BetterGI 使用安装版。源码位置可手动填写，留空分别使用 `%LocalAppData%/GameDailyHub/assistants/March7thAssistant`、`%LocalAppData%/GameDailyHub/assistants/ok-wuthering-waves`、`%LocalAppData%/GameDailyHub/assistants/ZenlessZoneZero-OneDragon`，跟随 Hub 的程序数据目录。

- **克隆 / 更新代码**：从官方 Git 仓库使用 `--depth 1` 浅克隆（含子模块）；已有仓库使用 `git pull --ff-only` 更新，并初始化子模块。非空且未包含 Git 仓库的目录会提示重新选择。
- **安装环境**：安装前自动检查并启用 Windows 长路径支持，配置失败时停止安装并显示原因。使用 uv 创建源码目录内的 `.venv`。March7th 与 OK-WW 使用 Python 3.12，绝区零一条龙使用 Python 3.11。March7th 与绝区零一条龙优先使用 `uv sync`，其余使用 `uv pip install -r requirements.txt`，包索引使用[清华源](https://mirrors.tuna.tsinghua.edu.cn/help/pypi/)。

操作进度写入运行日志，可取消当前源码操作。代码和环境准备完成后，使用卡片上的「助手」按钮打开界面。

发布包的 `tools` 目录包含 Windows x64 版 uv 0.13.1 与 Git 2.56.0.2（MinGit）。Hub 优先使用随包工具，并将工具目录加入助手子进程的 PATH；随包工具缺失时查找系统 PATH。源码模式的「启动助手」使用虚拟环境运行 March7th 的 `app.py` 或 OK-WW 的 `main.py`；这两款助手的「自动助手」运行 `main.py` 并沿用日常参数、配置校验和结果判定。绝区零使用下文所列官方入口。工作目录固定为源码目录；助手运行期间须先关闭再更新代码或环境。

### BetterGI

基线为 0.66.0。填写准确的一条龙配置名称。适配器检查 `User/OneDragon/<名称>.json` 及内部 `Name`，避免 BetterGI 找不到名称时回退到界面当前配置。

默认监听程序输出和 `log/better-genshin-impact*.log`。文件日志按本次 PID 的实例标识隔离。路径有差异时填写实际日志路径。

**默认需要人工确认最终结果。** 官方源码在部分子任务异常后仍可能发送流程结束通知，Hub 将其显示为待确认，保留失败证据；用户核对后选择成功、失败或跳过。此时任务仍占用队列。自定义日志规则仅适用于用户已经核实能够证明整个目标流程完成的入口，且需要进程正常退出。

### March7thAssistant

基线为 v2026.10.16，始终使用 March7thAssistant。

普通启动选择游戏或 `March7th Launcher.exe`。`March7th Assistant.exe` 默认运行任务，即便误填为游戏客户端路径，普通启动也会拦截。

日常任务选择 `March7th Assistant.exe`，配置名称留空执行 `main`。需要命名工作流时填写真实名称；可在助手内或官方 `--list-workflows` 入口检查。命名工作流输出未满足内置完整运行判据时，Hub 会要求人工确认。

助手 `config.yaml` 应配置：

```yaml
after_finish: None
pause_after_success: false
exit_after_failure: true
scheduled_run_enable: false
scheduled_tasks: []
```

也可保留全部处于禁用状态的 `scheduled_tasks`。先通过 Launcher 完成首次初始化。Hub 不修改这些设置。

完整运行成功须取得「开始运行」及「停止运行」阶段之后的最终「完成」，并观察任务进程正常退出；中间步骤的「完成」不能结束队列。错误输出和首次初始化失败优先判为失败，单独的退出码 0 视为证据不足。

### OK-WW

基线为 v3.7.4。官方配置的第一项为 Daily Task，默认调用 `-t 1`。请核对本机版本任务列表及安装启动器是否正确传递参数。

默认日志为 `logs/ok-ww.log`；安装器/运行目录不同请填写真实路径。检测本轮 `Daily Task Completed`，并检查任务异常及可选任务失败记录。助手退出本身不会被判为成功。

自定义 `-t 1 -e` 可要求任务结束后退出，**该参数在框架中还会关闭游戏**。默认参数不携带 `-e`。

### 绝区零一条龙

接入基线为 2026-10-18 核查的官方 main 提交 `bdafa6e3`。启动时将已有配置与支持列表同步，自动补齐绝区零卡片和日常队列项，保留已支持游戏的路径、日常设置及任务顺序。游戏名称与助手固定对应。先在助手内配置游戏路径、账号、模型与默认应用组。

安装版选择 `OneDragon-RuntimeLauncher.exe` 或 `OneDragon-Launcher.exe`，留空时查找默认安装根目录下的 `ZenlessZoneZero-OneDragon` 文件夹。普通启动使用空参数，自动日常使用 `--onedragon`。安装版和源码模式均继承 Hub 的管理员权限，任务进程继续由 Hub 管理。

源码界面入口为 `src/zzz_od/gui/app.py`，日常入口为 `src/zzz_od/application/zzz_application_launcher.py`，默认日常参数为空，使用助手已启用的实例和应用组。Hub 设置 `PYTHONPATH=<源码目录>/src`，使用 `.venv/Scripts/python.exe` 直接运行。需要限定账号实例时，可在自定义参数填写 `--instance 1,2`；安装版需同时保留 `--onedragon`。

内置判定读取本轮进程输出：子任务失败、异常退出优先判失败；顶层一条龙报告「全部结束」且进程正常退出后，等待人工确认。助手的应用组会继续处理失败的子任务，因此最终流程结束仍需核对。缺少结束证据时同样等待人工确认。Hub 默认不传关闭游戏或电源操作参数。助手配置导入导出包含 Hub 日常设置及 `config` 下的用户 YAML 配置，支持账号实例、应用组和自定义战斗配置；示例、合并缓存、项目与仓库配置及运行记录不包含。

### MaaEnd

使用官方 Windows MXU 版，选择完整安装目录中的 `MaaEnd.exe`，也支持该目录内的 `mxu.exe`。助手路径留空时按默认安装根目录查找 `MaaEnd/MaaEnd.exe` 或 `MaaEnd/mxu.exe`。游戏程序手动选择本机客户端。本次接入使用安装版。

在「游戏 → 明日方舟：终末地」点击「一键安装」，Hub 查询官方最新正式版，下载 Windows x64 ZIP，验证大小与 SHA-256，解压到 `%LocalAppData%/GameDailyHub/assistants/MaaEnd`，完成后自动填写助手程序路径。安装显示下载进度，支持取消；下载或校验失败时清理临时目录，完整包校验后才放入安装位置。已有完整安装直接使用，已有其它文件时停止并提示，保留用户配置；版本升级沿用 MaaEnd 自身更新功能。安装后打开助手完成首次配置。

先在 MaaEnd 内配置连接、任务和实例并保存，关闭“手动启动时也自动执行”及所有启用的定时策略。Hub 只读取校验。普通启动使用空参数；自动日常使用 `--autostart --quit-after-run`，实例名称留空时使用 MaaEnd 的默认自动执行实例，填写后附加 `--instance "名称"`。实例名称缺失或重复会拦截启动，避免 MXU 回退执行其它实例。自定义命令同样须包含自动运行与完成后退出参数。

默认跟踪本次启动后 `debug/日期-序号.log` 新增的界面日志。MXU 在运行状态结束后可退出 0，失败任务也可能经过这一分支，因此流程结束和正常退出后仍须核对实例内各任务并人工确认；缺失日志同样进入人工确认。停止、超时和跳过沿用队列的进程清理。配置导入导出包含 Hub 日常设置和 `config/mxu-MaaEnd.json`；恢复实例与任务时按 ID 保留本机绝对路径。更新器交接和真实游戏执行尚需本机联调。

## 完成、安全与人工确认

成功代表已配置流程取得约定的助手完成证据；游戏账号的服务端奖励状态仍以游戏为准。缺失、截断或无法可靠关联的证据进入未知状态，队列等待人工确认。手动执行模式打开助手后，由用户手动执行并确认结果，记录中保留人工确认来源。

助手已有实例时，首版会提示关闭后重试，以确保能够追踪本轮任务和安全停止。普通启动保留实例复用能力。

自动化进程以挂起方式创建，绑定独立 Windows Job Object 后恢复运行。每个日常终态均清理本次创建的助手及后代进程，然后推进队列：先尝试关闭本次根进程窗口，超出宽限期后只清理该 Job。普通启动、原先运行的游戏和其他会话不属于该 Job。

第三方助手自己执行的关机、关游戏、循环、计划和自动更新行为需在助手内关闭或核对。Hub 的进程隔离负责自身的清理操作。更新器或外部服务造成的任务交接如果无法确认归属，需人工处理。

默认每项尝试超时 60 分钟、重试 0 次、明确失败后继续，可在更多选项中调整。超时、失败和跳过均先完成清理；清理无法确认时保留队列锁等待确认。应用被异常结束时，启动中的历史记录会在下次运行标为未知。

## 数据、日志与定时

- 配置：`%LocalAppData%/GameDailyHub/config.json`，当前结构版本 1。
- 备份：`config.json.bak`；损坏文件以 `.damaged-时间戳` 保留。
- 「设置 → 数据 → 助手配置」提供「导入」「导出」，操作时可多选或全选助手，将所选助手配置与 Hub 日常设置保存到一个 JSON。导入从文件内选择助手，仅恢复勾选项，兼容已有单助手文件。导出时未安装或没有配置的助手跳过并列出；全部缺失时不生成文件。导入前需安装所选助手并关闭程序，导入时保留本机路径并备份原文件；所选配置先校验，替换失败时回滚已写入文件。导出文件可能含账号、密码和通知密钥，请妥善保管。
- 历史：`history/runs` 和 `history/batches`，包含时间、耗时、尝试、退出码、错误与证据。
- 应用日志：默认 `logs/hub-日期.jsonl`；可设置新目录，重启生效。
- 「日志」可按游戏查看历史，实时日志显示当前会话，完整应用日志可通过「设置」中的「打开日志文件夹」查看。
- 导入先校验再原子替换，导入后的定时运行保持关闭。运行中禁止导入；已开始的批次使用配置快照。
- 「定时」默认关闭，默认时区 `China Standard Time`（北京时间）。忙碌时记录冲突并跳过，启动和休眠后不补跑错过的计划。程序需要保持运行。
- 登录启动默认关闭，设置页开关创建或删除当前用户的最高权限登录计划任务，只负责打开 Hub，日常由应用计划控制。已有启用的注册表启动项在正常启动时迁移；迁移成功后清理旧项。移动程序目录后重新切换此开关。

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

提交的 `packages.lock.json` 锁定依赖。还原默认使用锁定模式。修改依赖时，显式重新还原并核对锁文件。

GitHub Actions 在推送 `v1.2.3` 格式的版本标签时，使用 Windows runner 编译 Release x64，创建正式 GitHub Release，并上传包含完整发布目录的 `GameDailyHub-win-x64.zip`。发布沿用上述共享运行库要求。工作流使用仓库自带的 `GITHUB_TOKEN`，无需配置额外密钥。

提交并推送待发布代码后，执行以下命令发布对应版本（替换为实际版本号）：

```powershell
git tag v1.2.3
git push origin v1.2.3
```

测试项目包含模拟适配器单元测试及测试专用进程程序，真实验证 Job Object、子进程、PID 创建时间、标准句柄继承、输出捕获和实例复用。该程序只用于测试，产品界面没有模拟成功数据。`Smoke.ps1` 构建 `asInvoker` 界面测试版，使用独立中间目录、输出目录和数据目录，启动前检查内嵌权限清单；管理员程序会在启动前被拦截。测试版仅接受冒烟入口，不读写登录启动设置，不触发 UAC。

本次验证结果见 docs/VERIFICATION.md，涵盖六个页面、按需显示和配置保存。界面测试结果来自普通权限测试版；交付版通过静态提取内嵌清单验证 `requireAdministrator`，测试不启动交付版。发布构建显式包含 WinUI 项目 PRI/XBF 资源。

受限执行环境可能阻止 NuGet 网络、VSTest 宿主通信或进程枚举，需要在具备对应权限的 Windows 终端执行。详细验证记录见 [docs/VERIFICATION.md](docs/VERIFICATION.md)。

源码结构：`src/GameDailyHub.Core`（模型、契约、编排），`src/GameDailyHub.Windows`（适配器、进程、配置、日志），`src/GameDailyHub.App`（WinUI / MVVM），`tests/GameDailyHub.Tests`（测试）。

应用图标位于 `src/GameDailyHub.App/Assets`，包含 SVG、512px PNG 和 16–256px 多尺寸 ICO，应用于 EXE、窗口与导航栏。修改 `scripts/Generate-Icon.ps1` 中的矢量图形后运行该脚本，即可同步生成全部图标资源，再重新构建发布。

## 已知验证边界

目前未提供助手与游戏的本机安装路径，因此真实游戏日常联调尚未执行。实际图像识别、账号登录、工具更新后的日志及游戏适配需在配置后验证。管理员交付版的实际启动、登录任务注册/删除与旧项迁移、纯净 Windows 10 部署需手动验收；测试期间未触发 UAC 或修改用户的登录启动设置。

官方资料及接入决策见 [docs/INTEGRATIONS.md](docs/INTEGRATIONS.md)。
