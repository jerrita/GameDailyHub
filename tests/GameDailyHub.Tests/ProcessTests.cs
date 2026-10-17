using System.Diagnostics;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

[CollectionDefinition("Process", DisableParallelization = true)] public sealed class ProcessCollection { }
[Collection("Process")]
public sealed class ProcessTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe");
    [Fact]
    public async Task JobTracksAndTerminatesChildrenWithoutTouchingUnrelatedNormalProcess()
    {
        var service = new ProcessService();
        using var unrelated = Process.Start(new ProcessStartInfo(Fixture, "fixture wait") { UseShellExecute = false, CreateNoWindow = true })!;
        await using var owned = service.StartOwned(Fixture, "fixture spawn", "");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int? child = null;
            while (child is null)
            {
                var line = await owned.Lines.Reader.ReadAsync(deadline.Token);
                if (line.StartsWith("CHILD=")) child = int.Parse(line[6..]);
            }
            Assert.True(owned.ActiveProcesses >= 2);
            Assert.True(service.IsSameProcess(owned.Identity));
            var wrongIdentity = owned.Identity with { CreatedFileTime = owned.Identity.CreatedFileTime + 1 };
            Assert.False(service.IsSameProcess(wrongIdentity));
            Assert.True((await owned.StopAsync(deadline.Token)).IsQuiescent);
            Assert.Equal(0u, owned.ActiveProcesses); Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) { unrelated.Kill(); await unrelated.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task NormalLaunchReusesExactExistingExecutable()
    {
        var service = new ProcessService();
        using var existing = Process.Start(new ProcessStartInfo(Fixture, "fixture wait") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var result = await service.LaunchNormalAsync(Fixture, "fixture wait", "", default);
            Assert.True(result.Reused); Assert.Equal(existing.Id, result.ProcessId);
        }
        finally { if (!existing.HasExited) { existing.Kill(); await existing.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task NormalLaunchStartsConfiguredExecutable()
    {
        var service = new ProcessService();
        var result = await service.LaunchNormalAsync(Fixture, "fixture wait", "", default);
        Assert.True(result.Success);
        Assert.False(result.Reused);
        using var started = Process.GetProcessById(result.ProcessId!.Value);
        try
        {
            Assert.Contains(service.FindRunning(Fixture), p => p.Pid == started.Id);
            Assert.False(started.HasExited);
        }
        finally { if (!started.HasExited) { started.Kill(); await started.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task CapturedOutputIsDrainedAfterRootExit()
    {
        await using var owned = new ProcessService().StartOwned(Fixture, "fixture exit", "");
        using var cts = new CancellationTokenSource(5000);
        while (!owned.HasExited) await Task.Delay(10, cts.Token);
        Assert.True(await owned.DrainOutputAsync(cts.Token));
        var lines = new List<string>(); while (owned.Lines.Reader.TryRead(out var line)) lines.Add(line);
        Assert.Contains("DONE", lines); Assert.Equal(0, owned.ExitCode);
    }
    [Fact] public void FailedCreateDoesNotLeaveRunningProcess() => Assert.ThrowsAny<Exception>(() => new ProcessService().StartOwned("C:\\missing\\no-such-tool.exe", "", "C:\\"));
}
