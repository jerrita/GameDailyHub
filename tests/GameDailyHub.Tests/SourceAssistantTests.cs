using System.Diagnostics;
using System.Text.Json;
using GameDailyHub.Core.Models;
using GameDailyHub.Core.Orchestration;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

[Collection("Process")]
public sealed class SourceAssistantTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameDailyHub-source-tests-" + Guid.NewGuid());
    private readonly ProcessService _processes = new();
    private SourceAssistantService Sources => new(_processes, _root);
    private GameConfig CreateSource(string adapterId)
    {
        var game = new GameConfig { AdapterId = adapterId, ToolRunMode = AssistantRunMode.Source };
        game.ToolSourceDirectory = Path.Combine(_root, "source workspace", adapterId);
        var directory = Sources.DirectoryFor(game);
        var scripts = Path.GetDirectoryName(Sources.PythonPath(game))!;
        Directory.CreateDirectory(scripts);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            if (Path.GetExtension(file) is ".dll" or ".json") File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe"), Sources.PythonPath(game));
        File.WriteAllText(Path.Combine(directory, "app.py"), "");
        File.WriteAllText(Path.Combine(directory, "main.py"), "");
        foreach (var gui in new[] { true, false })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sources.EntryPath(game, gui))!);
            File.WriteAllText(Sources.EntryPath(game, gui), "");
        }
        File.WriteAllText(Path.Combine(directory, "config.yaml"), "after_finish: None\npause_after_success: false\nexit_after_failure: true\nscheduled_run_enable: false\nscheduled_tasks: []\n");
        return game;
    }
    [Theory]
    [InlineData("march7th", "March7thAssistant")]
    [InlineData("okww", "ok-wuthering-waves")]
    [InlineData("zzz", "ZenlessZoneZero-OneDragon")]
    public async Task SourceSettingsPersistAndExplicitDirectoryOverridesDataRoot(string adapter, string folder)
    {
        Directory.CreateDirectory(_root);
        var game = new GameConfig { AdapterId = adapter, ToolRunMode = AssistantRunMode.Source };
        Assert.Equal(Path.Combine(_root, "assistants", folder), Sources.DirectoryFor(game));
        game.ToolSourceDirectory = Path.Combine(_root, "custom source");
        var config = new AppConfig { Games = [game] };
        await new ConfigurationStore(_root).SaveAsync(config);
        var loaded = Assert.Single((await new ConfigurationStore(_root).LoadAsync()).Games, game => game.AdapterId == adapter);
        Assert.Equal(AssistantRunMode.Source, loaded.ToolRunMode);
        Assert.Equal(game.ToolSourceDirectory, Sources.DirectoryFor(loaded));
        Assert.Equal(AssistantRunMode.Binary, JsonSerializer.Deserialize<GameConfig>("{}")!.ToolRunMode);
    }
    [Theory]
    [InlineData("march7th", "main.py")]
    [InlineData("zzz", "zzz_application_launcher.py")]
    public async Task SourceDailyUsesCliEntryAndSourceWorkingDirectoryThroughQueue(string adapter, string entry)
    {
        var game = CreateSource(adapter);
        game.ToolPath = Path.Combine(_root, "unused-binary.exe");
        game.Daily.Trigger = TriggerMode.CustomCommand;
        game.Daily.Arguments = "fixture exit";
        game.Daily.Completion = CompletionMode.LogRules;
        game.Daily.StartPattern = "^START$";
        game.Daily.SuccessPattern = "^DONE$";
        game.Daily.FailurePattern = "^FAIL$";
        var journal = new MemoryJournal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var batch = await new QueueEngine(new AdapterRegistry(_processes, Sources), journal)
            .RunAsync([new(new() { GameId = game.Id }, game)], timeout.Token);
        Assert.Equal(1, batch.SuccessCount);
        var evidence = Assert.Single(batch.Attempts).Evidence;
        Assert.Contains("ENTRY=" + entry, evidence);
        Assert.Contains("CWD=" + Sources.DirectoryFor(game), evidence);
        if (adapter == "zzz")
        {
            Assert.Contains("PYTHONPATH=" + Path.Combine(Sources.DirectoryFor(game), "src"), evidence);
            Assert.Contains("PYTHONIOENCODING=utf-8", evidence);
        }
    }
    [Theory]
    [InlineData("march7th", "app.py")]
    [InlineData("okww", "main.py")]
    [InlineData("zzz", "app.py")]
    public async Task GuiReusesSourceInstanceAndMaintenanceRefusesRunningHelper(string adapter, string entry)
    {
        var game = CreateSource(adapter);
        game.Normal.Target = LaunchTarget.Assistant;
        var tool = new AdapterRegistry(_processes, Sources).Get(adapter);
        Assert.True((await tool.ValidateAsync(game, false, default)).IsValid);
        var launch = await tool.LaunchNormalAsync(game, default);
        Assert.True(launch.Success);
        using var process = Process.GetProcessById(launch.ProcessId!.Value);
        try
        {
            Assert.False(process.HasExited);
            var marker = Path.Combine(Sources.DirectoryFor(game), "fixture-entry.txt");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(marker)) await Task.Delay(20, timeout.Token);
            Assert.Equal(entry, await File.ReadAllTextAsync(marker, timeout.Token));
            var repeated = await tool.LaunchNormalAsync(game, default);
            Assert.True(repeated.Reused);
            Assert.Equal(launch.ProcessId, repeated.ProcessId);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Sources.PrepareAsync(game, true, true, _ => { }, default));
            Assert.Contains("正在运行", error.Message);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
    [Fact]
    public void SourceValidationRequiresEnvironmentAndRejectsUnsupportedAssistant()
    {
        var game = new GameConfig { AdapterId = "okww", ToolRunMode = AssistantRunMode.Source };
        Assert.Equal(2, Sources.Validate(game, true).Count);
        var config = AppConfig.CreateDefault();
        config.Games[0].ToolRunMode = AssistantRunMode.Source;
        Assert.Throws<InvalidDataException>(() => ConfigurationStore.Validate(config));
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("GameDailyHub-source-tests-"))
            throw new InvalidOperationException("测试清理路径越界");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
