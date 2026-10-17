# GameDailyHub

原生 WinUI 3 二游启动与一条龙管理器。支持原神 / BetterGI、崩坏：星穹铁道 / March7thAssistant、鸣潮 / OK-WW，以及通过配置扩展的其他游戏。

## 运行

发布目录为 `artifacts/publish/win-x64`，压缩包为 `artifacts/GameDailyHub-win-x64.zip`。解压完整目录后运行 `GameDailyHub.App.exe`，以当前用户权限启动，无需 UAC。请保留同目录依赖文件。

目标系统为 Windows 10 22H2、Windows 11 x64。发布包包含 .NET 和 Windows App SDK 运行时。当前本机验证环境为 Windows 11 25H2；其他系统及纯净机器仍需兼容性验证。

首次启动打开「游戏」，游戏程序和助手程序路径留空时自动查找默认安装位置，也可手动指定。改动在离开页面、关闭应用或调整定时与外观时自动保存。BetterGI 另需填写一条龙配置名称；March7th 的工作流可留空运行默认日常。

- **启动助手**：打开该游戏的助手界面，使用空参数。March7th 使用 Launcher；相同程序已运行时尝试激活现有窗口。
- **启动游戏**：打开该游戏的客户端，使用独立的游戏启动参数。
- **自动助手**：执行该游戏已配置的日常任务，使用独立日常配置与全局串行队列。
- **开始全部日常**：按「任务队列」中启用任务的顺序执行。
- **停止任务**：取消当前批次，清理本次自动化创建的进程。
- **跳过当前任务**：停止当前项、完成清理后继续下一项。等待人工判定期间同样生效，也可勾选已停止后使用「确认跳过」。

所有日常入口共用串行引擎；忙碌时拒绝重复提交。其他游戏仍可通过卡片独立普通启动。

配置项的取舍见 [配置精简决策](docs/SETTINGS.md)。

## 配置三款助手

先在各助手内完成游戏路径、登录和任务内容配置，再配置 Hub。Hub 默认只读取第三方配置进行校验。

| 游戏 | 任务助手 | 助手图形启动器 | 日常官方入口 |
|---|---|---|---|
| 原神 | `BetterGI.exe` | 可留空 | `--startOneDragon "配置名称"` |
| 崩坏：星穹铁道 | `March7th Assistant.exe` | `March7th Launcher.exe` | `main`；命名工作流使用 `--workflow-name "名称"` |
| 鸣潮 | 本机 `ok-ww.exe` | 按实际安装情况选择 | `-t 1` |

助手路径留空时，依次检查 `%LocalAppData%/Programs`、`%ProgramFiles%`、`%ProgramFiles(x86)%`、`%LocalAppData%` 和 Hub 所在目录下的 `BetterGI/BetterGI.exe`、`March7thAssistant/March7th Assistant.exe`、`ok-ww/ok-ww.exe`。找到后显示并保存实际路径；手动填写的路径始终优先。March7th 为解压使用，可放在 Hub 同目录的 `March7thAssistant` 文件夹，或手动选择。未找到时提示选择已安装的助手；通用助手需手动指定路径。

常用设置只显示两个程序路径与适用的任务名称。自定义参数、工作目录、超时重试和日志路径归入「高级」；命令参数、日志正则和 March7th 专属配置仅在相应场景出现。选择官方入口时自动生成参数，工作目录和日志目录沿用助手默认值。March7th 同目录存在 Launcher 时自动补全界面入口，保留手动选择。通用助手默认手动执行并人工确认。游戏启动参数始终单独保存。

### 源码运行

March7thAssistant、OK-WW 可在「游戏」选择安装版或源码运行；BetterGI 使用安装版。源码位置可手动填写，留空分别使用 `%LocalAppData%/GameDailyHub/assistants/March7thAssistant`、`%LocalAppData%/GameDailyHub/assistants/ok-wuthering-waves`，跟随 Hub 的程序数据目录。

