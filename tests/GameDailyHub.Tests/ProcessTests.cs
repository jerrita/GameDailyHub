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
    public async Task OwnedProcessInheritsOnlyItsOwnStandardHandles()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.True(ProcessFixture.SetHandleInformation(handle.DangerousGetHandle(), 1, 1));
            await using var owned = new ProcessService().StartOwned(Fixture,
                "fixture probe-handle " + handle.DangerousGetHandle().ToInt64() + " " + CommandArguments.Quote(file), "");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!owned.HasExited) await Task.Delay(10, timeout.Token);
            Assert.True(await owned.DrainOutputAsync(timeout.Token));
            var lines = new List<string>();
            while (owned.Lines.Reader.TryRead(out var line)) lines.Add(line);
            Assert.Contains("EXTRA_HANDLE_INHERITED=False", lines);
            Assert.Equal(0, owned.ExitCode);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildEnvironmentDropsHubRuntimeAndPreservesOutputEncoding(bool owned)
    {
        const string variable = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";
        var original = Environment.GetEnvironmentVariable(variable);
        var file = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable(variable, "test-bootstrap-directory");
            var service = new ProcessService();
            var arguments = "fixture environment " + CommandArguments.Quote(file);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (owned)
            {
                await using var session = service.StartOwned(Fixture, arguments, "");
                while (!session.HasExited) await Task.Delay(10, timeout.Token);
                Assert.Equal(0, session.ExitCode);
            }
            else
            {
                var result = await service.LaunchNormalAsync(Fixture, arguments, "", timeout.Token, createNoWindow: true,
                    environment: new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" });
                Assert.True(result.Success);
                using var process = Process.GetProcessById(result.ProcessId!.Value);
                await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, process.ExitCode);
            }
            Assert.Equal("<unset>|utf-8", await File.ReadAllTextAsync(file, timeout.Token));
        }
        finally { Environment.SetEnvironmentVariable(variable, original); File.Delete(file); }
    }

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
