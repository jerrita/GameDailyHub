using System.Text.RegularExpressions;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Services;
using YamlDotNet.Serialization;

namespace GameDailyHub.Windows.Adapters;

public sealed class AdapterRegistry : IAdapterRegistry
{
    private readonly Dictionary<string, IToolAdapter> _adapters;
    public AdapterRegistry(ProcessService processes, SourceAssistantService? sources = null)
    {
        IToolAdapter[] adapters = [new BetterGiAdapter(processes), new March7thAdapter(processes, sources), new OkWwAdapter(processes, sources), new ZzzAdapter(processes, sources), new GenericAdapter(processes)];
        _adapters = adapters.ToDictionary(a => a.Capabilities.Id);
    }
    public IToolAdapter Get(string id) => _adapters.TryGetValue(id, out var adapter) ? adapter : throw new InvalidOperationException("未知适配器：" + id);
}

public sealed class ProcessAdapterRun(Guid id, GameConfig game, OwnedProcessSession session, LogTail? tail, Action<string> log) : AdapterRun(id, game)
{
    private readonly CancellationTokenSource _followCancellation = new();
    private Task? _following;
    public OwnedProcessSession Session { get; } = session;
    public LogTail? Tail { get; } = tail;
    public Action<string> Log { get; } = log;
    public RunState Status { get; set; } = RunState.Running;
    public void FollowRemainingLogs()
    {
        _following ??= Task.Run(async () =>
        {
            try
            {
                while (!_followCancellation.IsCancellationRequested)
                {
                    while (Session.Lines.Reader.TryRead(out var line)) Log(line);
                    if (Tail is not null) foreach (var line in await Tail.ReadAsync(_followCancellation.Token)) Log(line);
                    await Task.Delay(200, _followCancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log("后续日志读取失败：" + e.Message); }
        });
    }
    public override async ValueTask DisposeAsync()
    {
        _followCancellation.Cancel();
        if (_following is not null) await _following;
        Tail?.Dispose(); await Session.DisposeAsync(); _followCancellation.Dispose();
    }
}

public abstract class ProcessToolAdapter(ProcessService processes, SourceAssistantService? sources = null) : IToolAdapter
{
    protected ProcessService Processes { get; } = processes;
    protected SourceAssistantService Sources { get; } = sources ?? new(processes, ConfigurationStore.DefaultRoot);
    public abstract AdapterCapabilities Capabilities { get; }
    protected abstract string VerifiedArguments(GameConfig game);
    protected virtual void ValidateTool(GameConfig game, bool daily, List<string> errors) { }
    public Task<ValidationResult> ValidateAsync(GameConfig game, bool daily, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationSuggestions.Apply(game);
        var errors = new List<string>();
        var source = game.ToolRunMode == AssistantRunMode.Source && (daily || game.Normal.Target == LaunchTarget.Assistant);
        void CheckFile(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) && name == "助手程序" && Capabilities.Id != "generic") errors.Add("未找到助手，请在「游戏」中选择助手程序");
            else if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) errors.Add(name + "不存在，请重新选择");
            else if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) errors.Add(name + "需为 .exe 文件");
        }
        if (!daily)
        {
            if (game.Normal.Target == LaunchTarget.Game && string.IsNullOrWhiteSpace(game.GamePath) && Capabilities.Id != "generic")
                errors.Add("未找到游戏，请在「游戏」中选择游戏程序");
            else if (source) errors.AddRange(Sources.Validate(game, true));
            else CheckFile(game.NormalPath, "普通启动目标");
            var name = Path.GetFileName(game.NormalPath);
            var isAssistant = game.Normal.Target == LaunchTarget.Assistant
                || new[] { game.ToolPath, game.ToolGuiPath }.Any(p => !string.IsNullOrWhiteSpace(p) && string.Equals(ProcessService.Normalize(p), ProcessService.Normalize(game.NormalPath), StringComparison.OrdinalIgnoreCase))
                || new[] { "BetterGI.exe", "March7th Assistant.exe", "March7th Launcher.exe", "ok-ww.exe", "OneDragon-Launcher.exe", "OneDragon-RuntimeLauncher.exe" }.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (!source && name.Equals("March7th Assistant.exe", StringComparison.OrdinalIgnoreCase)) errors.Add("March7th Assistant.exe 默认执行任务，普通启动必须选择 March7th Launcher.exe");
            if (isAssistant)
            {
                if (!string.IsNullOrWhiteSpace(game.Normal.Arguments)) errors.Add("助手普通启动仅允许空参数，以确保不触发任务");
            }
        }
        else
        {
            if (source) errors.AddRange(Sources.Validate(game, game.Daily.Trigger == TriggerMode.Manual));
            else CheckFile(game.Daily.Trigger == TriggerMode.Manual && !string.IsNullOrWhiteSpace(game.ToolGuiPath) ? game.ToolGuiPath : game.ToolPath, "助手程序");
            if (!double.IsFinite(game.Daily.TimeoutMinutes) || game.Daily.TimeoutMinutes <= 0 || game.Daily.TimeoutMinutes > 1440 || game.Daily.RetryCount is < 0 or > 10) errors.Add("超时或重试配置无效");
            if (game.Daily.Trigger != TriggerMode.Manual && game.Daily.Completion == CompletionMode.LogRules)
            {
                if (new[] { game.Daily.StartPattern, game.Daily.SuccessPattern, game.Daily.FailurePattern }.Any(string.IsNullOrWhiteSpace)) errors.Add("日志规则需要开始、成功和失败三种正则表达式");
                foreach (var pattern in new[] { game.Daily.StartPattern, game.Daily.SuccessPattern, game.Daily.FailurePattern })
                    try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); } catch (ArgumentException) { errors.Add("日志正则表达式格式无效"); }
            }
            if (Capabilities.Id == "generic" && game.Daily.Trigger != TriggerMode.Manual && game.Daily.Completion == CompletionMode.AdapterEvidence) errors.Add("通用适配器需要配置日志规则或人工判定");
            if (Capabilities.Id == "generic" && game.Daily.Trigger == TriggerMode.VerifiedCommand) errors.Add("通用适配器请选择自定义命令或半自动方式");
        }
        var working = daily ? game.Daily.WorkingDirectory : game.Normal.WorkingDirectory;
        if (!source && !string.IsNullOrWhiteSpace(working) && !Directory.Exists(working)) errors.Add("工作目录不存在");
        try { ValidateTool(game, daily, errors); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or System.Text.Json.JsonException) { errors.Add("读取助手配置失败：" + e.Message); }
        return Task.FromResult(new ValidationResult(errors));
    }
    public Task<LaunchResult> LaunchNormalAsync(GameConfig game, CancellationToken cancellationToken)
        => game.ToolRunMode == AssistantRunMode.Source && game.Normal.Target == LaunchTarget.Assistant
            ? Sources.LaunchGuiAsync(game, cancellationToken)
            : Processes.LaunchNormalAsync(game.NormalPath, game.Normal.Arguments, game.Normal.WorkingDirectory, cancellationToken);

    public async Task<AdapterRun> TriggerDailyAsync(GameConfig game, Guid runId, Action<string> log, CancellationToken token)
    {
        var validation = await ValidateAsync(game, true, token);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join("；", validation.Errors));
        var source = game.ToolRunMode == AssistantRunMode.Source;
        foreach (var path in new[] { game.ToolPath, game.ToolGuiPath, source ? Sources.PythonPath(game) : "" }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (Processes.FindRunning(path).Count != 0) throw new InvalidOperationException("助手已有实例，无法可靠确认其空闲与关联结果。请关闭现有助手后重试");
        token.ThrowIfCancellationRequested();
        var manual = game.Daily.Trigger == TriggerMode.Manual;
        var pathToStart = manual && !string.IsNullOrWhiteSpace(game.ToolGuiPath) ? game.ToolGuiPath : game.ToolPath;
        var arguments = manual ? "" : game.Daily.Trigger == TriggerMode.CustomCommand ? game.Daily.Arguments : VerifiedArguments(game);
        string? logPath = game.Daily.LogPath;
        if (string.IsNullOrWhiteSpace(logPath))
            logPath = Capabilities.Id switch
            {
                "bettergi" => Path.Combine(Path.GetDirectoryName(game.ToolPath)!, "log", "better-genshin-impact*.log"),
                "okww" => Path.Combine(source ? Sources.DirectoryFor(game) : Path.GetDirectoryName(game.ToolPath)!, "logs", "ok-ww.log"),
                _ => null
            };
        var tail = string.IsNullOrWhiteSpace(logPath) ? null : new LogTail(logPath);
        OwnedProcessSession? session = null;
        try
        {
            session = source ? await Sources.StartDailyAsync(game, manual, arguments, token)
                : Processes.StartOwned(pathToStart, arguments, game.Daily.WorkingDirectory);
            token.ThrowIfCancellationRequested();
            log($"已创建任务进程 PID={session.Identity.Pid}，任务参数={arguments}");
            return new ProcessAdapterRun(runId, game, session, tail, log);
        }
        catch
        {
            tail?.Dispose();
            if (session is not null) await session.DisposeAsync();
            throw;
        }
    }
    public Task<RunState> QueryStatusAsync(AdapterRun run, CancellationToken cancellationToken) => Task.FromResult(((ProcessAdapterRun)run).Status);
    public async Task<AdapterResult> WaitForCompletionAsync(AdapterRun run, CancellationToken token)
    {
        var owned = (ProcessAdapterRun)run;
        if (run.Game.Daily.Trigger == TriggerMode.Manual)
        {
            owned.FollowRemainingLogs();
            return new(RunState.Unknown, "半自动模式：助手已打开，请手动执行任务并确认实际结果", false);
        }
        var monitor = new EvidenceMonitor(Capabilities.Id, run.Game.Daily, owned.Session.Identity.Pid);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            while (owned.Session.Lines.Reader.TryRead(out var line)) { owned.Log(line); monitor.Accept(line); }
            if (owned.Tail is not null)
                foreach (var line in await owned.Tail.ReadAsync(token)) { owned.Log(line); monitor.Accept(line, true); }
            // Drain both output pipes after root exit before classifying its final result.
            if (owned.Session.HasExited)
            {
                // Descendants can inherit pipes; never wait forever for their EOF.
                if (!await owned.Session.DrainOutputAsync(token))
                {
                    owned.FollowRemainingLogs();
                    return new(RunState.Unknown, "根进程已退出，但输出仍由后代进程持有，无法确认结果完整", false, owned.Session.ExitCode);
                }
                while (owned.Session.Lines.Reader.TryRead(out var line)) { owned.Log(line); monitor.Accept(line); }
                if (owned.Tail is not null)
                    foreach (var line in await owned.Tail.ReadAsync(token)) { owned.Log(line); monitor.Accept(line, true); }
            }
            var result = monitor.Evaluate(owned.Session.HasExited, owned.Session.ExitCode, owned.Tail?.SawDiscontinuity == true);
            if (result is not null) { owned.Status = result.State; owned.FollowRemainingLogs(); return result; }
            await Task.Delay(200, token);
        }
    }
    public async Task<StopResult> RequestStopAsync(AdapterRun run, CancellationToken cancellationToken)
    {
        var owned = (ProcessAdapterRun)run;
        owned.Log("请求本次助手正常关闭；无可用停止接口时，使用所属 Job Object 清理");
        return await owned.Session.StopAsync(cancellationToken);
    }
}

