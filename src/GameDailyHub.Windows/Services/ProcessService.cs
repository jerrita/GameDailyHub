using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using GameDailyHub.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace GameDailyHub.Windows.Services;

public static class CommandArguments
{
    public static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static string[] Parse(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        var ptr = NativeMethods.CommandLineToArgvW("program.exe " + arguments, out var count);
        if (ptr == IntPtr.Zero) throw new Win32Exception();
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * IntPtr.Size))!).ToArray(); }
        finally { NativeMethods.LocalFree(ptr); }
    }
}

public sealed record ProcessIdentity(int Pid, long CreatedFileTime, string Path, int SessionId);

public sealed class ProcessService
{
    internal static Dictionary<string, string> ChildEnvironment(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);
        environment.Remove("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY");
        if (overrides is not null) foreach (var pair in overrides) environment[pair.Key] = pair.Value;
        return environment;
    }
    public static string Normalize(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    public IReadOnlyList<ProcessIdentity> FindRunning(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];
        var target = Normalize(path);
        var currentSession = Process.GetCurrentProcess().SessionId;
        var found = new List<ProcessIdentity>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
        {
            using (process)
            {
                try
                {
                    // Same-named processes we cannot inspect (elevated/system) are not controllable by us, so they are skipped.
                    if (process.SessionId == currentSession && ImagePath(process.Id) is { } image && string.Equals(Normalize(image), target, StringComparison.OrdinalIgnoreCase))
                        found.Add(new(process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc(), target, currentSession));
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            }
        }
        return found;
    }
    private static string? ImagePath(int pid)
    {
        using var handle = NativeMethods.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid) return null;
        var name = new StringBuilder(1024);
        var size = name.Capacity;
        return NativeMethods.QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString(0, size) : null;
    }
    public bool IsSameProcess(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            return process.SessionId == identity.SessionId && process.StartTime.ToUniversalTime().ToFileTimeUtc() == identity.CreatedFileTime
                && ImagePath(identity.Pid) is { } image && string.Equals(Normalize(image), identity.Path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
    public async Task<LaunchResult> LaunchNormalAsync(string path, string arguments, string directory, CancellationToken token, bool createNoWindow = false, IReadOnlyDictionary<string, string>? environment = null)
    {
        token.ThrowIfCancellationRequested();
        var target = Normalize(path);
        var existing = FindRunning(target).FirstOrDefault();
        if (existing is not null)
        {
            try
            {
                using var process = Process.GetProcessById(existing.Pid);
                if (IsSameProcess(existing))
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    { NativeMethods.ShowWindow(process.MainWindowHandle, 9); NativeMethods.SetForegroundWindow(process.MainWindowHandle); }
                    return new(true, true, "目标已运行，已尝试切换到现有窗口", existing.Pid);
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        }
        Process started;
        try
        {
            // Helpers inherit the Hub's administrator token. UI tests use an asInvoker host.
            var startInfo = new ProcessStartInfo(target, arguments) { UseShellExecute = false, WorkingDirectory = ResolveDirectory(directory, target), CreateNoWindow = createNoWindow };
            startInfo.Environment.Clear();
            foreach (var (key, value) in ChildEnvironment(environment)) startInfo.Environment[key] = value;
            started = Process.Start(startInfo)
                ?? throw new InvalidOperationException("系统未返回启动进程");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 740)
        {
            return new(false, false, "当前 Hub 未取得管理员权限，请从发布目录启动管理员版 Hub");
        }
        using var startedProcess = started;
        await Task.Yield();
        return new(true, false, "已启动 " + Path.GetFileName(target), started.Id);
    }
    public OwnedProcessSession StartOwned(string path, string arguments, string directory, IReadOnlyDictionary<string, string>? environment = null)
        => new(Normalize(path), arguments, ResolveDirectory(directory, Normalize(path)), environment);
    public static string ResolveDirectory(string directory, string path) => string.IsNullOrWhiteSpace(directory) ? Path.GetDirectoryName(path)! : Normalize(directory);
}

public sealed class OwnedProcessSession : IAsyncDisposable
{
    private readonly SafeFileHandle _job;
    private readonly SafeProcessHandle _process = new(IntPtr.Zero, true);
    private readonly AnonymousPipeServerStream _output;
    private readonly AnonymousPipeServerStream _error;
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly Task _pumps;
    private bool _disposed;
    public ProcessIdentity Identity { get; }
    public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>(new() { SingleReader = true, SingleWriter = false });
    public bool HasExited => NativeMethods.WaitForSingleObject(_process, 0) == 0;
    public int? ExitCode => HasExited && NativeMethods.GetExitCodeProcess(_process, out var code) ? unchecked((int)code) : null;
    public async Task<bool> DrainOutputAsync(CancellationToken token)
    {
        try { await _pumps.WaitAsync(TimeSpan.FromSeconds(2), token); return true; }
        catch (TimeoutException) { return false; }
    }
    public uint ActiveProcesses
    {
        get
        {
            if (!NativeMethods.QueryInformationJobObject(_job, 1, out var info, (uint)Marshal.SizeOf<NativeMethods.JobAccounting>(), IntPtr.Zero)) throw new Win32Exception();
            return info.ActiveProcesses;
        }
    }
    internal OwnedProcessSession(string path, string args, string directory, IReadOnlyDictionary<string, string>? overrides)
    {
        NativeMethods.ProcessInformation info = default;
        var environmentPtr = IntPtr.Zero;
        try
        {
            _job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (_job.IsInvalid) throw new Win32Exception();
            _output = new(PipeDirection.In, HandleInheritability.Inheritable);
            _error = new(PipeDirection.In, HandleInheritability.Inheritable);
            var limits = new NativeMethods.ExtendedLimit { Basic = new() { LimitFlags = 0x2000 } };
            if (!NativeMethods.SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<NativeMethods.ExtendedLimit>())) throw new Win32Exception();
            using var input = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (!NativeMethods.SetHandleInformation(input.DangerousGetHandle(), 1, 1)) throw new Win32Exception();
            using var handles = new InheritedHandles(input.DangerousGetHandle(), _output.ClientSafePipeHandle.DangerousGetHandle(), _error.ClientSafePipeHandle.DangerousGetHandle());
            var startup = new NativeMethods.StartupInfoEx
            {
                Startup = new()
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfoEx>(),
                    Flags = 0x100 | 1,
                    ShowWindow = 0,
                    StdInput = input.DangerousGetHandle(),
                    StdOutput = _output.ClientSafePipeHandle.DangerousGetHandle(),
                    StdError = _error.ClientSafePipeHandle.DangerousGetHandle()
                },
                Attributes = handles.Attributes
            };
            var environment = ProcessService.ChildEnvironment(overrides);
            environment["PYTHONIOENCODING"] = "utf-8";
            var block = string.Join('\0', environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Key + "=" + e.Value)) + "\0\0";
            environmentPtr = Marshal.StringToHGlobalUni(block);
            if (!NativeMethods.CreateProcess(path, new StringBuilder(CommandArguments.Quote(path) + " " + args), IntPtr.Zero, IntPtr.Zero,
                    true, 0x4 | 0x08000000 | 0x400 | 0x80000, environmentPtr, directory, ref startup, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            _process = new SafeProcessHandle(info.Process, true);
            if (!NativeMethods.AssignProcessToJobObject(_job, _process))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "无法在执行前绑定任务进程；已停止本次启动");
            }
            if (!NativeMethods.GetProcessTimes(_process, out var created, out _, out _, out _)) throw new Win32Exception();
            Identity = new((int)info.ProcessId, created, path, Process.GetCurrentProcess().SessionId);
            if (NativeMethods.ResumeThread(info.Thread) == uint.MaxValue) throw new Win32Exception();
            _output.DisposeLocalCopyOfClientHandle(); _error.DisposeLocalCopyOfClientHandle();
        }
        catch
        {
            if (!_process.IsInvalid) NativeMethods.TerminateProcess(_process, 1);
            _job?.Dispose(); _output?.Dispose(); _error?.Dispose();
            _process.Dispose();
            _pumpCancellation.Dispose();
            throw;
        }
        finally
        {
            if (info.Thread != IntPtr.Zero) NativeMethods.CloseHandle(info.Thread);
            Marshal.FreeHGlobal(environmentPtr);
        }
        _pumps = Task.WhenAll(PumpAsync(_output), PumpAsync(_error));
        _ = _pumps.ContinueWith(_ => Lines.Writer.TryComplete(), TaskScheduler.Default);
    }
    private sealed class InheritedHandles : IDisposable
    {
        public IntPtr Attributes { get; private set; }
        private IntPtr _handles;
        private bool _initialized;
        public InheritedHandles(params IntPtr[] handles)
        {
            try
            {
                nuint size = 0;
                NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == 0) throw new Win32Exception();
                Attributes = Marshal.AllocHGlobal(checked((int)size));
                if (!NativeMethods.InitializeProcThreadAttributeList(Attributes, 1, 0, ref size)) throw new Win32Exception();
                _initialized = true;
                _handles = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
                Marshal.Copy(handles, 0, _handles, handles.Length);
                if (!NativeMethods.UpdateProcThreadAttribute(Attributes, 0, 0x20002, _handles, (nuint)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_initialized) NativeMethods.DeleteProcThreadAttributeList(Attributes);
            Marshal.FreeHGlobal(Attributes); Marshal.FreeHGlobal(_handles);
            Attributes = _handles = IntPtr.Zero;
            _initialized = false;
        }
    }
    private async Task PumpAsync(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, false), true, 4096, leaveOpen: true);
            while (await reader.ReadLineAsync(_pumpCancellation.Token) is { } line) await Lines.Writer.WriteAsync(line, _pumpCancellation.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    public async Task<StopResult> StopAsync(CancellationToken token)
    {
        if (ActiveProcesses == 0) return new(true, "本次进程已退出");
        // Only the exact root created by this session receives a graceful window-close request.
        try
        {
            var service = new ProcessService();
            if (service.IsSameProcess(Identity))
            {
                using var root = Process.GetProcessById(Identity.Pid);
                root.CloseMainWindow();
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException) { }
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (ActiveProcesses > 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(100, token);
        if (ActiveProcesses > 0 && !NativeMethods.TerminateJobObject(_job, 1)) throw new Win32Exception();
        while (ActiveProcesses > 0) await Task.Delay(50, token);
        return new(true, "已清理本次任务创建的进程树，既有进程保持运行");
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // KILL_ON_JOB_CLOSE remains active even if graceful cleanup was interrupted.
        _job.Dispose();
        _pumpCancellation.Cancel();
        _output.Dispose(); _error.Dispose();
        await _pumps;
        _pumpCancellation.Dispose(); _process.Dispose();
    }
}
