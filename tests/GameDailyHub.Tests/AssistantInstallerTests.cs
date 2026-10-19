using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class AssistantInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameDailyHub-install-test-" + Guid.NewGuid());
    private const string PackageName = "MaaEnd-win-x86_64-v1.2.3.zip";
    private const string DownloadUrl = "https://github.com/MaaEnd/MaaEnd/releases/download/v1.2.3/" + PackageName;

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            Assert.Contains("GameDailyHub", request.Headers.UserAgent.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static byte[] Zip(string? extra = null, bool complete = true)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            string[] paths = complete ? ["MaaEnd.exe", "interface.json", "agent/go-service.exe", "agent/cpp-algo.exe", "maafw/MaaFramework.dll", "maafw/MaaToolkit.dll", "resource/example.json"] : ["MaaEnd.exe"];
            foreach (var path in paths.Concat(extra is null ? [] : new[] { extra }))
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(path == "interface.json" ? "{\"name\":\"MaaEnd\"}" : "fixture");
            }
        }
        return buffer.ToArray();
    }

    private static Handler Respond(byte[] zip, bool badHash = false, bool prerelease = false, string name = PackageName, string url = DownloadUrl, HttpStatusCode downloadStatus = HttpStatusCode.OK)
        => new(request => request.RequestUri!.Host == "api.github.com"
            ? new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = "v1.2.3",
                    draft = false,
                    prerelease,
                    assets = new[] { new { name, browser_download_url = url, size = zip.Length, digest = "sha256:" + (badHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(zip))) } }
                }))
            }
            : new(downloadStatus) { Content = new ByteArrayContent(zip) });

    [Fact]
    public async Task InstallsVerifiedOfficialPackageAndReusesExistingInstallation()
    {
        using var handler = Respond(Zip());
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        var messages = new List<string>();
        var executable = await installer.InstallMaaEndAsync(messages.Add, default);
        Assert.Equal(Path.Combine(_root, "assistants", "MaaEnd", "MaaEnd.exe"), executable);
        Assert.True(File.Exists(executable));
        Assert.Equal(DownloadUrl, handler.Requests[1].ToString());
        Assert.Contains(messages, m => m.Contains("100%"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "assistants"), ".MaaEnd-install-*"));
        Directory.CreateDirectory(Path.Combine(installer.MaaEndDirectory, "config"));
        var config = Path.Combine(installer.MaaEndDirectory, "config", "mxu-MaaEnd.json");
        File.WriteAllText(config, "local-settings");
        Assert.Equal(executable, await installer.InstallMaaEndAsync(messages.Add, default));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("local-settings", File.ReadAllText(config));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BadHashAndIncompletePackageLeaveNoPartialInstallation(bool badHash, bool incomplete)
    {
        using var handler = Respond(Zip(complete: !incomplete), badHash: badHash);
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallMaaEndAsync(_ => { }, default));
        Assert.False(Directory.Exists(installer.MaaEndDirectory));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "assistants")));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    [InlineData("MaaEnd.exe:stream")]
    public async Task UnsafeZipPathsAreRejectedBeforeFilesCanEscape(string path)
    {
        using var handler = Respond(Zip(path));
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallMaaEndAsync(_ => { }, default));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "assistants")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Theory]
    [InlineData(true, PackageName, DownloadUrl)]
    [InlineData(false, "MaaEnd-win-aarch64-v1.2.3.zip", DownloadUrl)]
    [InlineData(false, PackageName, "https://example.com/package.zip")]
    public async Task RejectsPrereleasesOtherArchitecturesAndForeignDownloadUrls(bool prerelease, string name, string url)
    {
        using var handler = Respond(Zip(), prerelease: prerelease, name: name, url: url);
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallMaaEndAsync(_ => { }, default));
        Assert.Single(handler.Requests);
        Assert.False(Directory.Exists(installer.MaaEndDirectory));
    }

    [Fact]
    public async Task CancellationCleansStagingAndAllowsRetry()
    {
        using var handler = Respond(Zip());
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallMaaEndAsync(line =>
        {
            if (line.StartsWith("正在下载", StringComparison.Ordinal)) cancellation.Cancel();
        }, cancellation.Token));
        Assert.False(Directory.Exists(installer.MaaEndDirectory));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "assistants")));
        Assert.True(File.Exists(await installer.InstallMaaEndAsync(_ => { }, default)));
    }

    [Fact]
    public async Task HttpDownloadFailureLeavesDestinationUntouched()
    {
        using var handler = Respond(Zip(), downloadStatus: HttpStatusCode.BadGateway);
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        await Assert.ThrowsAsync<HttpRequestException>(() => installer.InstallMaaEndAsync(_ => { }, default));
        Assert.False(Directory.Exists(installer.MaaEndDirectory));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "assistants")));
    }

    [Fact]
    public async Task ExistingUserFilesAreNeverOverwritten()
    {
        using var handler = Respond(Zip());
        using var client = new HttpClient(handler);
        var installer = new AssistantInstallerService(_root, client);
        Directory.CreateDirectory(installer.MaaEndDirectory);
        var existing = Path.Combine(installer.MaaEndDirectory, "user.json");
        File.WriteAllText(existing, "saved-settings");
        await Assert.ThrowsAsync<IOException>(() => installer.InstallMaaEndAsync(_ => { }, default));
        Assert.Equal("saved-settings", File.ReadAllText(existing));
        Assert.Empty(handler.Requests);
    }

    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("GameDailyHub-install-test-", StringComparison.Ordinal)) throw new InvalidOperationException();
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