public sealed class BetterGiAdapter(ProcessService processes) : ProcessToolAdapter(processes)
{
    public override AdapterCapabilities Capabilities { get; } = new("bettergi", "0.66.0", "https://www.bettergi.com/feats/command/introduce.html", true);
    protected override string VerifiedArguments(GameConfig game) => "--startOneDragon " + CommandArguments.Quote(game.Daily.ProfileName);
    protected override void ValidateTool(GameConfig game, bool daily, List<string> errors)
    {
        if (!daily || game.Daily.Trigger != TriggerMode.VerifiedCommand) return;
        var name = game.Daily.ProfileName;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") { errors.Add("请填写有效的一条龙配置名称"); return; }
        var path = Path.Combine(Path.GetDirectoryName(game.ToolPath) ?? "", "User", "OneDragon", name + ".json");
        if (!File.Exists(path)) errors.Add("BetterGI 一条龙配置不存在：" + path);
        else
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("Name", out var configuredName) || configuredName.GetString() != name) errors.Add("BetterGI 配置名称与文件内容不一致");
        }
    }
}

public sealed class March7thAdapter(ProcessService processes, SourceAssistantService? sources = null) : ProcessToolAdapter(processes, sources)
{
    public override AdapterCapabilities Capabilities { get; } = new("march7th", "v2026.10.16", "https://github.com/moesnow/March7thAssistant/blob/v2026.10.16/main.py", true);
    protected override string VerifiedArguments(GameConfig game) => string.IsNullOrWhiteSpace(game.Daily.ProfileName) ? "main" : "--workflow-name " + CommandArguments.Quote(game.Daily.ProfileName);
    protected override void ValidateTool(GameConfig game, bool daily, List<string> errors)
    {
        var source = game.ToolRunMode == AssistantRunMode.Source;
        if (!source && !daily && game.Normal.Target == LaunchTarget.Assistant && !Path.GetFileName(game.NormalPath).Equals("March7th Launcher.exe", StringComparison.OrdinalIgnoreCase))
            errors.Add("普通启动请选择 March7th Launcher.exe；Assistant 程序默认会执行任务");
        if (!daily || game.Daily.Trigger == TriggerMode.Manual) return;
        if (!source && !Path.GetFileName(game.ToolPath).Equals("March7th Assistant.exe", StringComparison.OrdinalIgnoreCase)) errors.Add("日常任务请选择 March7th Assistant.exe");
        var actualConfigPath = Path.Combine(source ? Sources.DirectoryFor(game) : Path.GetDirectoryName(game.ToolPath) ?? Path.GetDirectoryName(game.ToolGuiPath) ?? "", "config.yaml");
        var configPath = string.IsNullOrWhiteSpace(game.ToolConfigPath) ? actualConfigPath : game.ToolConfigPath;
        if (!string.Equals(ProcessService.Normalize(configPath), ProcessService.Normalize(actualConfigPath), StringComparison.OrdinalIgnoreCase))
            errors.Add("该 March7th 版本固定读取助手目录的 config.yaml，请选择实际配置文件");
        if (!File.Exists(configPath)) { errors.Add("March7th 配置文件不存在，请先完成助手初始化并设置 config.yaml 路径"); return; }
        var yaml = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(File.ReadAllText(configPath));
        if (yaml is null) { errors.Add("March7th 配置为空"); return; }
        string Value(string key) => yaml.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
        if (Value("scheduled_run_enable").Equals("true", StringComparison.OrdinalIgnoreCase)
            || (yaml.TryGetValue("scheduled_tasks", out var scheduled) && scheduled is IEnumerable<object> entries
                && entries.OfType<Dictionary<object, object>>().Any(entry => entry.TryGetValue("enabled", out var enabled) && enabled?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)))
            errors.Add("请关闭 March7th 内启用的定时计划，避免与 Hub 操作冲突");
        if (!Value("after_finish").Equals("None", StringComparison.OrdinalIgnoreCase)) errors.Add("March7th after_finish 必须为 None");
        if (!Value("pause_after_success").Equals("false", StringComparison.OrdinalIgnoreCase)) errors.Add("March7th pause_after_success 必须为 false");
        if (!Value("exit_after_failure").Equals("true", StringComparison.OrdinalIgnoreCase)) errors.Add("March7th exit_after_failure 必须为 true");
        if (string.IsNullOrWhiteSpace(game.Daily.ProfileName) && game.Daily.Trigger == TriggerMode.CustomCommand && CommandArguments.Parse(game.Daily.Arguments).Length == 0)
            errors.Add("March7th 自定义日常参数不能为空");
    }
}

