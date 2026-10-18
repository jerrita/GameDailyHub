using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace GameDailyHub.Windows.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameDailyHub";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static string UserSid
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; }
    }
    private static string TaskName => "GameDailyHub-" + UserSid;

    public Task<bool> IsEnabledAsync() => Task.FromResult(WithFolder(folder =>
    {
        object? task = null;
        try
        {
            task = folder.GetTask(TaskName);
            return (bool)((dynamic)task).Enabled && MatchesTask((string)((dynamic)task).Xml, Environment.ProcessPath!, UserSid);
        }
        catch (COMException e) when (IsMissing(e)) { return false; }
        finally { if (task is not null) Marshal.FinalReleaseComObject(task); }
    }));

    public async Task MigrateLegacyAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value)) await SetEnabledAsync(true);
    }

    public Task SetEnabledAsync(bool enabled)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("请从发布目录启动管理员版 Hub 后设置登录启动");
        WithFolder(folder =>
        {
            if (enabled)
            {
                var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取应用路径");
                object task = folder.RegisterTask(TaskName, CreateTaskXml(path, UserSid), 6, UserSid, null, 3, null);
                Marshal.FinalReleaseComObject(task);
            }
            else
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (COMException e) when (IsMissing(e)) { }
            }
            return true;
        });
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(ValueName, false);
        return Task.CompletedTask;
    }

    public static string CreateTaskXml(string executable, string userSid)
    {
        var ns = TaskNamespace;
        return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Description", "登录时启动 GameDailyHub")),
            new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", true), new XElement(ns + "UserId", userSid))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                new XElement(ns + "UserId", userSid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false), new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "StartWhenAvailable", false), new XElement(ns + "ExecutionTimeLimit", "PT0S"), new XElement(ns + "Enabled", true)),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"), new XElement(ns + "Exec",
                new XElement(ns + "Command", Path.GetFullPath(executable)), new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(Path.GetFullPath(executable))))))).ToString();
    }

    public static bool MatchesTask(string xml, string executable, string userSid)
    {
        var root = XDocument.Parse(xml).Root;
        var ns = TaskNamespace;
        var principal = root?.Element(ns + "Principals")?.Element(ns + "Principal");
        var trigger = root?.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
        var command = root?.Element(ns + "Actions")?.Element(ns + "Exec")?.Element(ns + "Command")?.Value;
        return principal?.Element(ns + "UserId")?.Value == userSid
            && principal.Element(ns + "RunLevel")?.Value == "HighestAvailable"
            && principal.Element(ns + "LogonType")?.Value == "InteractiveToken"
            && trigger?.Element(ns + "UserId")?.Value == userSid
            && trigger.Element(ns + "Enabled")?.Value != "false"
            && string.Equals(command, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissing(COMException e) => e.HResult is unchecked((int)0x80070002) or unchecked((int)0x8004130F);
    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("任务计划服务不可用"))!;
        object? folder = null;
        try
        {
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(service);
        }
    }
}
