using System.Text.Json;
using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Services;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace GameDailyHub.Tests;

public sealed class AssistantBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameDailyHub-test-" + Guid.NewGuid());

    private string DirectoryFor(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string root, string path, string content)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static AssistantLocation Location(string adapter, string? root)
        => new(GameCatalog.Games.Single(g => g.AdapterId == adapter).Create(), root);

    [Fact]
    public void ZzzIncludesInstancesGroupsAndCustomBattleAndKeepsLocalPaths()
    {
        var source = DirectoryFor("source"); var target = DirectoryFor("target");
        Write(source, "config/one_dragon.yml", "instance_list:\n- idx: 1\n  active: true\n- idx: 2\n  active: false\n");
        Write(source, "config/01/game_account.yml", "game_path: 'C:\\Source\\Client.exe'\naccount: sample-user\n");
        Write(source, "config/02/game_account.yml", "game_path: 'C:\\Other\\Client.exe'\n");
        Write(source, "config/01/app_groups/daily.yml", "apps:\n- coffee\n- charge_plan\n");
        Write(source, "config/auto_battle/custom.yml", "operations:\n- op_name: attack\n  data: [1, 2]\n");
        Write(source, "config/auto_battle/default.sample.yml", "sample: true\n");
        Write(source, "config/auto_battle/default.merged.yml", "merged: true\n");
        Write(source, "config/01/run_record/daily.yml", "completed: true\n");
        Write(source, "config/project.yml", "system: true\n");
        Write(source, "config/tool.py", "print('excluded')");
        Write(target, "config/01/game_account.yml", "game_path: 'D:\\Local\\Client.exe'\n");
        var json = AssistantProfileService.Export(Location("zzz", source).Game, source);
        Assert.Equal(5, AssistantProfileService.Read(json).Files.Count);
        var result = AssistantProfileService.Import(json, Location("zzz", target).Game, target);
        Assert.Equal(new(5, 1), result);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(target, "config/one_dragon.yml"))));
        Assert.Equal(2, ((YamlSequenceNode)((YamlMappingNode)yaml.Documents[0].RootNode)["instance_list"]).Children.Count);
        Assert.Contains("D:\\Local\\Client.exe", File.ReadAllText(Path.Combine(target, "config/01/game_account.yml")));
        Assert.DoesNotContain("C:\\Other", File.ReadAllText(Path.Combine(target, "config/02/game_account.yml")));
        Assert.Contains("charge_plan", File.ReadAllText(Path.Combine(target, "config/01/app_groups/daily.yml")));
        Assert.Contains("attack", File.ReadAllText(Path.Combine(target, "config/auto_battle/custom.yml")));
    }

    [Fact]
    public void BundleRoundTripRestoresAllAssistantsAndDailySettings()
    {
        string[] adapters = ["bettergi", "march7th", "okww", "zzz", "maaend"];
        string[] paths = ["User/config.json", "config.yaml", "configs/daily.json", "config/01/game.yml", "config/mxu-MaaEnd.json"];
        var sources = adapters.Select(a => Location(a, DirectoryFor("source-" + a))).ToArray();
        var targets = adapters.Select(a => Location(a, DirectoryFor("target-" + a))).ToArray();
        for (var i = 0; i < adapters.Length; i++)
        {
            Write(sources[i].Root!, paths[i], paths[i].EndsWith("json") ? "{\"task\":true}" : "task: true\n");
            Write(targets[i].Root!, paths[i], paths[i].EndsWith("json") ? "{}" : "task: false\n");
            sources[i].Game.Daily.TimeoutMinutes = 25 + i;
            targets[i].Game.Daily.WorkingDirectory = "local-directory";
        }
        var exported = AssistantBundleService.Export(sources);
        Assert.Equal(adapters.Length, exported.AssistantsExported); Assert.Empty(exported.Skipped);
        var result = AssistantBundleService.Import(exported.Json, targets.Reverse());
        Assert.Equal(new(adapters.Length, adapters.Length, adapters.Length), result);
        for (var i = 0; i < adapters.Length; i++)
        {
            Assert.Contains("true", File.ReadAllText(Path.Combine(targets[i].Root!, paths[i])));
            Assert.Equal(25 + i, targets[i].Game.Daily.TimeoutMinutes);
            Assert.Equal("local-directory", targets[i].Game.Daily.WorkingDirectory);
        }
    }

    [Fact]
    public void ExportListsMissingAndEmptyAssistantsWithoutAddingEmptyProfiles()
    {
        var source = DirectoryFor("source"); Write(source, "config.yaml", "task: true\n");
        var result = AssistantBundleService.Export([Location("march7th", source), Location("zzz", null), Location("okww", DirectoryFor("empty"))]);
        Assert.Equal(1, result.AssistantsExported);
        Assert.Equal(["绝区零一条龙", "OK-WW"], result.Skipped);
        Assert.Single(AssistantBundleService.Read(result.Json).Assistants);
        Assert.Throws<InvalidOperationException>(() => AssistantBundleService.Export([Location("zzz", null)]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidationFailureOrMissingTargetLeavesEveryAssistantUnchanged(bool invalidYaml)
    {
        var source = DirectoryFor("source"); Write(source, "config.yaml", "task: true\n");
        var target = DirectoryFor("target"); Write(target, "config.yaml", "task: false\n");
        var game = Location("march7th", target);
        var profile = AssistantProfileService.Read(AssistantProfileService.Export(Location("march7th", source).Game, source));
        profile.TimeoutMinutes = 10;
        var bundle = new AssistantBundle { Assistants = [profile, new() { AdapterId = "zzz", Files = [new() { Path = "config/one_dragon.yml", Content = "broken: [" }] }] };
        var json = JsonSerializer.Serialize(bundle, JsonDefaults.Options);
        var targets = invalidYaml ? new[] { game, Location("zzz", DirectoryFor("zzz")) } : new[] { game };
        Assert.Throws<InvalidDataException>(() => AssistantBundleService.Import(json, targets));
        Assert.Equal("task: false\n", File.ReadAllText(Path.Combine(target, "config.yaml")));
        Assert.Equal(60, game.Game.Daily.TimeoutMinutes);
        Assert.Empty(Directory.GetFiles(target, "*.bak"));
    }

    [Fact]
    public void FileReplacementFailureRollsBackEarlierFilesAndLeavesDailySettings()
    {
        var source = DirectoryFor("source"); var target = DirectoryFor("target");
        Write(source, "configs/a.json", "{\"enabled\":true}"); Write(source, "configs/b.json", "{\"enabled\":true}");
        Write(target, "configs/a.json", "{}"); Write(target, "configs/b.json", "{}");
        var from = Location("okww", source); from.Game.Daily.TimeoutMinutes = 10;
        var to = Location("okww", target);
        var json = AssistantBundleService.Export([from]).Json;
        using var locked = new FileStream(Path.Combine(target, "configs/b.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => AssistantBundleService.Import(json, [to]));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "configs/a.json")));
        Assert.Equal(60, to.Game.Daily.TimeoutMinutes);
        Assert.Empty(Directory.GetFiles(target, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void DuplicateAssistantAndUnsupportedBundleVersionsAreRejected()
    {
        var profile = new AssistantProfile { AdapterId = "zzz" };
        foreach (var bundle in new[] { new AssistantBundle { Assistants = [profile, profile] }, new AssistantBundle { SchemaVersion = 2, Assistants = [profile] } })
            Assert.Throws<InvalidDataException>(() => AssistantBundleService.Read(JsonSerializer.Serialize(bundle, JsonDefaults.Options)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportOnlyChangesSelectedAssistants(bool multiple)
    {
        string[] adapters = ["march7th", "okww", "zzz"];
        string[] paths = ["config.yaml", "configs/daily.json", "config/one_dragon.yml"];
        var sources = adapters.Select(a => Location(a, DirectoryFor("source-" + a))).ToArray();
        var targets = adapters.Select(a => Location(a, DirectoryFor("target-" + a))).ToArray();
        for (var i = 0; i < adapters.Length; i++)
        {
            Write(sources[i].Root!, paths[i], i == 1 ? "{\"enabled\":true}" : "enabled: true\n");
            Write(targets[i].Root!, paths[i], i == 1 ? "{}" : "enabled: false\n");
            sources[i].Game.Daily.TimeoutMinutes = 15;
        }
        var json = AssistantBundleService.Export(sources).Json;
        string[] ids = multiple ? ["march7th", "okww"] : ["march7th"];
        var result = AssistantBundleService.Import(json, targets.Where(t => ids.Contains(t.Game.AdapterId)), ids);
        Assert.Equal(ids.Length, result.AssistantsImported);
        for (var i = 0; i < adapters.Length; i++)
        {
            var selected = ids.Contains(adapters[i]);
            Assert.Equal(selected ? 15 : 60, targets[i].Game.Daily.TimeoutMinutes);
            Assert.Equal(selected, Directory.GetFiles(targets[i].Root!, "*.bak", SearchOption.AllDirectories).Length > 0);
            if (!selected) Assert.Equal(i == 1 ? "{}" : "enabled: false\n", File.ReadAllText(Path.Combine(targets[i].Root!, paths[i])));
        }
    }

    [Fact]
    public void UnifiedImportAcceptsLegacySingleProfileAndRejectsEmptyOrAbsentSelection()
    {
        var source = DirectoryFor("source"); Write(source, "config.yaml", "enabled: true\n");
        var target = Location("march7th", DirectoryFor("target"));
        var json = AssistantProfileService.Export(Location("march7th", source).Game, source);
        Assert.Throws<InvalidDataException>(() => AssistantBundleService.Import(json, [target], []));
        Assert.Throws<InvalidDataException>(() => AssistantBundleService.Import(json, [target], ["zzz"]));
        Assert.Empty(Directory.GetFiles(target.Root!));
        var result = AssistantBundleService.Import(json, [target], ["march7th"]);
        Assert.Equal(1, result.AssistantsImported);
        Assert.Contains("true", File.ReadAllText(Path.Combine(target.Root!, "config.yaml")));
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(_root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("GameDailyHub-test-"))
            throw new InvalidOperationException("测试清理路径越界");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