- **克隆 / 更新代码**：从官方 Git 仓库使用 `--depth 1` 浅克隆；已有仓库使用 `git pull --ff-only` 更新，并初始化子模块。非空且未包含 Git 仓库的目录会提示重新选择。
- **安装环境**：使用已安装的 uv 创建源码目录内的 `.venv`，按 Python 3.12 安装依赖。March7th 优先使用 `uv sync`，其余使用 `uv pip install -r requirements.txt`，包索引使用[清华源](https://mirrors.tuna.tsinghua.edu.cn/help/pypi/)。

操作进度写入运行日志，可取消当前源码操作。代码和环境准备完成后，使用卡片上的「助手」按钮打开界面。

uv、Git 须已加入 PATH。源码模式的「启动助手」使用虚拟环境运行 March7th 的 `app.py` 或 OK-WW 的 `main.py`；「自动助手」运行 `main.py` 并沿用日常参数、配置校验和结果判定。工作目录固定为源码目录；助手运行期间须先关闭再更新代码或环境。

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

## 完成、安全与人工确认

成功代表已配置流程取得约定的助手完成证据；游戏账号的服务端奖励状态仍以游戏为准。缺失、截断或无法可靠关联的证据进入未知状态，队列等待人工确认。手动执行模式打开助手后，由用户手动执行并确认结果，记录中保留人工确认来源。

助手已有实例时，首版会提示关闭后重试，以确保能够追踪本轮任务和安全停止。普通启动保留实例复用能力。

自动化进程以挂起方式创建，绑定独立 Windows Job Object 后恢复运行。每个日常终态均清理本次创建的助手及后代进程，然后推进队列：先尝试关闭本次根进程窗口，超出宽限期后只清理该 Job。普通启动、原先运行的游戏和其他会话不属于该 Job。

第三方助手自己执行的关机、关游戏、循环、计划和自动更新行为需在助手内关闭或核对。Hub 的进程隔离负责自身的清理操作。提升权限、更新器或外部服务造成的任务交接如果无法确认归属，需人工处理。

默认每项尝试超时 60 分钟、重试 0 次、明确失败后继续，可在更多选项中调整。超时、失败和跳过均先完成清理；清理无法确认时保留队列锁等待确认。应用被异常结束时，启动中的历史记录会在下次运行标为未知。

## 数据、日志与定时

- 配置：`%LocalAppData%/GameDailyHub/config.json`，当前结构版本 1。
- 备份：`config.json.bak`；损坏文件以 `.damaged-时间戳` 保留。
- 历史：`history/runs` 和 `history/batches`，包含时间、耗时、尝试、退出码、错误与证据。
- 应用日志：默认 `logs/hub-日期.jsonl`；可设置新目录，重启生效。
- 「日志」可按游戏查看历史，实时日志显示当前会话，完整应用日志可通过「设置」中的「打开日志文件夹」查看。
- 导入先校验再原子替换，导入后的定时运行保持关闭。运行中禁止导入；已开始的批次使用配置快照。
- 「定时」默认关闭，默认时区 `China Standard Time`（北京时间）。忙碌时记录冲突并跳过，启动和休眠后不补跑错过的计划。程序需要保持运行。
- 登录启动默认关闭，设置页开关即时生效到当前用户 HKCU Run 项，无需管理员权限。只负责打开 Hub，日常由应用计划控制。移动程序目录后重新切换此开关。

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

测试项目包含模拟适配器单元测试及测试专用进程程序，真实验证 Job Object、子进程、PID 创建时间、输出捕获和实例复用。该程序只用于测试，产品界面没有模拟成功数据。`Smoke.ps1` 使用独立数据目录启动真实 WinUI 窗口、切换六个页面并输出 PNG 和绑定错误报告。

本次验证结果见 docs/VERIFICATION.md，涵盖六个页面、按需显示、配置保存与普通权限启动。发布构建显式包含 WinUI 项目 PRI/XBF 资源。

受限执行环境可能阻止 NuGet 网络、VSTest 宿主通信或进程枚举，需要在具备对应权限的 Windows 终端执行。详细验证记录见 [docs/VERIFICATION.md](docs/VERIFICATION.md)。

## 扩展

核心接口为 `IToolAdapter`，Windows 内置 BetterGI、March7th、OK-WW、Generic 四种实现。新增游戏可直接选择通用适配器，配置命令加日志规则或人工判定。专用工具可新增适配器并注册到 `AdapterRegistry`，复用现有界面、持久化和队列引擎。

源码结构：`src/GameDailyHub.Core`（模型、契约、编排），`src/GameDailyHub.Windows`（适配器、进程、配置、日志），`src/GameDailyHub.App`（WinUI / MVVM），`tests/GameDailyHub.Tests`（测试）。

应用图标位于 `src/GameDailyHub.App/Assets`，包含 SVG、512px PNG 和 16–256px 多尺寸 ICO，应用于 EXE、窗口与导航栏。修改 `scripts/Generate-Icon.ps1` 中的矢量图形后运行该脚本，即可同步生成全部图标资源，再重新构建发布。

## 已知验证边界

目前未提供三款助手与游戏的本机安装路径，因此真实游戏日常联调尚未执行。实际图像识别、账号登录、工具更新后的日志及游戏适配需在配置后验证。当前用户登录启动的启用/关闭以及纯净 Windows 10 部署需要额外手动验收；开发过程中未主动开启用户的开机启动。

官方资料及接入决策见 [docs/INTEGRATIONS.md](docs/INTEGRATIONS.md)。
