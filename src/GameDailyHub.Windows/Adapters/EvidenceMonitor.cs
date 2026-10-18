using System.Text.RegularExpressions;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Adapters;

/// <summary>Conservative, versioned parser. A process exit is never positive task evidence.</summary>
public sealed class EvidenceMonitor
{
    private readonly string _adapter;
    private readonly DailyTaskProfile _profile;
    private readonly int _pid;
    private readonly Queue<string> _evidence = new();
    private bool _ownedLogEntry;
    private bool _started, _ended, _failed, _cancelled, _stopping;
    public EvidenceMonitor(string adapter, DailyTaskProfile profile, int pid) { _adapter = adapter; _profile = profile; _pid = pid; }
    public static string StripAnsi(string line) => Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    private static bool Match(string line, string pattern) => !string.IsNullOrEmpty(pattern) && Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public void Accept(string input, bool fromFile = false)
    {
        var line = StripAnsi(input);
        if (_adapter == "bettergi" && fromFile)
        {
            if (Regex.IsMatch(line, @"^\[\d\d:\d\d:\d\d", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                _ownedLogEntry = line.Contains(":P" + _pid + ":", StringComparison.Ordinal);
            if (!_ownedLogEntry) return;
        }
        _evidence.Enqueue(line);
        while (_evidence.Count > 150) _evidence.Dequeue();
        if (_profile.Completion == CompletionMode.LogRules)
        {
            if (Match(line, _profile.StartPattern)) _started = true;
            if (!_started) return;
            if (Match(line, _profile.FailurePattern)) _failed = true;
            if (Match(line, _profile.SuccessPattern)) _ended = true;
            return;
        }
        switch (_adapter)
        {
            case "bettergi":
                if (line.Contains("启用一条龙配置：") || line.Contains("一条龙任务执行:")) _started = true;
                if (_started && (line.Contains("[ERR]") || line.Contains("执行配置组任务时失败") || line.Contains("任务启动失败") || line.Contains("执行异常"))) _failed = true;
                if (_started && (line.Contains("任务被取消") || line.Contains("一条龙在启动阶段被取消"))) _cancelled = true;
                if (_started && line.Contains("一条龙和配置组任务结束")) _ended = true;
                break;
            case "march7th":
                if (line.Contains("开始运行")) _started = true;
                if (line.Contains("停止运行")) _stopping = true;
                if (_started && _stopping && Regex.IsMatch(line, @"^[\s=─━\-]*完成[\s=─━\-]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100))) _ended = true;
                if (Regex.IsMatch(line, @"\bERROR\b", RegexOptions.None, TimeSpan.FromMilliseconds(100)) || line.Contains("发生错误") || line.Contains("首次使用请先打开") || line.Contains("每日实训未完成") || line.Contains("清体力未完成") || line.Contains("仍未完成")) _failed = true;
                if (line.Contains("手动强制停止")) _cancelled = true;
                break;
            case "okww":
                if (line.Contains("start running onetime_task") || line.Contains("get one enabled onetime_task") || line.Contains("get queued onetime_task")) _started = true;
                if (Regex.IsMatch(line, @"(?:^|:)Daily Task Completed\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(100))) { _started = true; _ended = true; }
                if (Regex.IsMatch(line, @"\b(?:ERROR|CRITICAL)\b", RegexOptions.None, TimeSpan.FromMilliseconds(100)) || line.Contains("Traceback (most recent call last)") || line.Contains("exception stopped") || line.Contains("Task Failed") || line.Contains("TaskDisabledException") || line.Contains("NightmareNestTask Failed") || line.Contains("GardenTask Failed") || line.Contains("MergeEchoTask Failed") || line.Contains("can not battle pass")) _failed = true;
                break;
            case "zzz":
                // Only the top-level operation can end the batch; child operations also report success.
                if (Match(line, @"指令\[\s*一条龙\s*\] 节点 ")) _started = true;
                if (Match(line, @"指令\[\s*一条龙\s*\] 执行成功 返回状态 全部结束\s*$")) { _started = true; _ended = true; }
                if (Match(line, @"\[(?:ERROR|CRITICAL)\]") || line.Contains("Traceback (most recent call last)") || line.Contains("执行失败 返回状态")) _failed = true;
                break;
        }
    }
    public AdapterResult? Evaluate(bool exited, int? exitCode, bool discontinuity = false)
    {
        var evidence = string.Join(Environment.NewLine, _evidence);
        if (_profile.Completion == CompletionMode.Manual) return new(RunState.Unknown, "人工判定模式：请在助手内操作并确认最终结果", false, exitCode, evidence);
        if (discontinuity) return new(RunState.Unknown, "日志发生截断或替换，无法确认本轮证据完整", false, exitCode, evidence);
        if (_cancelled) return new(RunState.Failed, "助手报告任务中断", false, exitCode, evidence);
        if (_failed) return new(RunState.Failed, "本轮日志包含任务失败证据", false, exitCode, evidence);
        if (exited && exitCode is not (null or 0)) return new(RunState.Failed, "助手异常退出", true, exitCode, evidence);
        if (_started && _ended)
        {
            if (_profile.Completion == CompletionMode.LogRules && !exited) return null;
            if (_adapter == "bettergi" && _profile.Completion == CompletionMode.AdapterEvidence)
                return new(RunState.Unknown, "BetterGI 已报告流程结束；其结束事件无法保证全部子任务成功，请核对任务日志", true, exitCode, evidence);
            if (_adapter == "march7th" && !exited) return null;
            if (_adapter == "zzz" && _profile.Completion == CompletionMode.AdapterEvidence)
            {
                if (!exited) return null;
                return new(RunState.Unknown, "绝区零一条龙已报告流程结束，请核对各子任务结果后确认", true, exitCode, evidence);
            }
            return new(RunState.Success, "已取得本轮日常完成证据", true, exitCode, evidence);
        }
        if (exited) return new(RunState.Unknown, "助手已退出，未取得完整的任务完成证据", true, exitCode, evidence);
        return null;
    }
}
