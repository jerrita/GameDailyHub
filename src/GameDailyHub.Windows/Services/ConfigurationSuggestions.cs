using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

public static class ConfigurationSuggestions
{
    // Resolve only an unspecified path. An explicit path, including a missing file,
    // remains the user's choice so a different installation is never run silently.
    public static void Apply(GameConfig game, IEnumerable<string>? installationRoots = null)
    {
        if (string.IsNullOrWhiteSpace(game.GamePath))
        {
            var path = DefaultGamePaths(game.AdapterId, installationRoots).FirstOrDefault(File.Exists);
            if (path is not null) game.GamePath = path;
        }
        if (game.ToolRunMode == AssistantRunMode.Source) return;
        if (string.IsNullOrWhiteSpace(game.ToolPath))
        {
            var path = DefaultToolPaths(game.AdapterId, installationRoots).FirstOrDefault(File.Exists);
            if (path is not null) game.ToolPath = path;
        }
        if (game.AdapterId != "march7th" || !File.Exists(game.ToolPath)) return;
        var directory = Path.GetDirectoryName(game.ToolPath)!;
        var launcher = Path.Combine(directory, "March7th Launcher.exe");
        if (string.IsNullOrWhiteSpace(game.ToolGuiPath) && File.Exists(launcher)) game.ToolGuiPath = launcher;
        // The adapter already resolves config.yaml and default log/work directories.
    }

    public static IEnumerable<string> DefaultGamePaths(string adapterId, IEnumerable<string>? installationRoots = null)
    {
        string[] relativePaths = adapterId switch
        {
            "bettergi" => [
                @"miHoYo Launcher\games\Genshin Impact Game\YuanShen.exe",
                @"HoYoPlay\games\Genshin Impact game\GenshinImpact.exe",
                @"Genshin Impact\Genshin Impact Game\YuanShen.exe",
                @"Genshin Impact\Genshin Impact game\GenshinImpact.exe"
            ],
            "march7th" => [
                @"miHoYo Launcher\games\Star Rail Game\StarRail.exe",
                @"HoYoPlay\games\Star Rail Games\StarRail.exe",
                @"Star Rail\Game\StarRail.exe",
                @"Star Rail\Games\StarRail.exe"
            ],
            "okww" => [@"Wuthering Waves\Wuthering Waves Game\Wuthering Waves.exe"],
            "zzz" => [
                @"miHoYo Launcher\games\ZenlessZoneZero Game\ZenlessZoneZero.exe",
                @"HoYoPlay\games\ZenlessZoneZero Game\ZenlessZoneZero.exe",
                @"ZenlessZoneZero\ZenlessZoneZero Game\ZenlessZoneZero.exe"
            ],
            _ => []
        };
        if (relativePaths.Length == 0) yield break;

        var roots = installationRoots ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            AppContext.BaseDirectory
        }.Concat(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .SelectMany(d => new[] { d.RootDirectory.FullName, Path.Combine(d.RootDirectory.FullName, "Games") }));
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var relativePath in relativePaths)
                yield return Path.Combine(root, relativePath);
    }

    public static IEnumerable<string> DefaultToolPaths(string adapterId, IEnumerable<string>? installationRoots = null)
    {
        var (folder, executables) = adapterId switch
        {
            "bettergi" => ("BetterGI", new[] { "BetterGI.exe" }),
            "march7th" => ("March7thAssistant", new[] { "March7th Assistant.exe" }),
            "okww" => ("ok-ww", new[] { "ok-ww.exe" }),
            "zzz" => ("ZenlessZoneZero-OneDragon", new[] { "OneDragon-RuntimeLauncher.exe", "OneDragon-Launcher.exe" }),
            "maaend" => ("MaaEnd", new[] { "MaaEnd.exe", "mxu.exe" }),
            _ => ("", Array.Empty<string>())
        };
        if (executables.Length == 0) yield break;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = installationRoots ?? new[]
        {
            Path.Combine(local, "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            local,
            AppContext.BaseDirectory
        };
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var executable in executables)
                yield return Path.Combine(root, folder, executable);
    }
}
