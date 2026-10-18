# 验证记录

## 游戏卡片三按钮

日期：2026-10-17。

- 卡片操作依次为「启动助手」「启动游戏」「自动助手」，分别打开助手界面、游戏客户端和执行已配置日常任务。
- 两个独立启动按钮按各自目标校验；助手界面使用空参数。目标切换时保留原始配置，原目标的参数与工作目录仅用于对应目标。
- Release x64 构建通过；使用已有本地顺序反射执行器运行 54 项 xUnit 测试，失败 0。新增验证独立目标选择、启动参数隔离、保留原配置与普通启动不触发日常任务。报告：`artifacts/TestResults/offline-tests.json`。
- 已更新自包含发布目录与压缩包。实际发布程序六页面原生检查通过，绑定错误 0；已目视确认三款游戏卡片均按顺序显示三个按钮，文字完整、排列正常。报告与截图：`artifacts/smoke-three-actions`。本轮未启动真实助手或游戏。

## 任务助手默认安装位置

日期：2026-10-17。

- 未指定助手路径时，读取、导入、保存配置及运行校验会查找默认位置；手动路径优先。未找到时提示选择已安装的助手。
- Release x64 构建与自包含发布通过；通过已有本地顺序反射执行器运行 51 项 xUnit 测试，失败 0。覆盖三款助手的默认位置识别、March7th Launcher 补全、手动路径优先、多个位置的顺序及未安装情况。
- 本机默认目录已发现 `C:\Program Files\BetterGI\BetterGI.exe`。未执行真实助手或游戏任务。
- 测试报告：`artifacts/TestResults/offline-tests.json`。
- 发布程序六页面原生冒烟检查通过，绑定错误 0；默认 BetterGI 路径已显示并持久化。报告与截图：`artifacts/smoke-default-assistants`。

## 应用图标

日期：2026-10-17。

- 新增蓝紫手柄与绿色完成标记的原创图标，提供 SVG、512px 透明 PNG 与 9 档尺寸 ICO（16、20、24、32、40、48、64、128、256）。生成脚本为 `scripts/Generate-Icon.ps1`。
- 已接入 EXE 图标、原生窗口图标和导航栏品牌图标；发布目录携带运行时图标文件。
- Release 构建、自包含发布通过；ICO 所有尺寸均成功解码，已从实际 EXE 提取并目视确认内嵌图标。
- 原生界面六页面冒烟检查通过，绑定错误 0，配置与时间选择器回读通过。报告与截图位于 `artifacts/smoke-icon`。

## 本轮：配置精简与普通权限启动

日期：2026-10-17，最终发布包冒烟时间：18:51（北京时间）。

- Release x64 构建与自包含发布通过，已更新发布目录和压缩包。
- 46 项测试通过，失败 0。覆盖原有队列、进程、配置和证据测试，并增加同目录助手入口识别、新进程普通启动、手动模式免自动证据配置的回归检查。
- 本轮标准 VSTest 因受限环境的 testhost 通信超时而中止。随后使用本地顺序反射执行器，逐案例创建测试实例、执行原有 xUnit 断言、等待异步方法并释放实例，完整执行上述 46 项。测试临时文件放在工作区内。结果为 artifacts/TestResults/offline-tests.json；执行器源文件保留在 artifacts/offline-tests。
- 从实际发布目录启动原生窗口：六个页面渲染通过；参数、超时、重试与时间选择器编辑后保存并重新读取 JSON，通过；按助手与执行方式显示配置项，通过；绑定错误 0。
- 实际窗口进程 Elevated=false，应用清单使用 asInvoker。普通程序启动使用 CreateProcess，Hub 启动流程没有请求提升权限。
- 当前沙箱限制读取 HKCU 登录启动项，已验证该开关显示不可用时应用可继续运行。登录启动启用与关闭未改动用户真实设置，仍需在普通桌面环境验收。
- 视觉检查后，保存按钮固定放在配置页顶部工具区；高级选项默认收起。
- 未启动真实游戏或助手。本轮验证覆盖 Hub、测试专用进程与原生界面。

本轮结果：

