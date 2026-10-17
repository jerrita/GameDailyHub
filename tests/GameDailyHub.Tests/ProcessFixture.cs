using System.Diagnostics;

namespace GameDailyHub.Tests;

internal static class ProcessFixture
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "-u")
        {
            Console.WriteLine("ENTRY=" + Path.GetFileName(args[1]));
            Console.WriteLine("CWD=" + Environment.CurrentDirectory);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "fixture-entry.txt"), Path.GetFileName(args[1]));
            args = ["fixture", .. args.Skip(2)];
        }
        if (args.Length == 0 || args[0] != "fixture") return 2;
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
}
