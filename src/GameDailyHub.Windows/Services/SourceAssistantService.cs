using System.Collections.Concurrent;
using GameDailyHub.Core.Models;
using Microsoft.Win32;

namespace GameDailyHub.Windows.Services;

public sealed record SourceAssistantDefinition(string Folder, string Repository, string GuiEntry, string DailyEntry, string PythonVersion = "3.12");

public sealed class SourceAssistantService(ProcessService processes, string dataRoot)
{
    public const string PackageIndex = "https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static bool Supports(string adapterId) => adapterId is "march7th" or "okww" or "zzz";
    public static SourceAssistantDefinition Definition(string adapterId) => adapterId switch
    {
        "march7th" => new("March7thAssistant", "https://github.com/moesnow/March7thAssistant.git", "app.py", "main.py"),
        "okww" => new("ok-wuthering-waves", "https://github.com/ok-oldking/ok-wuthering-waves.git", "main.py", "main.py"),
        "zzz" => new("ZenlessZoneZero-OneDragon", "https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon.git", "src/zzz_od/gui/app.py", "src/zzz_od/application/zzz_application_launcher.py", "3.11"),
        _ => throw new InvalidOperationException("该助手暂不支持源码运行")
    };
    public string DirectoryFor(GameConfig game) => Path.TrimEndingDirectorySeparator(string.IsNullOrWhiteSpace(game.ToolSourceDirectory)
        ? Path.GetFullPath(Path.Combine(dataRoot, "assistants", Definition(game.AdapterId).Folder))
        : ProcessService.Normalize(game.ToolSourceDirectory));
    public string PythonPath(GameConfig game) => Path.Combine(DirectoryFor(game), ".venv", "Scripts", "python.exe");
    public string EntryPath(GameConfig game, bool gui) => Path.Combine(DirectoryFor(game), gui ? Definition(game.AdapterId).GuiEntry : Definition(game.AdapterId).DailyEntry);
    public IReadOnlyList<string> Validate(GameConfig game, bool gui)
    {
        if (!Supports(game.AdapterId)) return ["该助手暂不支持源码运行"];
        var errors = new List<string>();
        if (!File.Exists(EntryPath(game, gui))) errors.Add("源码入口不存在，请先克隆代码或选择源码目录");
        if (!File.Exists(PythonPath(game))) errors.Add("源码环境未安装，请先安装环境");
        return errors;
    }
    public string Arguments(GameConfig game, bool gui, string arguments) => "-u " + CommandArguments.Quote(EntryPath(game, gui))
        + (string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments);
    private Dictionary<string, string> RuntimeEnvironment(GameConfig game)
    {
        var environment = new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" };
        if (game.AdapterId == "zzz") environment["PYTHONPATH"] = Path.Combine(DirectoryFor(game), "src");
        return environment;
    }
    public async Task<LaunchResult> LaunchGuiAsync(GameConfig game, CancellationToken token)
    {
        var gate = Gate(game);
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("该源码目录正在处理其他操作");
        try
        {
            EnsureReady(game, true);
            return await processes.LaunchNormalAsync(PythonPath(game), Arguments(game, true, ""), DirectoryFor(game), token, createNoWindow: true, environment: RuntimeEnvironment(game));
        }
        finally { gate.Release(); }
    }
    public async Task<OwnedProcessSession> StartDailyAsync(GameConfig game, bool gui, string arguments, CancellationToken token)
    {
        var gate = Gate(game);
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("该源码目录正在处理其他操作");
        try
        {
            EnsureReady(game, gui);
            EnsureStopped(game);
            token.ThrowIfCancellationRequested();
            return processes.StartOwned(PythonPath(game), Arguments(game, gui, arguments), DirectoryFor(game), RuntimeEnvironment(game));
        }
        finally { gate.Release(); }
    }
    public async Task PrepareAsync(GameConfig game, bool updateCode, bool installEnvironment, Action<string> log, CancellationToken token)
    {
        var gate = Gate(game);
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("该源码目录正在处理其他操作");
        try
        {
            EnsureStopped(game);
            var directory = DirectoryFor(game);
            var definition = Definition(game.AdapterId);
            if (updateCode)
            {
                var git = FindExecutable("git");
                if (File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git")))
                {
                    await RunAsync(git, ["-c", "core.longpaths=true", "-C", directory, "pull", "--ff-only", "--progress"], directory, log, token);
                }
                else
                {
                    if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                        throw new InvalidOperationException("源码目录非空且未包含 Git 仓库，请选择仓库目录或空目录");
                    var parent = Path.GetDirectoryName(directory) ?? throw new InvalidOperationException("请选择有效的源码目录");
                    Directory.CreateDirectory(parent);
                    await RunAsync(git, ["-c", "core.longpaths=true", "clone", "--depth", "1", "--progress", "--recurse-submodules", "--shallow-submodules", definition.Repository, directory], parent, log, token);
                }
                await RunAsync(git, ["-c", "core.longpaths=true", "-C", directory, "submodule", "update", "--init", "--recursive", "--depth", "1", "--progress"], directory, log, token);
            }
            if (installEnvironment)
            {
                if (!File.Exists(EntryPath(game, false))) throw new InvalidOperationException("源码入口不存在，请先克隆代码或选择源码目录");
                var uv = FindExecutable("uv");
                token.ThrowIfCancellationRequested();
                EnsureLongPathsEnabled(log);
                if (game.AdapterId is "march7th" or "zzz" && File.Exists(Path.Combine(directory, "pyproject.toml")))
                    await RunAsync(uv, ["sync", "--python", definition.PythonVersion, "--no-dev", "--default-index", PackageIndex, "--index", "pypi=" + PackageIndex], directory, log, token);
                else
                {
                    var requirements = Path.Combine(directory, "requirements.txt");
                    if (!File.Exists(requirements)) throw new InvalidOperationException("源码目录缺少 requirements.txt");
                    if (!File.Exists(PythonPath(game))) await RunAsync(uv, ["venv", "--python", definition.PythonVersion, ".venv"], directory, log, token);
                    await RunAsync(uv, ["pip", "install", "--python", PythonPath(game), "--default-index", PackageIndex, "-r", requirements], directory, log, token);
                }
            }
        }
        finally { gate.Release(); }
    }
    private static void EnsureLongPathsEnabled(Action<string> log)
    {
        const string keyPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";
        const string valueName = "LongPathsEnabled";
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var current = machine.OpenSubKey(keyPath);
            if (current?.GetValue(valueName) is int value && value == 1) return;
            using var writable = machine.OpenSubKey(keyPath, writable: true)
                ?? throw new InvalidOperationException("找不到 Windows 文件系统设置，无法启用长路径支持");
            writable.SetValue(valueName, 1, RegistryValueKind.DWord);
            if (writable.GetValue(valueName) is not int enabled || enabled != 1)
                throw new InvalidOperationException("Windows 长路径设置未生效，请检查系统策略后重试");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("启用 Windows 长路径支持失败，请以管理员身份运行 GameDailyHub 后重试", e);
        }
        log("已启用 Windows 长路径支持");
    }
    private SemaphoreSlim Gate(GameConfig game) => Gates.GetOrAdd(DirectoryFor(game), _ => new(1));
    private void EnsureReady(GameConfig game, bool gui)
    {
        var errors = Validate(game, gui);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
    }
    private void EnsureStopped(GameConfig game)
    {
        if (processes.FindRunning(PythonPath(game)).Count > 0) throw new InvalidOperationException("源码助手正在运行，请关闭后重试");
    }
    public static string FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim().Trim('"'), name + ".exe");
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidOperationException("找不到 " + name + "，请确认已安装并加入 PATH 后重启 GameDailyHub");
    }
    private async Task RunAsync(string path, string[] arguments, string directory, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        log(Path.GetFileNameWithoutExtension(path) + " " + string.Join(" ", arguments.Select(CommandArguments.Quote)));
        await using var session = processes.StartOwned(path, string.Join(" ", arguments.Select(CommandArguments.Quote)), directory,
            new Dictionary<string, string> { ["UV_PROJECT_ENVIRONMENT"] = Path.Combine(directory, ".venv"), ["UV_DEFAULT_INDEX"] = PackageIndex, ["GIT_TERMINAL_PROMPT"] = "0" });
        while (!session.HasExited)
        {
            while (session.Lines.Reader.TryRead(out var line)) log(line);
            await Task.Delay(100, token);
        }
        if (!await session.DrainOutputAsync(token)) throw new InvalidOperationException("命令输出未结束，请检查进程状态后重试");
        while (session.Lines.Reader.TryRead(out var line)) log(line);
        token.ThrowIfCancellationRequested();
        if (session.ExitCode != 0) throw new InvalidOperationException(Path.GetFileNameWithoutExtension(path) + " 执行失败，退出码 " + session.ExitCode + "，请查看运行日志");
    }
}
