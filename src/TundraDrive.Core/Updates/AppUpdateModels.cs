namespace TundraDrive.Core.Updates;

public enum AppInstallKind
{
    Standalone = 0,
    Rpm = 1,
    AppImage = 2
}

public sealed record ReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long SizeBytes);

public sealed record AppUpdateCheckResult
{
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string TagName { get; init; }
    public required bool IsUpdateAvailable { get; init; }
    public required string ReleaseUrl { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public ReleaseAssetInfo? RpmAsset { get; init; }
    public ReleaseAssetInfo? AppImageAsset { get; init; }
    public ReleaseAssetInfo? InstallerScriptAsset { get; init; }
    public ReleaseAssetInfo? ChecksumsAsset { get; init; }
    public AppInstallKind InstallKind { get; init; }
    public string? InstalledPackagePath { get; init; }

    public ReleaseAssetInfo? PreferredAsset => InstallKind switch
    {
        AppInstallKind.Rpm => RpmAsset ?? AppImageAsset,
        AppInstallKind.AppImage => AppImageAsset ?? RpmAsset,
        _ => AppImageAsset ?? RpmAsset
    };

    public bool CanSelfInstall =>
        IsUpdateAvailable &&
        ((InstallKind == AppInstallKind.AppImage && AppImageAsset is not null && !string.IsNullOrWhiteSpace(InstalledPackagePath)) ||
         (InstallKind == AppInstallKind.Rpm && RpmAsset is not null));
}

public sealed record UpdateDownloadProgress(
    long BytesDownloaded,
    long TotalBytes,
    double Percentage,
    double MegabytesPerSecond);

public sealed record DownloadedUpdatePackage(
    AppUpdateCheckResult CheckResult,
    ReleaseAssetInfo Asset,
    string LocalFilePath,
    bool Sha256Verified);

public sealed record UpdateApplyLaunchResult(
    bool Launched,
    bool ShouldShutdownApp,
    string Message);
