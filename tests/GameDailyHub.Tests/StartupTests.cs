using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class StartupTests
{
    private const string Executable = @"C:\Test & Tools\GameDailyHub.App.exe";
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void SchedulerAcceptsDefinitionWithoutRegisteringATask()
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? definition = null;
        try
        {
            ((dynamic)service).Connect();
            definition = ((dynamic)service).NewTask(0);
            ((dynamic)definition).XmlText = StartupService.CreateTaskXml(Executable, Sid);
            Assert.True(StartupService.MatchesTask((string)((dynamic)definition).XmlText, Executable, Sid));
        }
        finally
        {
            if (definition is not null) Marshal.FinalReleaseComObject(definition);
            Marshal.FinalReleaseComObject(service);
        }
    }

    [Fact]
    public void LogonTaskUsesInteractiveUserHighestPrivilegeAndLiteralExecutable()
    {
        var xml = StartupService.CreateTaskXml(Executable, Sid);
        var root = XDocument.Parse(xml).Root!;
        Assert.True(StartupService.MatchesTask(xml, Executable, Sid));
        Assert.Equal(Executable, root.Descendants(Ns + "Command").Single().Value);
        Assert.Equal(Path.GetDirectoryName(Executable), root.Descendants(Ns + "WorkingDirectory").Single().Value);
        Assert.Empty(root.Descendants(Ns + "Arguments"));
        Assert.Equal("IgnoreNew", root.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);
        Assert.Equal("PT0S", root.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", root.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", root.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
    }

    [Theory]
    [InlineData("RunLevel", "LeastPrivilege")]
    [InlineData("LogonType", "ServiceAccount")]
    [InlineData("Command", @"C:\Moved\GameDailyHub.App.exe")]
    [InlineData("UserId", "S-1-5-18")]
    public void StartupStatusRejectsWrongPrivilegeIdentityAndMovedProgram(string element, string value)
    {
        var document = XDocument.Parse(StartupService.CreateTaskXml(Executable, Sid));
        foreach (var node in document.Descendants(Ns + element)) node.Value = value;
        Assert.False(StartupService.MatchesTask(document.ToString(), Executable, Sid));
    }
}
