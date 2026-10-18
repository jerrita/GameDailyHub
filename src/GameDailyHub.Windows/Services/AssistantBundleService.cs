using System.Text.Json;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

public sealed class AssistantBundle
{
    public const string FileKind = "GameDailyHub.AssistantBundle";
    public string Kind { get; set; } = FileKind;
    public int SchemaVersion { get; set; } = 1;
    public List<AssistantProfile> Assistants { get; set; } = [];
}

public sealed record AssistantLocation(GameConfig Game, string? Root);
public sealed record AssistantBundleExport(string Json, int AssistantsExported, IReadOnlyList<string> Skipped);
public sealed record AssistantBundleImport(int AssistantsImported, int FilesWritten, int BackupsMade);

public static class AssistantBundleService
{
    public static AssistantBundleExport Export(IEnumerable<AssistantLocation> locations)
    {
        var bundle = new AssistantBundle();
        var skipped = new List<string>();
        foreach (var location in locations)
        {
            var profile = AssistantProfileService.CreateProfile(location.Game, location.Root, allowEmpty: true);
            if (profile.Files.Count == 0) { skipped.Add(location.Game.ToolName); continue; }
            bundle.Assistants.Add(profile);
        }
        if (bundle.Assistants.Count == 0) throw new InvalidOperationException("没有可导出的助手配置。跳过：" + string.Join("、", skipped));
        Validate(bundle);
        return new(JsonSerializer.Serialize(bundle, JsonDefaults.Options), bundle.Assistants.Count, skipped);
    }

    public static AssistantBundle Read(string json)
    {
        AssistantBundle bundle;
        try { bundle = JsonSerializer.Deserialize<AssistantBundle>(json, JsonDefaults.Options) ?? throw new InvalidDataException("助手配置为空"); }
        catch (JsonException e) { throw new InvalidDataException("助手配置 JSON 格式无效", e); }
        if (bundle.Kind == AssistantProfile.FileKind)
            bundle = new AssistantBundle { Assistants = [AssistantProfileService.Read(json)] };
        Validate(bundle);
        return bundle;
    }

    private static void Validate(AssistantBundle bundle)
    {
        if (bundle.Kind != AssistantBundle.FileKind || bundle.SchemaVersion != 1)
            throw new InvalidDataException("请选择 GameDailyHub 导出的助手配置文件（版本 1）");
        if (bundle.Assistants is null || bundle.Assistants.Count == 0 || bundle.Assistants.Count > GameCatalog.Games.Count)
            throw new InvalidDataException("助手配置数量无效");
        foreach (var profile in bundle.Assistants)
        {
            if (profile is null) throw new InvalidDataException("助手配置为空");
            AssistantProfileService.Validate(profile);
        }
        if (bundle.Assistants.Select(p => p.AdapterId).Distinct().Count() != bundle.Assistants.Count)
            throw new InvalidDataException("文件包含重复的助手配置");
    }

    // Prepare every assistant before writing any file or changing Hub settings.
    public static AssistantBundleImport Import(string json, IEnumerable<AssistantLocation> locations, IReadOnlyCollection<string>? adapterIds = null)
    {
        var bundle = Read(json);
        var selected = adapterIds is null ? bundle.Assistants : bundle.Assistants.Where(p => adapterIds.Contains(p.AdapterId)).ToList();
        if (selected.Count == 0 || (adapterIds is not null && adapterIds.Any(id => !selected.Any(p => p.AdapterId == id))))
            throw new InvalidDataException("请选择文件中包含的助手");
        var targets = locations.ToDictionary(l => l.Game.AdapterId);
        var pending = new List<(string Full, string Content)>();
        foreach (var profile in selected)
        {
            if (!targets.TryGetValue(profile.AdapterId, out var location)) throw new InvalidDataException("缺少目标助手：" + profile.ToolName);
            pending.AddRange(AssistantProfileService.Prepare(profile, location.Game, location.Root));
        }
        var result = AssistantProfileService.WriteFiles(pending);
        foreach (var profile in selected) AssistantProfileService.Apply(profile, targets[profile.AdapterId].Game);
        return new(selected.Count, result.FilesWritten, result.BackupsMade);
    }
}
