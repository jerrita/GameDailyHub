using System.Text.Json;
using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Services;

namespace GameDailyHub.Windows.Adapters;

public sealed class MaaEndAdapter(ProcessService processes) : ProcessToolAdapter(processes)
{
    public override AdapterCapabilities Capabilities { get; } = new("maaend", "MXU 5e923e83 (2026-10-19)", "https://github.com/MistEO/MXU/blob/5e923e83b30a102fb78da35c30ee2e1559aea682/README.md", true);
    protected override string VerifiedArguments(GameConfig game) => "--autostart --quit-after-run"
        + (string.IsNullOrWhiteSpace(game.Daily.ProfileName) ? "" : " --instance " + CommandArguments.Quote(game.Daily.ProfileName));

    protected override void ValidateTool(GameConfig game, bool daily, List<string> errors)
    {
        if (!daily && game.Normal.Target != LaunchTarget.Assistant
            && !new[] { "MaaEnd.exe", "mxu.exe" }.Contains(Path.GetFileName(game.NormalPath), StringComparer.OrdinalIgnoreCase)) return;
        if (game.ToolRunMode != AssistantRunMode.Binary) { errors.Add("MaaEnd 请选择安装版运行"); return; }
        var path = !daily ? game.NormalPath : game.Daily.Trigger == TriggerMode.Manual && !string.IsNullOrWhiteSpace(game.ToolGuiPath) ? game.ToolGuiPath : game.ToolPath;
        if (!new[] { "MaaEnd.exe", "mxu.exe" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            errors.Add("MaaEnd 请选择 MaaEnd.exe 或其目录内的 mxu.exe");
        var root = Path.GetDirectoryName(path) ?? "";
        var interfacePath = Path.Combine(root, "interface.json");
        if (!File.Exists(interfacePath)) { errors.Add("MaaEnd 目录缺少 interface.json，请选择完整安装目录中的程序"); return; }
        using var definition = JsonDocument.Parse(File.ReadAllText(interfacePath), new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (definition.RootElement.ValueKind != JsonValueKind.Object || !definition.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != "MaaEnd")
        { errors.Add("所选程序目录的 interface.json 不属于 MaaEnd"); return; }

        var automatic = daily && game.Daily.Trigger != TriggerMode.Manual;
        var configPath = Path.Combine(root, "config", "mxu-MaaEnd.json");
        if (!File.Exists(configPath))
        {
            if (automatic) errors.Add("请先在 MaaEnd 中完成实例配置并保存");
            return;
        }
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var data = config.RootElement;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("instances", out var instances) || instances.ValueKind != JsonValueKind.Array
            || instances.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.Object))
        { errors.Add("MaaEnd 配置结构无效，请在助手中重新保存"); return; }
        if (settings.TryGetProperty("autoRunOnLaunch", out var autoRun) && autoRun.ValueKind == JsonValueKind.True)
            errors.Add("请关闭 MaaEnd 的“手动启动时也自动执行”");
        if (instances.EnumerateArray().Any(i => i.TryGetProperty("schedulePolicies", out var policies) && policies.ValueKind == JsonValueKind.Array
            && policies.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.Object && p.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)))
            errors.Add("请关闭 MaaEnd 中启用的定时策略，避免与 Hub 队列冲突");
        if (!automatic) return;

        var instanceName = game.Daily.ProfileName;
        if (game.Daily.Trigger == TriggerMode.CustomCommand)
        {
            instanceName = "";
            var arguments = CommandArguments.Parse(game.Daily.Arguments);
            var autostart = false;
            var quit = false;
            var selected = false;
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                if (argument == "--autostart") autostart = true;
                else if (argument is "--quit-after-run" or "-q") quit = true;
                else if (argument is "--instance" or "-i" || argument.StartsWith("--instance=", StringComparison.Ordinal) || argument.StartsWith("-i=", StringComparison.Ordinal))
                {
                    if (selected) { errors.Add("MaaEnd 自动任务仅允许指定一个实例"); return; }
                    selected = true;
                    instanceName = argument.Contains('=') ? argument[(argument.IndexOf('=') + 1)..] : ++i < arguments.Length ? arguments[i] : "";
                    if (string.IsNullOrWhiteSpace(instanceName)) { errors.Add("MaaEnd 实例参数缺少名称"); return; }
                }
                else { errors.Add("不支持的 MaaEnd 自动任务参数：" + argument); return; }
            }
            if (!autostart || !quit) errors.Add("MaaEnd 自动任务需要 --autostart 和 -q/--quit-after-run 参数");
        }
        static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var matches = instances.EnumerateArray().Where(i => string.IsNullOrWhiteSpace(instanceName)
            ? Text(i, "id") is { } id && id == Text(settings, "autoStartInstanceId")
            : Text(i, "name") == instanceName).ToArray();
        if (matches.Length != 1) errors.Add(string.IsNullOrWhiteSpace(instanceName) ? "请在 MaaEnd 中选择默认自动执行实例，或填写唯一的实例名称" : "MaaEnd 实例名称不存在或重复，请核对后重试");
    }
}
