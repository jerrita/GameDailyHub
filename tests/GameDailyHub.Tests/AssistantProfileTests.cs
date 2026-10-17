using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class AssistantProfileTests
{
    private static GameConfig Game(string adapter = "march7th") => new() { AdapterId = adapter, ToolPath = @"C:\source-machine\tool.exe", GamePath = @"C:\source-machine\game.exe" };

    [Fact]
    public void RoundTripCopiesAutomationButNeverMachinePaths()
    {
        var source = Game();
        source.Daily = new() { Trigger = TriggerMode.CustomCommand, Arguments = "--task daily", ProfileName = "flow", Completion = CompletionMode.LogRules, StartPattern = "a", SuccessPattern = "b", FailurePattern = "c", TimeoutMinutes = 30, RetryCount = 2, ContinueOnFailure = false, LogPath = @"C:\source-machine\log.txt", WorkingDirectory = @"C:\source-machine" };
        var json = AssistantProfileService.Export(source);
        Assert.DoesNotContain("source-machine", json);

        var target = Game(); target.ToolPath = @"D:\other\tool.exe"; target.Daily.LogPath = @"D:\other\log.txt";
        AssistantProfileService.Import(json, target);
        Assert.Equal(TriggerMode.CustomCommand, target.Daily.Trigger);
        Assert.Equal("--task daily", target.Daily.Arguments);
        Assert.Equal(CompletionMode.LogRules, target.Daily.Completion);
        Assert.Equal(("a", "b", "c"), (target.Daily.StartPattern, target.Daily.SuccessPattern, target.Daily.FailurePattern));
        Assert.Equal((30d, 2, false), (target.Daily.TimeoutMinutes, target.Daily.RetryCount, target.Daily.ContinueOnFailure));
        Assert.Equal(@"D:\other\tool.exe", target.ToolPath);
        Assert.Equal(@"D:\other\log.txt", target.Daily.LogPath);
    }

    [Fact]
    public void ImportRejectsOtherAssistantWithoutChangingGame()
    {
        var json = AssistantProfileService.Export(Game("bettergi"));
        var target = Game(); target.Daily.Arguments = "keep";
        Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(json, target));
        Assert.Equal("keep", target.Daily.Arguments);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"kind\":\"other\"}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":1,\"adapterId\":\"march7th\",\"timeoutMinutes\":0}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":1,\"adapterId\":\"march7th\",\"retryCount\":99}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":2,\"adapterId\":\"march7th\"}")]
    public void InvalidFilesAreRejected(string json) => Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(json, Game()));
}
