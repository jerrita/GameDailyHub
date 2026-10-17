using Microsoft.Win32;

namespace GameDailyHub.Windows.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameDailyHub";

    public Task<bool> IsEnabledAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return Task.FromResult(key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value));
    }

    public Task SetEnabledAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取应用路径");
            key.SetValue(ValueName, CommandArguments.Quote(path), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, false);
        return Task.CompletedTask;
    }
}
