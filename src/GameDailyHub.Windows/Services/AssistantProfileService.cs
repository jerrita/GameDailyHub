using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

public sealed class AssistantFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>An assistant's own configuration files plus the Hub's automation settings for it.</summary>
public sealed class AssistantProfile
{
    public const string FileKind = "GameDailyHub.AssistantProfile";
    public string Kind { get; set; } = FileKind;
    public int SchemaVersion { get; set; } = 1;
    public string AdapterId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public TriggerMode Trigger { get; set; }
    public string Arguments { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public CompletionMode Completion { get; set; }
    public string StartPattern { get; set; } = "";
    public string SuccessPattern { get; set; } = "";
    public string FailurePattern { get; set; } = "";
    public double TimeoutMinutes { get; set; } = 60;
    public int RetryCount { get; set; }
    public bool ContinueOnFailure { get; set; } = true;
    /// <summary>Paths are relative to the assistant's install (or source) directory.</summary>
    public List<AssistantFile> Files { get; set; } = [];
}

public sealed record AssistantImportResult(int FilesWritten, int BackupsMade);

/// <summary>
/// Exports and imports an assistant's own configuration files so they can be reused on another machine.
/// Only known configuration files of each assistant are handled. Absolute Windows paths inside them belong to
/// the machine and are never imported: the target machine's value is kept, or the setting is left at its default.
/// The Hub-side install paths, working directories, log paths and source directory are not part of the file either.
/// </summary>
public static partial class AssistantProfileService
{
    private const long MaxFileBytes = 8 * 1024 * 1024;

    [GeneratedRegex(@"^(?:[A-Za-z]:[\\/]|\\\\)")]
    private static partial Regex AbsolutePath();
    [GeneratedRegex(@"^(?<indent> *)(?<key>[^\s#:'""\-\[{][^:#]*?):(?:[ \t]+(?<value>[^#\s].*?))?[ \t]*$")]
    private static partial Regex YamlKeyLine();
    [GeneratedRegex(@"^ *-[ \t]+(?<value>[^#\s].*?)[ \t]*$")]
    private static partial Regex YamlListLine();

    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool SupportsFiles(string adapterId) => adapterId is "bettergi" or "march7th" or "okww";

    /// <summary>Install directory (binary) or source directory (source mode) that holds the assistant's files.</summary>
    public static string? RootFor(GameConfig game, SourceAssistantService sources)
    {
        if (game.ToolRunMode == AssistantRunMode.Source) return sources.DirectoryFor(game);
        return string.IsNullOrWhiteSpace(game.ToolPath) ? null : Path.GetDirectoryName(ProcessService.Normalize(game.ToolPath));
    }

