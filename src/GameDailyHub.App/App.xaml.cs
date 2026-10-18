using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;

namespace GameDailyHub.App;

public partial class App : Application
{
#if HUB_UI_TEST
    public static bool IsUiTestBuild { get; } = true;
#else
    public static bool IsUiTestBuild { get; } = false;
#endif
    public static string? SmokeRoot { get; private set; }
    public static List<string> BindingErrors { get; } = [];
    private Mutex? _instance;
    private MainWindow? _window;
    public App()
    {
        InitializeComponent();
        DebugSettings.BindingFailed += (_, e) => BindingErrors.Add(e.Message);
        UnhandledException += (_, e) =>
        {
            if (SmokeRoot is not null) File.WriteAllText(Path.Combine(SmokeRoot, "crash.txt"), e.Message + "\n" + e.Exception + "\n" + string.Join("\n", e.Exception.Data.Cast<System.Collections.DictionaryEntry>().Select(item => item.Key + "=" + item.Value)));
        };
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(commandLine, "--smoke-root");
#if HUB_UI_TEST
        if (smokeIndex < 0 || smokeIndex + 1 >= commandLine.Length) { Exit(); return; }
        if (smokeIndex >= 0 && smokeIndex + 1 < commandLine.Length)
        {
            SmokeRoot = Path.GetFullPath(commandLine[smokeIndex + 1]); Directory.CreateDirectory(SmokeRoot);
            File.Delete(Path.Combine(SmokeRoot, "smoke.json")); File.Delete(Path.Combine(SmokeRoot, "crash.txt"));
        }
#else
        if (smokeIndex >= 0) { Exit(); return; }
#endif
        using var identity = WindowsIdentity.GetCurrent();
        var instanceName = SmokeRoot is null ? identity.User!.Value
            : "UiTest-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SmokeRoot)));
        _instance = new(false, "Local\\GameDailyHub-" + instanceName);
        bool acquired;
        try { acquired = _instance.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            foreach (var p in Process.GetProcessesByName("GameDailyHub.App"))
            {
                using (p) if (p.Id != Environment.ProcessId && p.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(p.MainWindowHandle);
            }
            Exit(); return;
        }
        _window = new MainWindow();
        _window.Closed += (_, _) => { _instance.ReleaseMutex(); _instance.Dispose(); };
        _window.Activate();
        await _window.InitializeAsync();
        if (SmokeRoot is not null)
        {
            try { await _window.SmokeAsync(SmokeRoot); }
            catch (Exception e) { await File.WriteAllTextAsync(Path.Combine(SmokeRoot, "crash.txt"), e.ToString()); Environment.ExitCode = 1; }
            _window.Close(); Exit();
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
