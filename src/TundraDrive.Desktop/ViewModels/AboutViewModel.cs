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
    private AppUpdateCheckResult? _latestCheckResult;

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
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _installedModeSummary = string.Empty;

    [ObservableProperty]
    private bool _isUpdateError;

    [ObservableProperty]
    private string _latestReleaseUrl = MainWindowViewModel.RepositoryUrl + "/releases/latest";

    [ObservableProperty]
    private string _latestAvailableVersion = string.Empty;

    public bool HasUpdateCheckResult => !string.IsNullOrWhiteSpace(_latestCheckResult?.TagName);
    public bool CanCheckUpdates => _updateService is not null && !IsCheckingUpdates && !IsDownloadingUpdate;
    public bool CanInstallUpdate => _updateService is not null && IsUpdateAvailable && !IsCheckingUpdates && !IsDownloadingUpdate && _latestCheckResult?.PreferredAsset is not null;

    public string CheckUpdatesButtonText =>
        IsCheckingUpdates ? Loc.UpdateCheckingButton : Loc.UpdateCheckButton;

    public AboutViewModel(ILocalizationService localization, IAppUpdateService? updateService = null)
    {
        Loc = localization;
        _updateService = updateService;
        RefreshInstalledModeSummary();

        Loc.LanguageChanged += (_, _) =>
        {
            RefreshInstalledModeSummary();
            OnPropertyChanged(nameof(CheckUpdatesButtonText));
            RefreshLocalizedStatusFromCache();
        };
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
    public async Task<AppUpdateCheckResult?> CheckForUpdatesAsync()
    {
        if (_updateService is null || IsCheckingUpdates || IsDownloadingUpdate)
        {
            return _latestCheckResult;
        }

        IsCheckingUpdates = true;
        IsUpdateError = false;
        UpdateStatusText = Loc.Text("UpdateCheckingStatus");

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
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
            IsCheckingUpdates = false;
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
        IsUpdateError = false;
        RefreshLocalizedStatusFromCache();
        OnPropertyChanged(nameof(HasUpdateCheckResult));
        OnPropertyChanged(nameof(CanInstallUpdate));
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