    public static bool IsAllowed(string adapterId, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (normalized.Split('/').Any(p => p is "" or "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
        return adapterId switch
        {
            "march7th" => normalized == "config.yaml",
            "bettergi" => normalized == "User/config.json" || OneLevel(normalized, "User/OneDragon/", ".json"),
            "okww" => OneLevel(normalized, "configs/", ".json"),
            _ => false
        };
    }
    private static bool OneLevel(string path, string prefix, string extension)
        => path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length + extension.Length
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && !path[prefix.Length..].Contains('/');

    private static IEnumerable<string> Discover(string adapterId, string root)
    {
        IEnumerable<string> In(string directory, string pattern) => Directory.Exists(Path.Combine(root, directory))
            ? Directory.EnumerateFiles(Path.Combine(root, directory), pattern).Select(f => directory + "/" + Path.GetFileName(f)) : [];
        var found = adapterId switch
        {
            "march7th" => ["config.yaml"],
            "bettergi" => new[] { "User/config.json" }.Concat(In("User/OneDragon", "*.json")),
            "okww" => In("configs", "*.json"),
            _ => []
        };
        return found.Where(f => File.Exists(Path.Combine(root, f)) && new FileInfo(Path.Combine(root, f)).Length <= MaxFileBytes);
    }

    public static string Export(GameConfig game, string? root)
    {
        var daily = game.Daily;
        var profile = new AssistantProfile
        {
            AdapterId = game.AdapterId,
            ToolName = game.ToolName,
            Trigger = daily.Trigger,
            Arguments = daily.Arguments,
            ProfileName = daily.ProfileName,
            Completion = daily.Completion,
            StartPattern = daily.StartPattern,
            SuccessPattern = daily.SuccessPattern,
            FailurePattern = daily.FailurePattern,
            TimeoutMinutes = daily.TimeoutMinutes,
            RetryCount = daily.RetryCount,
            ContinueOnFailure = daily.ContinueOnFailure
        };
        if (SupportsFiles(game.AdapterId))
        {
            if (root is null || !Directory.Exists(root)) throw new InvalidOperationException("未找到助手目录，请先设置助手路径（源码模式请先安装源码）");
            foreach (var relative in Discover(game.AdapterId, root))
                profile.Files.Add(new() { Path = relative, Content = File.ReadAllText(Path.Combine(root, relative)) });
            if (profile.Files.Count == 0) throw new InvalidOperationException("助手目录中没有找到可导出的配置文件，请先运行并设置一次助手：" + root);
        }
        return JsonSerializer.Serialize(profile, JsonDefaults.Options);
    }

    public static AssistantProfile Read(string json)
    {
        AssistantProfile profile;
        try { profile = JsonSerializer.Deserialize<AssistantProfile>(json, JsonDefaults.Options) ?? throw new InvalidDataException("助手配置为空"); }
        catch (JsonException e) { throw new InvalidDataException("不是有效的助手配置文件：" + e.Message, e); }
        if (profile.Kind != AssistantProfile.FileKind) throw new InvalidDataException("不是 GameDailyHub 助手配置文件（整体配置请在“设置 → 数据与备份”导入）");
        if (profile.SchemaVersion != 1) throw new InvalidDataException("不支持助手配置版本 " + profile.SchemaVersion);
        if (new[] { profile.Arguments, profile.ProfileName, profile.StartPattern, profile.SuccessPattern, profile.FailurePattern, profile.ToolName }.Any(s => s is null) || profile.Files is null)
            throw new InvalidDataException("助手配置包含无效的空值");
        if (!Enum.IsDefined(profile.Trigger) || !Enum.IsDefined(profile.Completion)) throw new InvalidDataException("枚举配置无效");
        if (!double.IsFinite(profile.TimeoutMinutes) || profile.TimeoutMinutes <= 0 || profile.TimeoutMinutes > 1440 || profile.RetryCount is < 0 or > 10)
            throw new InvalidDataException("超时须在 0–1440 分钟内，重试须在 0–10 次内");
        foreach (var file in profile.Files)
        {
            if (file is null || file.Path is null || file.Content is null || !IsAllowed(profile.AdapterId, file.Path)) throw new InvalidDataException("助手配置包含不受支持的文件：" + file?.Path);
            if (Encoding.UTF8.GetByteCount(file.Content) > MaxFileBytes) throw new InvalidDataException("配置文件过大：" + file.Path);
        }
        if (profile.Files.Select(f => f.Path.Replace('\\', '/').ToLowerInvariant()).Distinct().Count() != profile.Files.Count) throw new InvalidDataException("助手配置包含重复文件");
        return profile;
    }

    /// <summary>
    /// Validates everything and prepares all file contents first; the game's settings and the files are only changed afterwards.
    /// The caller must make sure the assistant is not running.
    /// </summary>
    public static AssistantImportResult Import(string json, GameConfig game, string? root)
    {
        var profile = Read(json);
        if (profile.AdapterId != game.AdapterId) throw new InvalidDataException($"助手类型不匹配：文件为 {profile.AdapterId}，当前游戏使用 {game.AdapterId}");
        var pending = new List<(string Full, string Content)>();
        if (profile.Files.Count > 0)
        {
            if (root is null) throw new InvalidOperationException("未找到助手目录，请先设置助手路径（源码模式请先安装源码）");
            if (!Directory.Exists(root)) throw new InvalidOperationException("助手目录不存在，请先安装助手后再导入：" + root);
            foreach (var file in profile.Files)
            {
                var full = Path.GetFullPath(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("配置文件路径越界：" + file.Path);
                var existing = File.Exists(full) ? File.ReadAllText(full) : null;
                pending.Add((full, file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? MergeJson(file.Content, existing, file.Path) : MergeYaml(file.Content, existing)));
            }
        }
        var backups = 0;
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        foreach (var (full, content) in pending)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (File.Exists(full)) { File.Copy(full, full + ".gdh-" + stamp + ".bak", false); backups++; }
            var temp = full + ".gdh-tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, full, true);
        }
        var daily = game.Daily;
        daily.Trigger = profile.Trigger; daily.Arguments = profile.Arguments; daily.ProfileName = profile.ProfileName; daily.Completion = profile.Completion;
        daily.StartPattern = profile.StartPattern; daily.SuccessPattern = profile.SuccessPattern; daily.FailurePattern = profile.FailurePattern;
        daily.TimeoutMinutes = profile.TimeoutMinutes; daily.RetryCount = profile.RetryCount; daily.ContinueOnFailure = profile.ContinueOnFailure;
        return new(pending.Count, backups);
    }

    private static bool IsPath(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && AbsolutePath().IsMatch(text);

    private static string MergeJson(string incoming, string? existing, string name)
    {
        JsonNode? imported, current = null;
        try { imported = JsonNode.Parse(incoming, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new InvalidDataException(name + " 不是有效的 JSON：" + e.Message, e); }
        if (imported is not JsonObject root) throw new InvalidDataException(name + " 的根节点须为对象");
        if (existing is not null)
        {
            try { current = JsonNode.Parse(existing, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
            catch (JsonException) { current = null; }
        }
        LocalizeJson(root, current as JsonObject);
        return root.ToJsonString(FileJson);
    }
    private static void LocalizeJson(JsonObject incoming, JsonObject? existing)
    {
        foreach (var key in incoming.Select(p => p.Key).ToArray())
        {
            var node = incoming[key];
            var local = existing is not null && existing.TryGetPropertyValue(key, out var found) ? found : null;
            switch (node)
            {
                case JsonObject child: LocalizeJson(child, local as JsonObject); break;
                case JsonArray array when array.Any(IsPath) || array.OfType<JsonObject>().Any(ContainsPath):
                    if (local is JsonArray keep) incoming[key] = keep.DeepClone(); else incoming.Remove(key);
                    break;
                default:
                    if (IsPath(node))
                    {
                        if (local is JsonValue) incoming[key] = local.DeepClone(); else incoming.Remove(key);
                    }
                    break;
            }
        }
    }
    private static bool ContainsPath(JsonNode node) => node switch
    {
        JsonObject o => o.Any(p => ContainsPath(p.Value!)),
        JsonArray a => a.Any(ContainsPath),
        _ => IsPath(node)
    };

    /// <summary>Line based so comments and layout of the assistant's YAML survive; only simple "key: value" lines are localized.</summary>
    private static string MergeYaml(string incoming, string? existing)
    {
        var local = new Dictionary<string, string>();
        if (existing is not null) foreach (var (path, line, _) in YamlLines(existing)) if (line.Groups["value"].Success) local[path] = line.Groups["value"].Value;
        var eol = incoming.Contains("\r\n") ? "\r\n" : "\n";
        var output = new List<string>();
        var keyLines = YamlLines(incoming).ToDictionary(t => t.Index, t => t);
        var lines = Regex.Split(incoming, "\r?\n");
        for (var i = 0; i < lines.Length; i++)
        {
            if (keyLines.TryGetValue(i, out var entry) && entry.Match.Groups["value"].Success && AbsolutePath().IsMatch(Unquote(entry.Match.Groups["value"].Value)))
            {
                if (local.TryGetValue(entry.Path, out var value)) output.Add(entry.Match.Groups["indent"].Value + entry.Match.Groups["key"].Value + ": " + value);
                continue;
            }
            if (YamlListLine().Match(lines[i]) is { Success: true } item && AbsolutePath().IsMatch(Unquote(item.Groups["value"].Value))) continue;
            output.Add(lines[i]);
        }
        return string.Join(eol, output);
    }
    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
    private static IEnumerable<(string Path, Match Match, int Index)> YamlLines(string text)
    {
        var stack = new List<(int Indent, string Key)>();
        var lines = Regex.Split(text, "\r?\n");
        for (var i = 0; i < lines.Length; i++)
        {
            var match = YamlKeyLine().Match(lines[i]);
            if (!match.Success) continue;
            var indent = match.Groups["indent"].Length;
            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            var key = Unquote(match.Groups["key"].Value);
            stack.Add((indent, key));
            yield return (string.Join("/", stack.Select(s => s.Key)), match, i);
        }
    }
}
