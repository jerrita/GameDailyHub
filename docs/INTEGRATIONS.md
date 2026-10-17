# 第三方集成调查

核查日期：2026-10-17。参数以官方文档与对应发布标签的源码为依据，路径由用户本地配置。首版采用命令行入口，界面自动点击不作为默认集成方式。

| 工具 | 官方稳定发布基线 | 入口和限制 |
|---|---|---|
| BetterGI | [0.66.0](https://github.com/babalae/better-genshin-impact/releases/tag/0.66.0) | 无任务参数打开界面；`--startOneDragon 名称` 执行一条龙。名称不存在时上游会回退，Hub 提前检查。 |
| March7thAssistant | [v2026.10.16](https://github.com/moesnow/March7thAssistant/releases/tag/v2026.10.16) | Launcher 无参数打开 GUI；Assistant 的 `main` 执行完整运行。支持工作流名称及查询命令。 |
| OK-WW | [v3.7.4](https://github.com/ok-oldking/ok-wuthering-waves/releases/tag/v3.7.4) | `-t` / `--task` 选择索引，`-e` / `--exit` 请求任务后退出。第一项需核对为 Daily Task。 |

## BetterGI

- [官方命令行文档](https://www.bettergi.com/feats/command/introduce.html)
- [参数解析](https://github.com/babalae/better-genshin-impact/blob/0.66.0/BetterGenshinImpact/Helpers/CommandLineOptions.cs)
- [一条龙实现](https://github.com/babalae/better-genshin-impact/blob/0.66.0/BetterGenshinImpact/ViewModel/Pages/OneDragonFlowViewModel.cs)
- [任务异常处理](https://github.com/babalae/better-genshin-impact/blob/0.66.0/BetterGenshinImpact/GameTask/TaskRunner.cs)
- [文件日志和实例标识](https://github.com/babalae/better-genshin-impact/blob/0.66.0/BetterGenshinImpact/App.xaml.cs)

一条龙结束事件会在部分异常处理分支后发出，部分任务也会捕获异常。Hub 内置解析器可以识别已知失败、取消和结束，但没有声称所有子任务都成功的充分接口。因此默认最终成功需要用户确认。文件日志通过会话/PID 实例标识关联，历史日志不会参与本次判定。

## March7thAssistant

- [官方 README](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/README.md)
- [GUI 入口及已有实例转发](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/app.py)
- [CLI 参数及退出路径](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/main.py)
- [完成阶段及循环](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/tasks/game/__init__.py)
- [配置示例](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/assets/config/config.example.yaml)
- [日志标题格式](https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/utils/logger/logger.py)

首版优先调用独立 CLI 进程以获得本轮标准输出和所属进程树。已有 Launcher 虽支持转发任务，但缺少 Hub 已验证的、完整关联终态及安全清理的协议，因此先提示关闭。任务完成需要最终停止阶段、完成标记与退出码共同验证。首次初始化错误可能仍退出 0，输出检查优先。

## OK-WW

- [官方 CLI 说明](https://github.com/ok-oldking/ok-wuthering-waves/blob/v3.7.4/README.md)
- [任务顺序、日志与启动条件](https://github.com/ok-oldking/ok-wuthering-waves/blob/v3.7.4/config.py)
- [DailyTask 及可选任务失败处理](https://github.com/ok-oldking/ok-wuthering-waves/blob/v3.7.4/src/task/DailyTask.py)
- [该发布锁定的框架版本](https://github.com/ok-oldking/ok-wuthering-waves/blob/v3.7.4/requirements.txt)
- [ok-script v2.0.8 任务执行与退出](https://github.com/ok-oldking/ok-script/blob/v2.0.8/ok/task/TaskExecutor.py)

该版本框架在任务执行返回后发送完成信号；异常与手动停止走不同分支，进程不一定退出。DailyTask 的最终日志位于其任务流程末尾；部分可选任务失败被内部捕获，Hub 对已知失败优先判失败。`-e` 对应的退出行为还包括关闭游戏，所以默认只配置 `-t 1`。

## 共同策略

源码模式使用 [March7th 官方 uv 工作流与 Python 入口](https://github.com/moesnow/March7thAssistant/blob/main/README_EN.md)、[OK-WW main.py](https://github.com/ok-oldking/ok-wuthering-waves/blob/master/main.py) 及其 `requirements.txt`。环境管理遵循 [uv 虚拟环境文档](https://docs.astral.sh/uv/pip/environments/)，包索引通过命令参数指定[清华镜像](https://mirrors.tuna.tsinghua.edu.cn/help/pypi/)，仅作用于该次安装。March7th 的 `uv sync` 同时覆盖项目中名为 `pypi` 的索引。启动使用 uv 管理的 `.venv/Scripts/python.exe`，使日常进程继续直接归属现有 Job Object。

普通启动与自动化的配置及方法独立。固定延时仅用于轮询和清理宽限期；完成必须来自约定信号或人工确认。未知版本、不同日志格式或安装器进程交接无法验证时允许人工模式，并保留原因。新增参数需先更新调查与测试。
