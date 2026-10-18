using GameDailyHub.Core.Models;
using GameDailyHub.Core.Orchestration;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

[Collection("Process")]
public sealed class AdapterIntegrationTests
{
    [Theory]
    [InlineData("bettergi", "BetterGI.exe")]
    [InlineData("march7th", "March7th Launcher.exe")]
    [InlineData("okww", "ok-ww.exe")]
    [InlineData("zzz", "OneDragon-RuntimeLauncher.exe")]
    [InlineData("zzz", "OneDragon-Launcher.exe")]
    public async Task OpeningAssistantDoesNotRequireDailyConfiguration(string adapterId, string executable)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameDailyHub-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, executable);
            File.WriteAllText(path, "");
            var game = new GameConfig
            {
                AdapterId = adapterId,
                ToolPath = path,
                Normal = new() { Target = LaunchTarget.Assistant }
            };
            var validation = await new AdapterRegistry(new()).Get(adapterId).ValidateAsync(game, false, default);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ManualModeNeedsNoAutomaticEvidenceSetup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe");
        var game = new GameConfig { AdapterId = "bettergi", ToolPath = path, Daily = new() { Trigger = TriggerMode.Manual } };
        var adapter = new BetterGiAdapter(new());
        foreach (var completion in new[] { CompletionMode.AdapterEvidence, CompletionMode.LogRules })
        {
            game.Daily.Completion = completion;
            var validation = await adapter.ValidateAsync(game, true, default);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        }
    }
    [Fact]
    public async Task AdapterLaunchesFixtureAndWaitsForOutputEvidence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe");
        var game = new GameConfig
        {
            Name = "测试专用进程",
            AdapterId = "bettergi",
            ToolPath = path,
            Daily = new()
            {
                Trigger = TriggerMode.CustomCommand,
                Arguments = "fixture exit",
                Completion = CompletionMode.LogRules,
                StartPattern = "^START$",
                SuccessPattern = "^DONE$",
                FailurePattern = "^FAIL$"
            }
        };
        var journal = new MemoryJournal();
        var result = await new QueueEngine(new AdapterRegistry(new()), journal).RunAsync([new(new() { GameId = game.Id }, game)]);
        Assert.Equal(1, result.SuccessCount); Assert.Contains("DONE", Assert.Single(result.Attempts).Evidence);
    }
    [Fact]
    public async Task OrdinaryMarch7thCannotRunAssistantEvenWhenMislabelledAsGame()
    {
        var game = new GameConfig { AdapterId = "march7th", GamePath = "C:\\fake\\March7th Assistant.exe" };
        var validation = await new March7thAdapter(new()).ValidateAsync(game, false, default);
        Assert.Contains(validation.Errors, e => e.Contains("默认执行任务"));
    }
    [Fact]
    public async Task BetterGiNormalArgumentsCannotTriggerDragonThroughMislabelledTarget()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe");
        var game = new GameConfig { GamePath = path, ToolPath = path, Normal = new() { Arguments = "--startOneDragon" } };
        var validation = await new BetterGiAdapter(new()).ValidateAsync(game, false, default);
        Assert.Contains(validation.Errors, e => e.Contains("仅允许空参数"));
    }
}
