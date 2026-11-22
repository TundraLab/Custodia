using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IAppUpdateService? _updateService;
    private readonly IAppSettingsService? _settingsService;
    private AppUpdateCheckResult? _latestCheckResult;

    public TimeSpan MinimumCheckAnimationDuration { get; set; } = TimeSpan.FromSeconds(5);

    public ILocalizationService Loc { get; }
    public string CompanyName => "TundraLab";
    public string ProductName => "TundraDrive";
    public string DeveloperName => "Gustavo Henrique Novaes Gomes";
    public string RepositoryUrl => MainWindowViewModel.RepositoryUrl;
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Action? RequestAppShutdownCallback { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckUpdatesButtonText))]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdates))]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdates))]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyPropertyChangedFor(nameof(HasUpdateCheckResult))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private double _updateCheckProgress;

    [ObservableProperty]
    private string _lastCheckSummary = string.Empty;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _installedModeSummary = string.Empty;

    [ObservableProperty]
    private bool _isUpdateError;

    [ObservableProperty]
    private string _latestReleaseUrl = MainWindowViewModel.RepositoryUrl + "/releases/latest";

    [ObservableProperty]
    private string _latestAvailableVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private string _latestReleaseNotes = string.Empty;

    public bool HasReleaseNotes => IsUpdateAvailable && !string.IsNullOrWhiteSpace(LatestReleaseNotes);
    public bool HasUpdateCheckResult => !string.IsNullOrWhiteSpace(_latestCheckResult?.TagName);
    public bool CanCheckUpdates => _updateService is not null && !IsCheckingUpdates && !IsDownloadingUpdate;
    public bool CanInstallUpdate => _updateService is not null && IsUpdateAvailable && !IsCheckingUpdates && !IsDownloadingUpdate && _latestCheckResult?.PreferredAsset is not null;

    public string CheckUpdatesButtonText =>
        IsCheckingUpdates ? Loc.UpdateCheckingButton : Loc.UpdateCheckButton;

    public AboutViewModel(
        ILocalizationService localization,
        IAppUpdateService? updateService = null,
        IAppSettingsService? settingsService = null)
    {
        Loc = localization;
        _updateService = updateService;
        _settingsService = settingsService;
        RefreshInstalledModeSummary();
        RefreshLastCheckSummary();

        Loc.LanguageChanged += (_, _) =>
        {
            RefreshInstalledModeSummary();
            RefreshLastCheckSummary();
            OnPropertyChanged(nameof(CheckUpdatesButtonText));
            RefreshLocalizedStatusFromCache();
        };
    }

    public void RefreshLastCheckSummary()
    {
        var lastUtc = _settingsService?.Current.LastUpdateCheckUtc;
        var relative = lastUtc.HasValue
            ? Loc.FormatRelativeTimestamp(lastUtc)
            : Loc.Text("UpdateLastCheckNever");
        LastCheckSummary = Loc.Format("UpdateLastCheckFmt", relative);
    }

    public void RefreshInstalledModeSummary()
    {
        var kind = _updateService?.DetectInstallKind(out _) ?? AppInstallKind.Standalone;
        var modeText = kind switch
        {
            AppInstallKind.Rpm => Loc.Text("InstallKindRpm"),
            AppInstallKind.AppImage => Loc.Text("InstallKindAppImage"),
            _ => Loc.Text("InstallKindStandalone")
        };

        InstalledModeSummary = Loc.Format("UpdateCurrentVersionFmt", AppVersionInfo.CurrentVersion, modeText);
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(RepositoryUrl));
        }
    }

    [RelayCommand]
    private async Task OpenReleasePageAsync()
    {
        if (OpenExternalUriCallback is not null &&
            Uri.TryCreate(LatestReleaseUrl, UriKind.Absolute, out var uri))
        {
            await OpenExternalUriCallback.Invoke(uri);
        }
    }

    [RelayCommand]
    public Task<AppUpdateCheckResult?> CheckForUpdatesAsync() =>
        CheckForUpdatesInternalAsync(useSearchAnimation: true);

    public Task<AppUpdateCheckResult?> CheckForUpdatesQuietlyAsync() =>
        CheckForUpdatesInternalAsync(useSearchAnimation: false);

    private async Task<AppUpdateCheckResult?> CheckForUpdatesInternalAsync(bool useSearchAnimation)
    {
        if (_updateService is null || IsCheckingUpdates || IsDownloadingUpdate)
        {
            return _latestCheckResult;
        }

        IsCheckingUpdates = true;
        IsUpdateError = false;
        UpdateCheckProgress = 0;

        try
        {
            var checkTask = _updateService.CheckForUpdatesAsync();

            if (useSearchAnimation && MinimumCheckAnimationDuration > TimeSpan.Zero)
            {
                await RunSearchAnimationAsync(checkTask, MinimumCheckAnimationDuration);
            }

            var result = await checkTask;
            await RecordLastCheckTimestampAsync();
            ApplyCheckResult(result);
            return result;
        }
        catch (Exception ex)
        {
            IsUpdateError = true;
            UpdateStatusText = Loc.Format("UpdateErrorFmt", ex.Message);
            return null;
        }
        finally
        {
            UpdateCheckProgress = 0;
            IsCheckingUpdates = false;
        }
    }

    private async Task RunSearchAnimationAsync(Task checkTask, TimeSpan duration)
    {
        var totalMs = Math.Max(100.0, duration.TotalMilliseconds);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < duration || !checkTask.IsCompleted)
        {
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var pct = Math.Clamp((elapsedMs / totalMs) * 100.0, 2.0, 99.0);
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((totalMs - elapsedMs) / 1000.0));

            var phaseText = pct < 34.0
                ? Loc.Format("UpdateSearchPhase1Fmt", remainingSeconds)
                : pct < 68.0
                    ? Loc.Format("UpdateSearchPhase2Fmt", remainingSeconds)
                    : Loc.Format("UpdateSearchPhase3Fmt", AppVersionInfo.CurrentVersion, remainingSeconds);

            UpdateCheckProgress = pct;
            UpdateStatusText = phaseText;

            var nextTickDelay = elapsedMs >= totalMs ? 80 : 100;
            await Task.Delay(nextTickDelay);
        }

        UpdateCheckProgress = 100;
    }

    private async Task RecordLastCheckTimestampAsync()
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            var current = _settingsService.Current;
            current.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            await _settingsService.SaveSilentlyAsync(current);
            RefreshLastCheckSummary();
        }
        catch
        {
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_updateService is null || _latestCheckResult is null || IsDownloadingUpdate)
        {
            return;
        }

        IsDownloadingUpdate = true;
        IsUpdateError = false;
        UpdateDownloadProgress = 0;

        var assetName = _latestCheckResult.PreferredAsset?.Name ?? "TundraDrive";
        UpdateStatusText = Loc.Format("UpdateDownloadingFmt", assetName, 0.0, 0.0, 0.0, 0.0);

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateDownloadProgress = p.Percentage;
                    var downloadedMb = p.BytesDownloaded / (1024.0 * 1024.0);
                    var totalMb = p.TotalBytes > 0 ? p.TotalBytes / (1024.0 * 1024.0) : downloadedMb;
                    UpdateStatusText = Loc.Format("UpdateDownloadingFmt", assetName, p.Percentage, downloadedMb, totalMb, p.MegabytesPerSecond);
                });
            });

            var downloaded = await _updateService.DownloadUpdatePackageAsync(_latestCheckResult, progress);
            var launchResult = await _updateService.LaunchUpdateInstallerAsync(downloaded);

            if (launchResult.ShouldShutdownApp)
            {
                UpdateDownloadProgress = 100;
                UpdateStatusText = Loc.Text("UpdateInstallingStatus");
                await Task.Delay(350);
                RequestAppShutdownCallback?.Invoke();
            }
            else
            {
                UpdateDownloadProgress = 100;
                UpdateStatusText = Loc.Format("UpdateDownloadedFolderStatusFmt", downloaded.LocalFilePath);
            }
        }
        catch (Exception ex)
        {
            IsUpdateError = true;
            UpdateStatusText = Loc.Format("UpdateErrorFmt", ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    internal void ApplyCheckResult(AppUpdateCheckResult result)
    {
        _latestCheckResult = result;
        LatestAvailableVersion = result.LatestVersion;
        LatestReleaseUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl)
            ? RepositoryUrl + "/releases/latest"
            : result.ReleaseUrl;
        IsUpdateAvailable = result.IsUpdateAvailable;
        LatestReleaseNotes = FormatReleaseNotesForDisplay(result.ReleaseNotes);
        IsUpdateError = false;
        RefreshLocalizedStatusFromCache();
        OnPropertyChanged(nameof(HasReleaseNotes));
        OnPropertyChanged(nameof(HasUpdateCheckResult));
        OnPropertyChanged(nameof(CanInstallUpdate));
    }

    internal static string FormatReleaseNotesForDisplay(string? rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes))
        {
            return string.Empty;
        }

        var lines = rawNotes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var formatted = new List<string>(lines.Length);
        var previousBlank = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                if (formatted.Count > 0 && !previousBlank)
                {
                    formatted.Add(string.Empty);
                    previousBlank = true;
                }
                continue;
            }

            line = line.Replace("**", string.Empty, StringComparison.Ordinal)
                       .Replace("__", string.Empty, StringComparison.Ordinal)
                       .Replace("`", string.Empty, StringComparison.Ordinal);

            if (line.StartsWith('#'))
            {
                line = line.TrimStart('#').Trim();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                line = "• " + line[2..].TrimStart();
            }

            if (!string.IsNullOrEmpty(line))
            {
                formatted.Add(line);
                previousBlank = false;
            }
        }

        return string.Join('\n', formatted).Trim();
    }

    private void RefreshLocalizedStatusFromCache()
    {
        if (_latestCheckResult is null)
        {
            return;
        }

        UpdateStatusText = _latestCheckResult.IsUpdateAvailable
            ? Loc.Format("UpdateAvailableFmt", _latestCheckResult.LatestVersion)
            : Loc.Format("UpdateUpToDateFmt", _latestCheckResult.CurrentVersion);
    }
}