public sealed class OkWwAdapter(ProcessService processes, SourceAssistantService? sources = null) : ProcessToolAdapter(processes, sources)
{
    public override AdapterCapabilities Capabilities { get; } = new("okww", "v3.7.4", "https://github.com/ok-oldking/ok-wuthering-waves/blob/v3.7.4/README.md", true);
    protected override string VerifiedArguments(GameConfig game) => "-t 1";
    protected override void ValidateTool(GameConfig game, bool daily, List<string> errors)
    {
        if (daily && game.Daily.Trigger == TriggerMode.CustomCommand && !CommandArguments.Parse(game.Daily.Arguments).Any(a => a is "-t" or "--task")) errors.Add("OK-WW 自动任务需要核实 -t/--task 任务选择参数");
    }
}

public sealed class ZzzAdapter(ProcessService processes, SourceAssistantService? sources = null) : ProcessToolAdapter(processes, sources)
{
    public override AdapterCapabilities Capabilities { get; } = new("zzz", "main bdafa6e3 (2026-10-18)", "https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon/blob/bdafa6e3e91e537f279dcaaf7514ef1314519893/src/one_dragon/launcher/exe_launcher.py", true);
    protected override string VerifiedArguments(GameConfig game) => game.ToolRunMode == AssistantRunMode.Source ? "" : "--onedragon";
    protected override void ValidateTool(GameConfig game, bool daily, List<string> errors)
    {
        if (game.ToolRunMode == AssistantRunMode.Source) return;
        if (!daily && game.Normal.Target != LaunchTarget.Assistant) return;
        var path = !daily ? game.NormalPath
            : game.Daily.Trigger == TriggerMode.Manual && !string.IsNullOrWhiteSpace(game.ToolGuiPath) ? game.ToolGuiPath : game.ToolPath;
        if (!new[] { "OneDragon-Launcher.exe", "OneDragon-RuntimeLauncher.exe" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            errors.Add("绝区零一条龙请选择 OneDragon-Launcher.exe 或 OneDragon-RuntimeLauncher.exe");
        if (daily && game.Daily.Trigger == TriggerMode.CustomCommand && !CommandArguments.Parse(game.Daily.Arguments).Any(a => a is "-o" or "--onedragon"))
            errors.Add("安装版一条龙自动任务需要 -o/--onedragon 参数");
    }
}

public sealed class GenericAdapter(ProcessService processes) : ProcessToolAdapter(processes)
{
    public override AdapterCapabilities Capabilities { get; } = new("generic", "用户配置", "", false);
    protected override string VerifiedArguments(GameConfig game) => throw new InvalidOperationException("请配置自定义入口");
}
