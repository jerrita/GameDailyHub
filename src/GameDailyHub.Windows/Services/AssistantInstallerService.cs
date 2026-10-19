using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameDailyHub.Windows.Services;

public sealed class AssistantInstallerService(string dataRoot, HttpClient? client = null)
{
    private static readonly HttpClient DefaultClient = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly string[] RequiredFiles = ["MaaEnd.exe", "interface.json", "agent/go-service.exe", "agent/cpp-algo.exe", "maafw/MaaFramework.dll", "maafw/MaaToolkit.dll"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string MaaEndDirectory => Path.GetFullPath(Path.Combine(dataRoot, "assistants", "MaaEnd"));

    public async Task<string> InstallMaaEndAsync(Action<string> log, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("MaaEnd 正在安装中");
        string? staging = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        var token = timeout.Token;
        try
        {
            var target = MaaEndDirectory;
            var parent = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(parent);
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("助手安装目录不能为目录链接");
            if (Directory.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("MaaEnd 安装目录不能为目录链接");
                if (File.Exists(Path.Combine(target, "MaaEnd.exe")))
                {
                    ValidatePackage(target);
                    log("MaaEnd 已安装，使用现有程序");
                    return Path.Combine(target, "MaaEnd.exe");
                }
                if (Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("MaaEnd 安装目录已有文件，安装已停止：" + target);
            }
            log("正在查询 MaaEnd 最新正式版…");
            var http = client ?? DefaultClient;
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/MaaEnd/MaaEnd/releases/latest");
            request.Headers.UserAgent.ParseAdd("GameDailyHub");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var metadata = release.RootElement;
            if (metadata.GetProperty("draft").GetBoolean() || metadata.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("GitHub 返回了非正式发布版本");
            var version = metadata.GetProperty("tag_name").GetString();
            var packageName = $"MaaEnd-win-x86_64-{version}.zip";
            var assets = metadata.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == packageName).ToArray();
            if (assets.Length != 1) throw new InvalidDataException("最新正式版缺少唯一的 Windows x64 ZIP 发布包");
            var asset = assets[0];
            var address = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (address.Scheme != "https" || address.Host != "github.com" || !address.AbsolutePath.StartsWith("/MaaEnd/MaaEnd/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("发布包下载地址不属于 MaaEnd 官方仓库");
            var size = asset.GetProperty("size").GetInt64();
            if (size is <= 0 or > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("发布包大小无效");
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("官方发布包缺少有效的 SHA-256 校验值");

            staging = Path.Combine(parent, ".MaaEnd-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var zipPath = Path.Combine(staging, "package.zip");
            log($"正在下载 {version}（{size / 1024d / 1024:F1} MB）…");
            using (var downloadRequest = new HttpRequestMessage(HttpMethod.Get, address))
            {
                downloadRequest.Headers.UserAgent.ParseAdd("GameDailyHub");
                using var download = await http.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, token);
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long received = 0;
                var lastProgress = Environment.TickCount64;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += read;
                    if (received > size) throw new InvalidDataException("下载内容超过官方发布包大小");
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (Environment.TickCount64 - lastProgress >= 1000 || received == size)
                    {
                        log($"下载 {version}：{received * 100d / size:F0}%（{received / 1024d / 1024:F1} / {size / 1024d / 1024:F1} MB）");
                        lastProgress = Environment.TickCount64;
                    }
                }
                if (received != size || !Convert.ToHexString(sha.GetHashAndReset()).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("发布包下载不完整或 SHA-256 校验失败，请重试");
            }
            log("校验通过，正在解压…");
            var payload = Path.Combine(staging, "payload");
            await ExtractAsync(zipPath, payload, token);
            ValidatePackage(payload);
            token.ThrowIfCancellationRequested();
            // Only the verified, complete directory is committed; existing user files are never replaced.
            if (Directory.Exists(target)) Directory.Delete(target, false);
            Directory.Move(payload, target);
            log("MaaEnd 已安装到：" + target);
            return Path.Combine(target, "MaaEnd.exe");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("MaaEnd 安装超时，请检查网络后重试");
        }
        finally
        {
            try
            {
                if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            finally { _gate.Release(); }
        }
    }

    private static async Task ExtractAsync(string zipPath, string directory, CancellationToken token)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            total = checked(total + entry.Length);
            if (total > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("ZIP 解压后超过允许的大小");
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(directory, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || name.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("ZIP 包含无效或越界路径：" + entry.FullName);
            if (name.EndsWith(Path.DirectorySeparatorChar)) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }

    private static void ValidatePackage(string directory)
    {
        foreach (var relative in RequiredFiles)
            if (!File.Exists(Path.Combine(directory, relative))) throw new InvalidDataException("MaaEnd 发布包缺少文件：" + relative);
        if (!Directory.Exists(Path.Combine(directory, "resource"))) throw new InvalidDataException("MaaEnd 发布包缺少任务资源");
        using var definition = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "interface.json")), new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (definition.RootElement.GetProperty("name").GetString() != "MaaEnd") throw new InvalidDataException("发布包的项目标识无效");
    }
}
