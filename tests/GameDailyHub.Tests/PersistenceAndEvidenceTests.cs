using System.Text;
using System.Text.Json;
using GameDailyHub.Core.Models;
using GameDailyHub.Core.Services;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class PersistenceAndEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GameDailyHub-test-" + Guid.NewGuid());
    public PersistenceAndEvidenceTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_directory);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("GameDailyHub-test-")) throw new InvalidOperationException("测试清理路径越界");
        Directory.Delete(resolved, true);
    }
    [Fact]
    public async Task ConfigSurvivesRestartAndProfilesAreSeparate()
    {
        var store = new ConfigurationStore(_directory); var config = AppConfig.CreateDefault();
        config.Games[0].Normal.Arguments = "ordinary"; config.Games[0].Daily.Arguments = "automation";
        await store.SaveAsync(config); var loaded = await new ConfigurationStore(_directory).LoadAsync();
        Assert.Equal("ordinary", loaded.Games[0].Normal.Arguments); Assert.Equal("automation", loaded.Games[0].Daily.Arguments); Assert.Equal(4, loaded.Tasks.Count);
    }
    [Fact]
    public async Task ExistingConfigurationGetsSupportedGamesWithoutResettingPathsOrQueue()
    {
        var store = new ConfigurationStore(_directory);
        var config = AppConfig.CreateDefault();
        var missing = config.Games.Single(game => game.AdapterId == "zzz");
        config.Games.Remove(missing);
        config.Tasks.RemoveAll(task => task.GameId == missing.Id);
        config.Games[0].ToolPath = Path.Combine(_directory, "saved-assistant.exe");
        config.Games[0].GamePath = Path.Combine(_directory, "saved-client.exe");
        config.Games[0].Daily.ProfileName = "saved-flow";
        config.Games[0].Daily.TimeoutMinutes = 25;
        config.Tasks.Reverse();
        config.Tasks[0].Enabled = false;
        config.Schedule.Enabled = true;
        var originalGame = config.Games[0];
        var originalTasks = config.Tasks.Select(task => (task.Id, task.GameId, task.Enabled)).ToArray();
        var unsupported = new GameConfig { AdapterId = "retired-adapter", Name = "previous-entry" };
        config.Games.Add(unsupported);
        config.Tasks.Add(new() { GameId = unsupported.Id });
        var originalJson = JsonSerializer.Serialize(config, JsonDefaults.Options);
        await File.WriteAllTextAsync(store.ConfigPath, originalJson);

        var loaded = await store.LoadAsync();

        Assert.Null(store.RecoveryMessage);
        Assert.Equal(new[] { "原神", "崩坏：星穹铁道", "鸣潮", "绝区零" }, loaded.Games.Select(game => game.Name));
        Assert.Equal(originalGame.Id, loaded.Games[0].Id);
        Assert.Equal(originalGame.ToolPath, loaded.Games[0].ToolPath);
        Assert.Equal(originalGame.GamePath, loaded.Games[0].GamePath);
        Assert.Equal("saved-flow", loaded.Games[0].Daily.ProfileName);
        Assert.Equal(25, loaded.Games[0].Daily.TimeoutMinutes);
        Assert.Equal(originalTasks, loaded.Tasks.Take(3).Select(task => (task.Id, task.GameId, task.Enabled)));
        var zzz = loaded.Games.Single(game => game.AdapterId == "zzz");
        var addedTask = Assert.Single(loaded.Tasks, task => task.GameId == zzz.Id);
        Assert.True(loaded.Schedule.Enabled);
        Assert.DoesNotContain(loaded.Tasks, task => task.GameId == unsupported.Id);

        await store.SaveAsync(loaded);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(store.ConfigPath + ".bak"));
        var again = await store.LoadAsync();
        Assert.Equal(zzz.Id, again.Games.Single(game => game.AdapterId == "zzz").Id);
        Assert.Equal(addedTask.Id, Assert.Single(again.Tasks, task => task.GameId == zzz.Id).Id);
        Assert.Equal(4, again.Tasks.Count);
    }

    [Fact]
    public async Task ImportRestoresMissingSupportedGameAndKeepsExistingTaskChoices()
    {
        var config = AppConfig.CreateDefault();
        var zzz = config.Games.Single(game => game.AdapterId == "zzz");
        config.Games.Remove(zzz);
        config.Tasks.RemoveAll(task => task.GameId == zzz.Id || task.GameId == config.Games[0].Id);
        config.Schedule.Enabled = true;
        var importPath = Path.Combine(_directory, "prior-config.json");
        await File.WriteAllTextAsync(importPath, JsonSerializer.Serialize(config, JsonDefaults.Options));

        var imported = await new ConfigurationStore(_directory).ImportAsync(importPath);

        Assert.False(imported.Schedule.Enabled);
        Assert.Equal(4, imported.Games.Count);
        Assert.DoesNotContain(imported.Tasks, task => task.GameId == config.Games[0].Id);
        Assert.Single(imported.Tasks, task => task.GameId == imported.Games.Single(game => game.AdapterId == "zzz").Id);
    }

    [Fact]
    public async Task SupportedEntriesAreUniqueAndUseCatalogNames()
    {
        var config = AppConfig.CreateDefault();
        var original = config.Games[0];
        original.Name = "prior-label";
        original.ToolName = "prior-tool-label";
        var duplicate = new GameConfig { AdapterId = original.AdapterId };
        config.Games.Add(duplicate);
        var task = new QueueTaskConfig { GameId = duplicate.Id, Name = "saved-task", Enabled = false };
        config.Tasks.Add(task);

        await new ConfigurationStore(_directory).SaveAsync(config);

        Assert.Equal(4, config.Games.Count);
        Assert.Same(original, Assert.Single(config.Games, game => game.AdapterId == "bettergi"));
        Assert.Equal(("原神", "BetterGI"), (original.Name, original.ToolName));
        Assert.Equal(original.Id, task.GameId);
        Assert.Equal("saved-task", task.Name);
        Assert.False(task.Enabled);
    }
    [Theory]
    [InlineData(LaunchTarget.Assistant)]
    [InlineData(LaunchTarget.Launcher)]
    public async Task PersistedNormalSettingsApplyOnlyToTheirIntendedDestination(LaunchTarget target)
    {
        var config = AppConfig.CreateDefault();
        config.Games[0].Normal = new() { Target = target, Arguments = "destination-argument", WorkingDirectory = "destination-directory" };
        config.Games[0].Daily.Arguments = "daily-argument";
        var store = new ConfigurationStore(_directory);
        await File.WriteAllTextAsync(store.ConfigPath, JsonSerializer.Serialize(config, JsonDefaults.Options));

        var loaded = await store.LoadAsync();

        Assert.Equal(LaunchTarget.Game, loaded.Games[0].Normal.Target);
        Assert.Empty(loaded.Games[0].Normal.Arguments);
        Assert.Empty(loaded.Games[0].Normal.WorkingDirectory);
        Assert.Equal("daily-argument", loaded.Games[0].Daily.Arguments);
    }
    [Fact]
    public void March7thFindsExistingCompanionWithoutOverwritingUserChoice()
    {
        var assistant = Path.Combine(_directory, "March7th Assistant.exe");
        var launcher = Path.Combine(_directory, "March7th Launcher.exe");
        File.WriteAllText(assistant, "");
        var game = new GameConfig { AdapterId = "march7th", ToolPath = assistant };
        ConfigurationSuggestions.Apply(game);
        Assert.Empty(game.ToolGuiPath);
        File.WriteAllText(launcher, "");
        ConfigurationSuggestions.Apply(game);
        Assert.Equal(launcher, game.ToolGuiPath);
        game.ToolGuiPath = "custom-launcher.exe";
        ConfigurationSuggestions.Apply(game);
        Assert.Equal("custom-launcher.exe", game.ToolGuiPath);
    }
    [Theory]
    [InlineData("bettergi", "BetterGI", "BetterGI.exe")]
    [InlineData("march7th", "March7thAssistant", "March7th Assistant.exe")]
    [InlineData("okww", "ok-ww", "ok-ww.exe")]
    [InlineData("zzz", "ZenlessZoneZero-OneDragon", "OneDragon-RuntimeLauncher.exe")]
    [InlineData("zzz", "ZenlessZoneZero-OneDragon", "OneDragon-Launcher.exe")]
    public void BlankAssistantPathUsesExistingDefault(string adapter, string folder, string executable)
    {
        var directory = Path.Combine(_directory, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, executable);
        File.WriteAllText(path, "");
        var launcher = Path.Combine(directory, "March7th Launcher.exe");
        if (adapter == "march7th") File.WriteAllText(launcher, "");
        var game = new GameConfig
        {
            AdapterId = adapter,
            ToolPath = " "
        };
        ConfigurationSuggestions.Apply(game, [_directory]);
        Assert.Equal(path, game.ToolPath);
        Assert.Equal(adapter == "march7th" ? launcher : "", game.ToolGuiPath);
    }
    [Fact]
    public void ExplicitAssistantPathWinsEvenWhenMissingAndDefaultExists()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "BetterGI"));
        File.WriteAllText(Path.Combine(_directory, "BetterGI", "BetterGI.exe"), "");
        var game = new GameConfig
        {
            AdapterId = "bettergi",
            ToolPath = Path.Combine(_directory, "custom.exe")
        };
        ConfigurationSuggestions.Apply(game, [_directory]);
        Assert.Equal(Path.Combine(_directory, "custom.exe"), game.ToolPath);
    }
    [Fact]
    public void DefaultSearchUsesFirstExistingInstallationAndLeavesOtherToolsAlone()
    {
        var roots = new[] { Path.Combine(_directory, "missing"), Path.Combine(_directory, "first"), Path.Combine(_directory, "second") };
        foreach (var root in roots.Skip(1))
        {
            Directory.CreateDirectory(Path.Combine(root, "ok-ww"));
            File.WriteAllText(Path.Combine(root, "ok-ww", "ok-ww.exe"), "");
        }
        var game = new GameConfig { AdapterId = "okww" };
        ConfigurationSuggestions.Apply(game, roots);
        Assert.Equal(Path.Combine(roots[1], "ok-ww", "ok-ww.exe"), game.ToolPath);
        game = new GameConfig { AdapterId = "zzz" };
        ConfigurationSuggestions.Apply(game, roots);
        Assert.Empty(game.ToolPath);
        game.AdapterId = "bettergi";
        ConfigurationSuggestions.Apply(game, roots);
        Assert.Empty(game.ToolPath);
    }
    [Theory]
    [InlineData("bettergi", @"miHoYo Launcher\games\Genshin Impact Game\YuanShen.exe")]
    [InlineData("bettergi", @"HoYoPlay\games\Genshin Impact game\GenshinImpact.exe")]
    [InlineData("bettergi", @"Genshin Impact\Genshin Impact Game\YuanShen.exe")]
    [InlineData("march7th", @"miHoYo Launcher\games\Star Rail Game\StarRail.exe")]
    [InlineData("march7th", @"HoYoPlay\games\Star Rail Games\StarRail.exe")]
    [InlineData("march7th", @"Star Rail\Game\StarRail.exe")]
    [InlineData("okww", @"Wuthering Waves\Wuthering Waves Game\Wuthering Waves.exe")]
    public async Task BlankGamePathUsesInstalledClientWithoutChangingLaunchOrDailySettings(string adapter, string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        var game = new GameConfig
        {
            AdapterId = adapter,
            GamePath = " ",
            ToolPath = "custom-assistant.exe",
            Normal = new() { Arguments = "client-argument" },
            Daily = new() { Arguments = "daily-argument" }
        };

        ConfigurationSuggestions.Apply(game, [_directory]);
        var validation = await new AdapterRegistry(new()).Get(adapter).ValidateAsync(game, false, default);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        Assert.Equal(path, game.NormalPath);
        Assert.Equal("client-argument", game.Normal.Arguments);
        Assert.Equal("custom-assistant.exe", game.ToolPath);
        Assert.Equal("daily-argument", game.Daily.Arguments);
    }
    [Fact]
    public void GameDefaultSearchRespectsExplicitPathsAndInstallationOrder()
    {
        var roots = new[] { Path.Combine(_directory, "missing"), Path.Combine(_directory, "first"), Path.Combine(_directory, "second") };
        const string relativePath = @"Wuthering Waves\Wuthering Waves Game\Wuthering Waves.exe";
        foreach (var root in roots.Skip(1))
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        var game = new GameConfig { AdapterId = "okww", GamePath = Path.Combine(_directory, "custom.exe") };
        ConfigurationSuggestions.Apply(game, roots);
        Assert.Equal(Path.Combine(_directory, "custom.exe"), game.GamePath);

        game.GamePath = "";
        ConfigurationSuggestions.Apply(game, roots);
        Assert.Equal(Path.Combine(roots[1], relativePath), game.GamePath);
        foreach (var adapter in new[] { "zzz", "bettergi", "march7th" })
        {
            game = new() { AdapterId = adapter };
            ConfigurationSuggestions.Apply(game, roots);
            Assert.Empty(game.GamePath);
        }
    }
    [Fact]
    public async Task CorruptionRestoresBackupAndPreservesDamagedFile()
    {
        var store = new ConfigurationStore(_directory); var config = AppConfig.CreateDefault();
        await store.SaveAsync(config); config.Games[0].Daily.ProfileName = "changed-profile"; await store.SaveAsync(config);
        await File.WriteAllTextAsync(store.ConfigPath, "{");
        var loaded = await store.LoadAsync(); Assert.Empty(loaded.Games[0].Daily.ProfileName);
        Assert.NotNull(store.RecoveryMessage); Assert.Single(Directory.GetFiles(_directory, "*.damaged-*"));
    }
    [Fact]
    public async Task ImportRejectsUnknownVersionAndDisablesSchedule()
    {
        var store = new ConfigurationStore(_directory); var config = AppConfig.CreateDefault(); config.Schedule.Enabled = true;
        var path = Path.Combine(_directory, "import.json"); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, JsonDefaults.Options));
        Assert.False((await store.ImportAsync(path)).Schedule.Enabled);
        config.SchemaVersion = 99; await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, JsonDefaults.Options));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(path));
    }
    [Fact]
    public void BrokenTaskReferencesAndBadLimitsAreRejected()
    {
        var config = AppConfig.CreateDefault(); config.Tasks[0].GameId = Guid.NewGuid(); Assert.Throws<InvalidDataException>(() => ConfigurationStore.Validate(config));
        config = AppConfig.CreateDefault(); config.Games[0].Daily.TimeoutMinutes = double.NaN; Assert.Throws<InvalidDataException>(() => ConfigurationStore.Validate(config));
    }
    [Fact]
    public async Task TailIgnoresOldSuccessAndHandlesUtf8SplitAndTruncation()
    {
        var path = Path.Combine(_directory, "tool.log"); await File.WriteAllTextAsync(path, "OLD SUCCESS\n");
        using var tail = new LogTail(path); Assert.Empty(await tail.ReadAsync(default));
        var bytes = Encoding.UTF8.GetBytes("完成\n");
        using (var stream = new FileStream(path, FileMode.Append)) await stream.WriteAsync(bytes.AsMemory(0, 2));
        Assert.Empty(await tail.ReadAsync(default));
        using (var stream = new FileStream(path, FileMode.Append)) await stream.WriteAsync(bytes.AsMemory(2));
        Assert.Equal("完成", Assert.Single(await tail.ReadAsync(default)));
        await File.WriteAllTextAsync(path, "x\n"); Assert.Equal("x", Assert.Single(await tail.ReadAsync(default))); Assert.True(tail.SawDiscontinuity);
    }
    [Fact]
    public async Task HistoryCanBeFilteredByGameAndBatch()
    {
        var journal = new ExecutionJournal(_directory); var game = Guid.NewGuid(); var batch = Guid.NewGuid();
        await journal.SaveRunAsync(new(batch, Guid.NewGuid(), game, Guid.NewGuid(), "test", "daily", 1, DateTimeOffset.Now, DateTimeOffset.Now, RunState.Failed, "error", 1, "evidence"));
        Assert.Single(await journal.ReadRunsAsync(game, batch)); Assert.Empty(await journal.ReadRunsAsync(Guid.NewGuid()));
    }
    [Fact]
    public async Task InterruptedRunIsRecoveredAsUnknown()
    {
        var journal = new ExecutionJournal(_directory);
        await journal.SaveRunAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "daily", 1, DateTimeOffset.Now, DateTimeOffset.Now, RunState.Starting, "running", null, ""));
        await journal.RecoverInterruptedRunsAsync();
        Assert.Equal(RunState.Unknown, Assert.Single(await journal.ReadRunsAsync()).State);
    }
    [Fact]
    public async Task TruncateAndRegrowCannotReusePriorCursorEvidence()
    {
        var path = Path.Combine(_directory, "tool.log"); await File.WriteAllTextAsync(path, "old contents\n");
        using var tail = new LogTail(path); await File.WriteAllTextAsync(path, "START\nDONE\nnew longer contents\n");
        await tail.ReadAsync(default); Assert.True(tail.SawDiscontinuity);
    }
    [Fact]
    public void ExitZeroIsNeverSufficientEvidence()
    {
        foreach (var adapter in new[] { "bettergi", "march7th", "okww", "zzz" }) Assert.Equal(RunState.Unknown, new EvidenceMonitor(adapter, new(), 5).Evaluate(true, 0)!.State);
    }
    [Fact]
    public void BetterGiEndAloneRequiresConfirmationAndLaterFailureWins()
    {
        var monitor = new EvidenceMonitor("bettergi", new(), 5);
        monitor.Accept("启用一条龙配置：默认"); monitor.Accept("一条龙和配置组任务结束");
        Assert.Equal(RunState.Unknown, monitor.Evaluate(false, null)!.State);
        monitor.Accept("[ERR] 合成树脂执行异常"); Assert.Equal(RunState.Failed, monitor.Evaluate(false, null)!.State);
    }
    [Fact]
    public void BetterGiRejectsOtherInstanceLogEntries()
    {
        var monitor = new EvidenceMonitor("bettergi", new(), 5);
        monitor.Accept("[10:10:10.000] [INF] [Primary:S1:P6:T1]", true);
        monitor.Accept("启用一条龙配置：默认", true); monitor.Accept("一条龙和配置组任务结束", true);
        Assert.Null(monitor.Evaluate(false, null));
    }
    [Fact]
    public void March7thIntermediateCompletedIsNotTerminal()
    {
        var monitor = new EvidenceMonitor("march7th", new(), 5);
        monitor.Accept("开始运行"); monitor.Accept("──── 完成 ────"); Assert.Equal(RunState.Unknown, monitor.Evaluate(true, 0)!.State);
        monitor.Accept("停止运行"); monitor.Accept("==== 完成 ===="); Assert.Equal(RunState.Success, monitor.Evaluate(true, 0)!.State);
    }
    [Fact]
    public void OkWwFailureTakesPrecedenceOverFinalCompletionLine()
    {
        var monitor = new EvidenceMonitor("okww", new(), 5); monitor.Accept("GardenTask Failed"); monitor.Accept("Daily Task Completed");
        Assert.Equal(RunState.Failed, monitor.Evaluate(false, null)!.State);
    }
    [Fact]
    public void OkWwTracebackSourceCodeCannotMasqueradeAsCompletion()
    {
        var monitor = new EvidenceMonitor("okww", new(), 5);
        monitor.Accept("    self.log_info('Daily Task Completed', notify=True)");
        Assert.Null(monitor.Evaluate(false, null));
        monitor.Accept("Traceback (most recent call last)"); Assert.Equal(RunState.Failed, monitor.Evaluate(true, 0)!.State);
    }
    [Fact]
    public async Task NullConfigurationObjectsAreRejectedAsDamagedData()
    {
        var path = Path.Combine(_directory, "null.json"); await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"games\":[null],\"tasks\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationStore.ReadValidatedAsync(path));
    }
    [Fact]
    public void LogRulesNeedStartAndRootExit()
    {
        var profile = new DailyTaskProfile { Completion = CompletionMode.LogRules, StartPattern = "^START$", SuccessPattern = "^DONE$", FailurePattern = "^FAIL$" };
        var monitor = new EvidenceMonitor("zzz", profile, 5); monitor.Accept("DONE"); Assert.Null(monitor.Evaluate(false, null));
        monitor.Accept("START"); monitor.Accept("DONE"); Assert.Null(monitor.Evaluate(false, null)); Assert.Equal(RunState.Success, monitor.Evaluate(true, 0)!.State);
    }
    [Fact]
    public void ScheduleRunsOnceAndSkipsStartupAndWakeCatchup()
    {
        var config = new ScheduleConfig { Enabled = true, Time = "08:00", TimeZoneId = "China Standard Time" };
        var evaluator = new ScheduleEvaluator(); var date = new DateTimeOffset(2026, 10, 17, 7, 59, 59, TimeSpan.FromHours(8));
        Assert.False(evaluator.ShouldRun(config, date)); Assert.True(evaluator.ShouldRun(config, date.AddSeconds(1))); Assert.False(evaluator.ShouldRun(config, date.AddSeconds(2)));
        var next = new ScheduleEvaluator(); Assert.False(next.ShouldRun(config, date.AddDays(1))); Assert.False(next.ShouldRun(config, date.AddDays(1).AddHours(1)));
    }
    [Theory]
    [InlineData("")]
    [InlineData("C:\\path with space\\")]
    [InlineData("a\"b")]
    [InlineData("trailing space ")]
    public void WindowsArgumentQuotingRoundTrips(string value) => Assert.Equal(value, Assert.Single(CommandArguments.Parse(CommandArguments.Quote(value))));
}