- 发布程序：artifacts/publish/win-x64/GameDailyHub.App.exe
- 压缩包：artifacts/GameDailyHub-win-x64.zip
- 原生页面与报告：artifacts/smoke-settings-release/*.png、smoke.json
- 测试结果：artifacts/TestResults/offline-tests.json
- 配置决策：docs/SETTINGS.md

## 此前基线记录

以下为本轮修改前的验证记录；登录启动现已改为当前用户 HKCU Run 项。
验证记录

日期：2026-10-17。环境：Windows 11 25H2 x64（Build 26200）、Visual Studio Community 2026 18.11.0、MSBuild 18.11.0.47511、.NET SDK 10.0.402。

### 已执行

- NuGet 官方源还原，四个项目均生成依赖锁文件。
- Debug x64 解决方案构建。
- 43 项 xUnit 测试通过：编排、普通启动隔离、真实进程/子进程、Job Object、取消/超时/重试/跳过、人工确认、异常恢复、配置/备份、日志解析和每日计划。
- 进程适配器启动测试专用 EXE，捕获输出、验证开始/完成信号和退出后清理。
- 实际 WinUI 窗口的六页面渲染检查，绑定错误 0；高 DPI 截图检查后调整初始窗口尺寸和卡片布局。

### 最终发布验证

- Debug 与 Release x64 构建通过；最终 Release 构建日志无编译警告或错误。
- 最终测试：43 项通过，失败 0、跳过 0；包括人工确认期间的超时和全局跳过、伪造成功文本拦截。
- 自包含发布完成，并从实际发布目录运行原生窗口；六个页面均成功渲染，绑定错误 0。
- 直接通过真实 WinUI TextBox / NumberBox 修改普通参数、日常参数、超时和重试，保存后重新读取 JSON 验证成功，并恢复测试默认值。
- 发布目标显式携带应用 PRI/XBF 资源；已修复首次发布遗漏界面资源导致的启动解析错误。
- 页面和配置验证运行在独立数据目录，未开启用户每日计划或开机启动，未启动任何真实游戏。

最终发布冒烟时间：2026-10-17 16:56（北京时间）。结果文件及发布目录见下方。

### 测试报告位置

- 单元与进程测试：`artifacts/TestResults/tests.trx`。
- Debug 页面：`artifacts/smoke/*.png`、`smoke.json`。
- 发布页面：`artifacts/smoke-release/*.png`、`smoke.json`。
- Debug / Release 构建日志：`artifacts/build-debug.log`、`artifacts/build-release.log`。
- x64 发布目录：`artifacts/publish/win-x64`。

### 待人工验证

未配置真实游戏与助手安装路径，因此三款助手的真实游戏日常均未执行。当前结论只覆盖官方入口调查、保守解析逻辑、测试专用进程和本机原生界面。

| 项目 | 状态与操作 |
|---|---|
| BetterGI 0.66.0 | 待验证：配置真实名称，验证触发、日志 PID 关联、失败识别和人工确认。默认不自动宣称全部子任务成功。 |
| March7thAssistant v2026.10.16 | 待验证：首次初始化、配置前置条件、`main` 成功与失败、手动取消和工作流输出差异。 |
| OK-WW v3.7.4 | 待验证：安装器参数转交、第一项 Daily Task、真实日志目录、任务末尾信号及可选任务失败。 |
| 用户已有游戏进程 | 测试专用进程已验证保护行为；真实助手自身的关闭游戏行为需结合完成后设置检查。 |
| 开机启动注册/删除 | 功能已实现；未主动创建用户登录任务，需在设置页自愿启用后验证。 |
| Windows 10 / 纯净机器 | 待安装部署验证，当前实际运行检查为本机 Windows 11。 |

测试不使用真实游戏账号，生产界面没有注入测试数据。

## 2026-10-18 绝区零一条龙接入

- fmt、静态分析与 `git diff --check` 通过。
- Release x64 构建及发布完成，更新 `artifacts/publish/win-x64`。
- 106 项测试通过，失败 0、跳过 0。新增覆盖安装版权限前置检查、顶层与子任务结果区分、源码入口与环境变量、实例复用和配置持久化。
- 从实际发布目录以普通权限启动，独立数据目录验证六个页面、四款游戏、绝区零源码模式与路径保存；绑定错误 0。已检查总览与绝区零源码配置截图。
- 报告：`artifacts/TestResults/tests.trx`、`artifacts/build-zzz-release.log`、`artifacts/publish-zzz.log`、`artifacts/smoke-zzz-release/smoke.json`；截图位于同一冒烟目录。

尚未解决或验证的项目：

- 尚未使用真实绝区零与助手安装目录联调。官方依赖的实际下载与安装、两种安装版启动器、管理员权限下的日常运行及游戏内结果仍需验证；当前源码进程测试使用测试专用程序。

此前待人工验证的其他助手、登录启动与 Windows 10 / 纯净机器项目保持原有状态。

## 2026-10-18 管理员启动与进程重审

- fmt、C# 静态分析、PowerShell 语法检查与差异检查通过；Release 构建为 0 警告、0 错误。
- 116 项测试通过，失败 0、跳过 0。覆盖标准句柄隔离、子进程环境与 UTF-8 输出、现有进程保护、启动任务 XML 及嵌套空值配置合并。
- 登录任务定义已通过 Windows Task Scheduler 的内存解析校验；测试未注册任务，未修改用户登录启动设置。
- 界面测试使用独立的 `obj/ui-test`、`bin/ui-test`、`artifacts/ui-test/win-x64` 和数据目录。六个页面、四款游戏及配置保存验证通过，`Elevated=false`、绑定错误 0。
- 静态提取三个 EXE 的内嵌清单：交付版为 `requireAdministrator`，界面测试版及测试宿主为 `asInvoker`。读取程序集元数据确认交付版测试标志关闭、测试版开启。
- 验证冒烟脚本会在启动前拒绝管理员程序。测试期间未运行管理员交付程序，未触发 UAC。
- 已修复清单缓存混用、登录启动机制不适配管理员程序、额外句柄继承、普通启动环境污染、Python 输出编码不一致及配置合并可空性警告。
- 交付程序已更新到 `artifacts/publish/win-x64/GameDailyHub.App.exe`。

报告：`artifacts/build-admin-review.log`、`artifacts/TestResults/tests.trx`、`artifacts/smoke-admin-review/smoke.json`、`artifacts/permission-verification.json`。

未实测项目：交付版实际提权启动、管理员状态下真实助手与游戏联调、登录任务实际注册/删除及旧启动项迁移。既有 Windows 10 / 纯净机器验证继续保留。

## 2026-10-18 固定游戏目录与旧配置同步

- 游戏目录固定为原神、崩坏：星穹铁道、鸣潮和绝区零，名称与助手对应关系由内置目录确定。
- 读取、导入和保存配置时补齐缺少的游戏及其日常项，保留已有游戏 ID、路径、源码运行方式、日常参数、任务顺序与启用状态；重复条目沿用首条配置并统一任务引用。
- fmt、静态分析及差异检查通过。Release 构建与发布完成，0 警告、0 错误；118 项测试通过，失败 0、跳过 0。
- 使用隔离的旧配置运行普通权限界面测试：自动显示四款游戏，已目视确认绝区零卡片与游戏配置页；六个页面和配置保存检查通过，绑定错误 0、`Elevated=false`。
- 测试未触发 UAC；交付版保持 `requireAdministrator`，已更新 `artifacts/publish/win-x64/GameDailyHub.App.exe`。
- 报告：`artifacts/build-fixed-catalog.log`、`artifacts/TestResults/tests.trx`、`artifacts/smoke-fixed-catalog/smoke.json`；截图位于同一冒烟目录。

本轮没有剩余失败项；真实助手与游戏、登录任务及纯净机器的待验项目沿用前述记录。

## 2026-10-18 全部助手配置导入导出

- 绝区零配置导入导出包含 `config` 下的用户 YAML，覆盖多账号实例、应用组与自定义战斗配置；排除示例、合并缓存、项目与仓库配置、运行记录。
- 设置页新增全部助手配置导入导出，使用单个 JSON；未安装或无配置的助手跳过并列出。导入按类型匹配，保留本机路径；全部校验、暂存和备份后写入，写入失败时回滚已替换文件。
- fmt、C# 静态分析及差异检查通过；Release 编译、发布完成。134 项测试通过，失败 0、跳过 0，覆盖四助手恢复、缺失助手、无效配置、路径保留与文件占用回滚。首次回滚测试对 Windows 异常类型的断言已修正并复跑通过。
- 普通权限界面冒烟检查通过，绑定错误 0，完成四助手统一导出、导入及持久化检查；已目视检查设置页全部助手入口。测试数据位于 `artifacts/smoke-assistant-bundle`，未读取真实助手账号配置或触发 UAC。
- 交付目录：`artifacts/publish/win-x64`；测试结果：`artifacts/TestResults/tests.trx`；界面报告及截图：`artifacts/smoke-assistant-bundle/smoke.json`、`settings.png`。

本轮失败项均已处理。真实助手配置恢复后的实际运行、管理员交付版启动、登录任务和纯净 Windows 部署仍待本机验证。
