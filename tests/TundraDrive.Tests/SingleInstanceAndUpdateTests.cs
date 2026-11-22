using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class SingleInstanceAndUpdateTests
{
    [Fact]
    public void SingleInstanceGuard_BlocksSecondInstanceSignalsPrimaryAndReleasesOnDispose()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tundra-si-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var lockPath = Path.Combine(tempDir, "app.lock");
        var sockPath = Path.Combine(Path.GetTempPath(), $"tsi-{Guid.NewGuid():N}.sock");

        try
        {
            using (var first = SingleInstanceGuard.TryAcquireOrActivate(lockPath, sockPath, TimeSpan.FromSeconds(2)))
            {
                Assert.True(first.IsPrimary);
                Assert.NotNull(first.Guard);

                var activated = new ManualResetEventSlim(false);
                first.Guard!.ActivationRequested += (_, _) => activated.Set();

                var second = SingleInstanceGuard.TryAcquireOrActivate(lockPath, sockPath, TimeSpan.FromSeconds(2));
                Assert.False(second.IsPrimary);
                Assert.Null(second.Guard);
                Assert.True(second.SignaledExistingInstance);
                Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));

                first.Guard.Dispose();
            }

            using var third = SingleInstanceGuard.TryAcquireOrActivate(lockPath, sockPath, TimeSpan.FromSeconds(1));
            Assert.True(third.IsPrimary);
            Assert.NotNull(third.Guard);
            third.Guard?.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }

            if (File.Exists(sockPath))
            {
                File.Delete(sockPath);
            }
        }
    }

    [Theory]
    [InlineData("v2.6.1", "2.6.0", true)]
    [InlineData("2.7.0", "2.6.0", true)]
    [InlineData("v3.0.0", "2.6.9", true)]
    [InlineData("v2.6.0", "2.6.0", false)]
    [InlineData("v2.5.9", "2.6.0", false)]
    public void AppVersionInfo_ComparesSemanticVersionsCorrectly(string candidate, string current, bool expectedNewer)
    {
        Assert.Equal(expectedNewer, AppVersionInfo.IsNewerVersion(candidate, current));
    }

    [Fact]
    public async Task GitHubAppUpdateService_ChecksDownloadsAndVerifiesSha256()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tundra-upd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var payload = Encoding.UTF8.GetBytes("fake-tundradrive-appimage-binary-content");
            var shaHex = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            var sumsText = $"{shaHex}  TundraDrive-2.7.0-x86_64.AppImage\n";

            var releaseJson = """
                {
                  "tag_name": "v2.7.0",
                  "html_url": "https://github.com/TundraLab/TundraDrive/releases/tag/v2.7.0",
                  "body": "Melhorias de desempenho e correções.",
                  "published_at": "2026-12-01T12:00:00Z",
                  "assets": [
                    {
                      "name": "TundraDrive-2.7.0-x86_64.AppImage",
                      "browser_download_url": "https://downloads.example/TundraDrive-2.7.0-x86_64.AppImage",
                      "size": 40
                    },
                    {
                      "name": "TundraDrive-2.7.0-1.x86_64.rpm",
                      "browser_download_url": "https://downloads.example/TundraDrive-2.7.0-1.x86_64.rpm",
                      "size": 42
                    },
                    {
                      "name": "SHA256SUMS.txt",
                      "browser_download_url": "https://downloads.example/SHA256SUMS.txt",
                      "size": 100
                    }
                  ]
                }
                """;

            using var handler = new StubHttpMessageHandler(req =>
            {
                var url = req.RequestUri!.AbsoluteUri;
                if (url.Contains("api.github.com", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }

                if (url.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(sumsText, Encoding.UTF8, "text/plain")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                };
            });

            using var svc = new GitHubAppUpdateService(
                NullLogger<GitHubAppUpdateService>.Instance,
                handler,
                tempDir,
                "2.6.0");

            var check = await svc.CheckForUpdatesAsync();
            Assert.True(check.IsUpdateAvailable);
            Assert.Equal("2.7.0", check.LatestVersion);
            Assert.NotNull(check.AppImageAsset);
            Assert.NotNull(check.RpmAsset);

            var downloaded = await svc.DownloadUpdatePackageAsync(check);
            Assert.True(downloaded.Sha256Verified);
            Assert.True(File.Exists(downloaded.LocalFilePath));

            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");
            var aboutVm = new AboutViewModel(loc, svc);
            await aboutVm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.True(aboutVm.IsUpdateAvailable);
            Assert.Contains("2.7.0", aboutVm.UpdateStatusText);
            Assert.True(aboutVm.HasReleaseNotes);
            Assert.Contains("Melhorias", aboutVm.LatestReleaseNotes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GitHubAppUpdateService_RejectsCorruptedSha256Payload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tundra-upd-bad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var payload = Encoding.UTF8.GetBytes("tampered-binary");
            var badSums = "0000000000000000000000000000000000000000000000000000000000000000  TundraDrive-2.7.0-x86_64.AppImage\n";

            using var handler = new StubHttpMessageHandler(req =>
            {
                if (req.RequestUri!.AbsoluteUri.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(badSums, Encoding.UTF8, "text/plain")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                };
            });

            using var svc = new GitHubAppUpdateService(
                NullLogger<GitHubAppUpdateService>.Instance,
                handler,
                tempDir,
                "2.6.0");

            var fakeCheck = new AppUpdateCheckResult
            {
                CurrentVersion = "2.6.0",
                LatestVersion = "2.7.0",
                TagName = "v2.7.0",
                IsUpdateAvailable = true,
                ReleaseUrl = "https://github.com/TundraLab/TundraDrive/releases/tag/v2.7.0",
                AppImageAsset = new ReleaseAssetInfo("TundraDrive-2.7.0-x86_64.AppImage", "https://downloads.example/TundraDrive-2.7.0-x86_64.AppImage", payload.Length),
                ChecksumsAsset = new ReleaseAssetInfo("SHA256SUMS.txt", "https://downloads.example/SHA256SUMS.txt", badSums.Length),
                InstallKind = AppInstallKind.AppImage
            };

            await Assert.ThrowsAsync<InvalidDataException>(() => svc.DownloadUpdatePackageAsync(fakeCheck));
            Assert.False(File.Exists(Path.Combine(tempDir, "TundraDrive-2.7.0-x86_64.AppImage")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
