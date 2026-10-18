using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameDailyHub.Tests;

internal static class ProcessFixture
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "-u")
        {
            Console.WriteLine("ENTRY=" + Path.GetFileName(args[1]));
            Console.WriteLine("CWD=" + Environment.CurrentDirectory);
            Console.WriteLine("PYTHONPATH=" + Environment.GetEnvironmentVariable("PYTHONPATH"));
            Console.WriteLine("PYTHONIOENCODING=" + Environment.GetEnvironmentVariable("PYTHONIOENCODING"));
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "fixture-entry.txt"), Path.GetFileName(args[1]));
            args = ["fixture", .. args.Skip(2)];
        }
        if (args.Length == 0 || args[0] != "fixture") return 2;
        if (args.Length >= 4 && args[1] == "probe-handle")
        {
            var handle = new IntPtr(long.Parse(args[2]));
            var name = new StringBuilder(4096);
            var length = GetFinalPathNameByHandle(handle, name, name.Capacity, 0);
            var inherited = length > 0 && string.Equals(name.ToString().Replace(@"\\?\", ""), args[3], StringComparison.OrdinalIgnoreCase);
            Console.WriteLine("EXTRA_HANDLE_INHERITED=" + inherited);
            return 0;
        }
        if (args.Length >= 3 && args[1] == "environment")
        {
            var value = (Environment.GetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY") ?? "<unset>")
                + "|" + Environment.GetEnvironmentVariable("PYTHONIOENCODING");
            File.WriteAllText(args[2], value);
            return 0;
        }
        Console.WriteLine("ROOT=" + Environment.ProcessId);
        if (args.Contains("spawn"))
        {
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "fixture wait", UseShellExecute = false, CreateNoWindow = true })!;
            Console.WriteLine("CHILD=" + child.Id);
        }
        if (args.Contains("exit")) { Console.WriteLine("START"); Console.WriteLine("DONE"); return 0; }
        await Task.Delay(Timeout.Infinite);
        return 0;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(IntPtr handle, StringBuilder path, int size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
