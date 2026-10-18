using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GameDailyHub.Core.Models;
using YamlDotNet.RepresentationModel;

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

    public static bool SupportsFiles(string adapterId) => GameCatalog.Supports(adapterId);

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
            "zzz" => normalized.StartsWith("config/", StringComparison.Ordinal)
                && (normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                && !normalized.EndsWith(".sample.yml", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith(".merged.yml", StringComparison.OrdinalIgnoreCase)
                && normalized is not ("config/project.yml" or "config/repository.yml")
                && !normalized.Split('/').Contains("run_record", StringComparer.OrdinalIgnoreCase),
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
            "zzz" => Directory.Exists(Path.Combine(root, "config"))
                ? Directory.EnumerateFiles(Path.Combine(root, "config"), "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Where(f => IsAllowed(adapterId, f)) : [],
            _ => []
        };
        return found.Where(f => File.Exists(Path.Combine(root, f))).Order(StringComparer.Ordinal);
    }

    public static string Export(GameConfig game, string? root)
        => JsonSerializer.Serialize(CreateProfile(game, root), JsonDefaults.Options);

    internal static AssistantProfile CreateProfile(GameConfig game, string? root, bool allowEmpty = false)
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
            if (root is null || !Directory.Exists(root))
            {
                if (allowEmpty) return profile;
                throw new InvalidOperationException("未找到助手目录，请先设置助手路径（源码模式请先安装源码）");
            }
            foreach (var relative in Discover(game.AdapterId, root))
            {
                if (new FileInfo(Path.Combine(root, relative)).Length > MaxFileBytes) throw new InvalidDataException("配置文件过大：" + relative);
                profile.Files.Add(new() { Path = relative, Content = File.ReadAllText(Path.Combine(root, relative)) });
            }
            if (!allowEmpty && profile.Files.Count == 0) throw new InvalidOperationException("助手目录中没有找到可导出的配置文件，请先运行并设置一次助手：" + root);
        }
        Validate(profile);
        return profile;
    }

    public static AssistantProfile Read(string json)
    {
        AssistantProfile profile;
        try { profile = JsonSerializer.Deserialize<AssistantProfile>(json, JsonDefaults.Options) ?? throw new InvalidDataException("助手配置为空"); }
        catch (JsonException e) { throw new InvalidDataException("不是有效的助手配置文件：" + e.Message, e); }
        Validate(profile);
        return profile;
    }

    internal static void Validate(AssistantProfile profile)
    {
        if (profile.Kind != AssistantProfile.FileKind) throw new InvalidDataException("不是 GameDailyHub 助手配置文件（整体配置请在“设置 → 数据与备份”导入）");
        if (profile.SchemaVersion != 1) throw new InvalidDataException("不支持助手配置版本 " + profile.SchemaVersion);
        if (!GameCatalog.Supports(profile.AdapterId)) throw new InvalidDataException("不支持的助手类型");
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
    }

    /// <summary>
    /// Validates everything and prepares all file contents first; the game's settings and the files are only changed afterwards.
    /// The caller must make sure the assistant is not running.
    /// </summary>
    public static AssistantImportResult Import(string json, GameConfig game, string? root)
    {
        var profile = Read(json);
        var pending = Prepare(profile, game, root);
        var result = WriteFiles(pending);
        Apply(profile, game);
        return result;
    }

    internal static List<(string Full, string Content)> Prepare(AssistantProfile profile, GameConfig game, string? root)
    {
        Validate(profile);
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
                pending.Add((full, file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? MergeJson(file.Content, existing, file.Path)
                    : game.AdapterId == "zzz" ? MergeZzzYaml(file.Content, existing) : MergeYaml(file.Content, existing)));
            }
        }
        return pending;
    }

    internal static AssistantImportResult WriteFiles(List<(string Full, string Content)> pending)
    {
        if (pending.Select(f => f.Full).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pending.Count)
            throw new InvalidDataException("多个配置指向同一目标文件");
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        var staged = new List<(string Full, string Temp, string? Backup)>();
        var written = new List<(string Full, string? Backup)>();
        try
        {
            foreach (var (full, content) in pending)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                var backup = File.Exists(full) ? full + ".gdh-" + stamp + ".bak" : null;
                var temp = full + ".gdh-" + stamp + ".tmp";
                staged.Add((full, temp, backup));
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (backup is not null) File.Copy(full, backup, false);
            }
            foreach (var file in staged)
            {
                File.Move(file.Temp, file.Full, true);
                written.Add((file.Full, file.Backup));
            }
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            foreach (var file in written.AsEnumerable().Reverse())
                try { if (file.Backup is null) File.Delete(file.Full); else File.Copy(file.Backup, file.Full, true); }
                catch (Exception rollbackError) { errors.Add(rollbackError); }
            if (errors.Count > 1) throw new AggregateException("导入失败，部分文件恢复失败，请使用 .gdh-*.bak 备份恢复", errors);
            throw;
        }
        finally
        {
            foreach (var file in staged) if (File.Exists(file.Temp)) File.Delete(file.Temp);
        }
        return new(pending.Count, staged.Count(f => f.Backup is not null));
    }

    internal static void Apply(AssistantProfile profile, GameConfig game)
    {
        var daily = game.Daily;
        daily.Trigger = profile.Trigger; daily.Arguments = profile.Arguments; daily.ProfileName = profile.ProfileName; daily.Completion = profile.Completion;
        daily.StartPattern = profile.StartPattern; daily.SuccessPattern = profile.SuccessPattern; daily.FailurePattern = profile.FailurePattern;
        daily.TimeoutMinutes = profile.TimeoutMinutes; daily.RetryCount = profile.RetryCount; daily.ContinueOnFailure = profile.ContinueOnFailure;
    }

    private static string MergeZzzYaml(string incoming, string? existing)
    {
        static YamlNode Parse(string text)
        {
            var yaml = new YamlStream();
            try { yaml.Load(new StringReader(text)); }
            catch (YamlDotNet.Core.YamlException e) { throw new InvalidDataException("绝区零配置 YAML 格式无效", e); }
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not (YamlMappingNode or YamlSequenceNode))
                throw new InvalidDataException("绝区零配置须包含一个 YAML 对象或列表");
            return yaml.Documents[0].RootNode;
        }
        static YamlNode? Localize(YamlNode node, YamlNode? local)
        {
            if (node is YamlScalarNode scalar && AbsolutePath().IsMatch(scalar.Value ?? "")) return local is YamlScalarNode ? local : null;
            if (node is YamlMappingNode map)
            {
                foreach (var key in map.Children.Keys.ToArray())
                {
                    YamlNode? current = null;
                    if (local is YamlMappingNode localMap) localMap.Children.TryGetValue(key, out current);
                    var value = Localize(map.Children[key], current);
                    if (value is null) map.Children.Remove(key); else map.Children[key] = value;
                }
            }
            if (node is YamlSequenceNode sequence)
            {
                var current = local as YamlSequenceNode;
                for (var i = sequence.Children.Count - 1; i >= 0; i--)
                {
                    var value = Localize(sequence.Children[i], current is not null && i < current.Children.Count ? current.Children[i] : null);
                    if (value is null) sequence.Children.RemoveAt(i); else sequence.Children[i] = value;
                }
            }
            return node;
        }
        var root = Localize(Parse(incoming), existing is null ? null : Parse(existing))!;
        var output = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(output, false);
        return output.ToString();
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
    private static bool ContainsPath(JsonNode? node) => node switch
    {
        JsonObject o => o.Any(p => ContainsPath(p.Value)),
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
