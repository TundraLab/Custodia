using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Security;

namespace TundraDrive.Core.Updates;

public interface IAppUpdateService
{
    AppInstallKind DetectInstallKind(out string? installedPackagePath);

    Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<DownloadedUpdatePackage> DownloadUpdatePackageAsync(
        AppUpdateCheckResult checkResult,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UpdateApplyLaunchResult> LaunchUpdateInstallerAsync(
        DownloadedUpdatePackage package,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubAppUpdateService : IAppUpdateService, IDisposable
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/TundraLab/Custodia/releases/latest";

    private const string LatestReleaseWebUrl =
        "https://github.com/TundraLab/Custodia/releases/latest";

    private readonly ILogger<GitHubAppUpdateService> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _updatesDirectory;
    private readonly string _currentVersion;

    public GitHubAppUpdateService(ILogger<GitHubAppUpdateService> logger)
        : this(logger, null, AppPaths.UpdatesDirectory, AppVersionInfo.CurrentVersion)
    {
    }

    internal GitHubAppUpdateService(
        ILogger<GitHubAppUpdateService> logger,
        HttpMessageHandler? messageHandler,
        string updatesDirectory,
        string currentVersion)
    {
        _logger = logger;
        _updatesDirectory = updatesDirectory;
        _currentVersion = string.IsNullOrWhiteSpace(currentVersion)
            ? AppVersionInfo.FallbackVersion
            : currentVersion;

        if (messageHandler is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true
            });
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient(messageHandler, disposeHandler: false);
            _ownsHttpClient = false;
        }

        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Custodia", _currentVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public AppInstallKind DetectInstallKind(out string? installedPackagePath)
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) &&
            !appImagePath.Contains("/.cache/custodia-installer/", StringComparison.Ordinal) &&
            !appImagePath.Contains("/.cache/tundradrive-installer/", StringComparison.Ordinal) &&
            File.Exists(appImagePath))
        {
            installedPackagePath = appImagePath;
            return AppInstallKind.AppImage;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            (processPath.StartsWith("/opt/custodia/", StringComparison.Ordinal) ||
             processPath.StartsWith("/opt/tundradrive/", StringComparison.Ordinal)))
        {
            installedPackagePath = File.Exists("/usr/bin/custodia")
                ? "/usr/bin/custodia"
                : File.Exists("/usr/bin/tundradrive")
                    ? "/usr/bin/tundradrive"
                    : processPath;
            return AppInstallKind.Rpm;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppImage = Path.Combine(userProfile, ".local", "opt", "custodia", "Custodia.AppImage");
        if (!File.Exists(localAppImage))
        {
            localAppImage = Path.Combine(userProfile, ".local", "opt", "tundradrive", "TundraDrive.AppImage");
        }
        if (File.Exists(localAppImage) &&
            !string.IsNullOrWhiteSpace(processPath) &&
            !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            installedPackagePath = localAppImage;
            return AppInstallKind.AppImage;
        }

        installedPackagePath = null;
        return AppInstallKind.Standalone;
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var installKind = DetectInstallKind(out var installedPackagePath);

        using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (resp.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
        {
            return await CheckViaRedirectFallbackAsync(installKind, installedPackagePath, cancellationToken);
        }

        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagEl)
            ? tagEl.GetString() ?? $"v{_currentVersion}"
            : $"v{_currentVersion}";
        var latestVersion = tagName.Trim().TrimStart('v', 'V');
        var releaseUrl = root.TryGetProperty("html_url", out var htmlEl)
            ? htmlEl.GetString() ?? LatestReleaseWebUrl
            : LatestReleaseWebUrl;
        var releaseNotes = root.TryGetProperty("body", out var bodyEl)
            ? bodyEl.GetString() ?? string.Empty
            : string.Empty;

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var pubEl) &&
            pubEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(pubEl.GetString(), out var parsedPub))
        {
            publishedAt = parsedPub;
        }

        ReleaseAssetInfo? rpmAsset = null;
        ReleaseAssetInfo? appImageAsset = null;
        ReleaseAssetInfo? installerAsset = null;
        ReleaseAssetInfo? checksumsAsset = null;

        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var uEl) ? uEl.GetString() ?? string.Empty : string.Empty;
                var size = asset.TryGetProperty("size", out var sEl) && sEl.TryGetInt64(out var parsedSize) ? parsedSize : 0;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var info = new ReleaseAssetInfo(name, url, size);
                if (name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
                {
                    rpmAsset = info;
                }
                else if (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                {
                    appImageAsset = info;
                }
                else if (name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset ??= info;
                }
                else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumsAsset = info;
                }
            }
        }

        return new AppUpdateCheckResult
        {
            CurrentVersion = _currentVersion,
            LatestVersion = latestVersion,
            TagName = tagName,
            IsUpdateAvailable = AppVersionInfo.IsNewerVersion(latestVersion, _currentVersion),
            ReleaseUrl = releaseUrl,
            ReleaseNotes = releaseNotes.Trim(),
            PublishedAtUtc = publishedAt,
            RpmAsset = rpmAsset,
            AppImageAsset = appImageAsset,
            InstallerScriptAsset = installerAsset,
            ChecksumsAsset = checksumsAsset,
            InstallKind = installKind,
            InstalledPackagePath = installedPackagePath
        };
    }

    private async Task<AppUpdateCheckResult> CheckViaRedirectFallbackAsync(
        AppInstallKind installKind,
        string? installedPackagePath,
        CancellationToken cancellationToken)
    {
        using var resp = await _httpClient.GetAsync(LatestReleaseWebUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var finalUri = resp.RequestMessage?.RequestUri?.AbsoluteUri ?? LatestReleaseWebUrl;
        var tagName = finalUri.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? $"v{_currentVersion}";
        if (!tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            tagName = $"v{_currentVersion}";
        }

        var latestVersion = tagName.TrimStart('v', 'V');
        var dlBase = $"https://github.com/TundraLab/Custodia/releases/download/{tagName}";
        return new AppUpdateCheckResult
        {
            CurrentVersion = _currentVersion,
            LatestVersion = latestVersion,
            TagName = tagName,
            IsUpdateAvailable = AppVersionInfo.IsNewerVersion(latestVersion, _currentVersion),
            ReleaseUrl = finalUri,
            RpmAsset = new ReleaseAssetInfo($"Custodia-{latestVersion}-1.x86_64.rpm", $"{dlBase}/Custodia-{latestVersion}-1.x86_64.rpm", 0),
            AppImageAsset = new ReleaseAssetInfo($"Custodia-{latestVersion}-x86_64.AppImage", $"{dlBase}/Custodia-{latestVersion}-x86_64.AppImage", 0),
            InstallerScriptAsset = new ReleaseAssetInfo("Custodia-Installer-x86_64.sh", $"{dlBase}/Custodia-Installer-x86_64.sh", 0),
            ChecksumsAsset = new ReleaseAssetInfo("SHA256SUMS.txt", $"{dlBase}/SHA256SUMS.txt", 0),
            InstallKind = installKind,
            InstalledPackagePath = installedPackagePath
        };
    }

    public async Task<DownloadedUpdatePackage> DownloadUpdatePackageAsync(
        AppUpdateCheckResult checkResult,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = checkResult.PreferredAsset
            ?? throw new InvalidOperationException("No compatible release package asset found.");

        PosixPermissionHelper.EnsurePrivateDirectory(_updatesDirectory);

        var safeAssetName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(safeAssetName) || safeAssetName is "." or "..")
        {
            throw new InvalidDataException($"Invalid release asset file name '{asset.Name}'.");
        }

        var targetPath = Path.Combine(_updatesDirectory, safeAssetName);
        var tempPath = targetPath + ".part";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            using var resp = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            resp.EnsureSuccessStatusCode();

            var totalBytes = resp.Content.Headers.ContentLength ?? asset.SizeBytes;
            await using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                var sw = Stopwatch.StartNew();
                var lastReportMs = -250L;

                int read;
                while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;

                    if (sw.ElapsedMilliseconds - lastReportMs >= 150 || (totalBytes > 0 && downloaded >= totalBytes))
                    {
                        lastReportMs = sw.ElapsedMilliseconds;
                        var secs = Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        var mbps = downloaded / (1024.0 * 1024.0) / secs;
                        var pct = totalBytes > 0 ? Math.Clamp((downloaded * 100.0) / totalBytes, 0, 100) : 0;
                        progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes, pct, mbps));
                    }
                }
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            var sha256Verified = false;
            if (checkResult.ChecksumsAsset is not null)
            {
                sha256Verified = await VerifyChecksumAsync(targetPath, safeAssetName, checkResult.ChecksumsAsset.DownloadUrl, cancellationToken);
                if (!sha256Verified)
                {
                    try { File.Delete(targetPath); } catch { }
                    throw new InvalidDataException($"Could not verify SHA-256 signature for '{safeAssetName}' in SHA256SUMS.txt.");
                }
            }

            return new DownloadedUpdatePackage(checkResult, asset, targetPath, sha256Verified);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    private async Task<bool> VerifyChecksumAsync(
        string downloadedFilePath,
        string assetName,
        string checksumsUrl,
        CancellationToken cancellationToken)
    {
        using var resp = await _httpClient.GetAsync(checksumsUrl, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return false;
        }

        var sumsContent = await resp.Content.ReadAsStringAsync(cancellationToken);
        string? expectedHash = null;
        foreach (var rawLine in sumsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var fileToken = parts[^1].TrimStart('*');
            if (string.Equals(Path.GetFileName(fileToken), assetName, StringComparison.Ordinal))
            {
                expectedHash = parts[0].Trim().ToLowerInvariant();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        await using var fs = File.OpenRead(downloadedFilePath);
        var actualBytes = await SHA256.HashDataAsync(fs, cancellationToken);
        var actualHash = Convert.ToHexString(actualBytes).ToLowerInvariant();

        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            try { File.Delete(downloadedFilePath); } catch { }
            throw new InvalidDataException($"SHA-256 checksum mismatch for {assetName}: expected {expectedHash}, got {actualHash}.");
        }

        return true;
    }

    public Task<UpdateApplyLaunchResult> LaunchUpdateInstallerAsync(
        DownloadedUpdatePackage package,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (package.CheckResult.ChecksumsAsset is not null && !package.Sha256Verified)
        {
            throw new InvalidDataException("Refusing to execute update package without verified SHA-256 checksum.");
        }

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new UpdateApplyLaunchResult(false, false, "Automatic installer is currently supported on Linux only."));
        }

        var kind = package.CheckResult.InstallKind;
        var targetAppPath = package.CheckResult.InstalledPackagePath;
        if (kind == AppInstallKind.Standalone ||
            (kind == AppInstallKind.AppImage && string.IsNullOrWhiteSpace(targetAppPath)))
        {
            return Task.FromResult(new UpdateApplyLaunchResult(false, false, package.LocalFilePath));
        }

        Directory.CreateDirectory(_updatesDirectory);
        var scriptPath = Path.Combine(_updatesDirectory, "apply-update.sh");
        var currentPid = Environment.ProcessId;
        var escapedPackage = EscapeShellSingleQuoted(package.LocalFilePath);

        string scriptBody;
        if (kind == AppInstallKind.AppImage)
        {
            var escapedTarget = EscapeShellSingleQuoted(targetAppPath!);
            var needsElevation = !IsDirectoryWritable(Path.GetDirectoryName(targetAppPath!));
            var replaceSnippet = needsElevation
                ? $"""
                  pkexec bash -c 'cp -f {escapedPackage} {EscapeShellSingleQuoted(targetAppPath! + ".tmp")} && chmod 755 {EscapeShellSingleQuoted(targetAppPath! + ".tmp")} && mv -f {EscapeShellSingleQuoted(targetAppPath! + ".tmp")} {escapedTarget}'
                  """
                : $"""
                  cp -f {escapedPackage} {escapedTarget}.tmp
                  chmod 755 {escapedTarget}.tmp
                  mv -f {escapedTarget}.tmp {escapedTarget}
                  """;

            scriptBody = $"""
                #!/usr/bin/env bash
                set -euo pipefail
                PID={currentPid}
                for _ in $(seq 1 150); do
                  if ! kill -0 "$PID" 2>/dev/null; then
                    break
                  fi
                  sleep 0.2
                done
                {replaceSnippet}
                rm -f {escapedPackage}
                nohup {escapedTarget} >/dev/null 2>&1 &
                """;
        }
        else
        {
            var escapedLog = EscapeShellSingleQuoted(Path.Combine(_updatesDirectory, "last-update.log"));
            scriptBody = $"""
                #!/usr/bin/env bash
                set -euo pipefail
                PID={currentPid}
                for _ in $(seq 1 150); do
                  if ! kill -0 "$PID" 2>/dev/null; then
                    break
                  fi
                  sleep 0.2
                done
                if command -v dnf >/dev/null 2>&1; then
                  pkexec dnf install -y {escapedPackage} > {escapedLog} 2>&1
                elif command -v zypper >/dev/null 2>&1; then
                  pkexec zypper --non-interactive install -y --allow-unsigned-rpm {escapedPackage} > {escapedLog} 2>&1
                else
                  pkexec rpm -Uvh --replacepkgs {escapedPackage} > {escapedLog} 2>&1
                fi
                rm -f {escapedPackage}
                if [[ -x /usr/bin/custodia ]]; then
                  nohup /usr/bin/custodia >/dev/null 2>&1 &
                elif [[ -x /usr/bin/tundradrive ]]; then
                  nohup /usr/bin/tundradrive >/dev/null 2>&1 &
                fi
                """;
        }

        File.WriteAllText(scriptPath, scriptBody);
        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"nohup {EscapeShellSingleQuoted(scriptPath)} >/dev/null 2>&1 &");

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return Task.FromResult(new UpdateApplyLaunchResult(false, false, "Failed to launch update helper."));
        }

        _logger.LogInformation("Launched detached self-update helper '{ScriptPath}' for {InstallKind}.", scriptPath, kind);
        return Task.FromResult(new UpdateApplyLaunchResult(true, true, scriptPath));
    }

    private static string EscapeShellSingleQuoted(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static bool IsDirectoryWritable(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
        {
            return false;
        }

        try
        {
            var probe = Path.Combine(dirPath, $".tundra-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe))
            {
            }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
