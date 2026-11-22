using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public const string RepositoryUrl = "https://github.com/TundraLab/TundraDrive";

    private readonly IDriveSyncService _syncService;
    private readonly IEmbeddedApiServer _apiServer;
    private readonly IAppSettingsService _settingsService;
    private readonly IAutostartManager _autostartManager;
    private readonly IDesktopNotificationService _notificationService;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private int _selectedNavigationIndex = 1;

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    [ObservableProperty]
    private string _sidebarAccountText = string.Empty;

    [ObservableProperty]
    private string _sidebarSyncStatusText = string.Empty;

    [ObservableProperty]
    private bool _isSidebarCompact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarGenericSyncing))]
    [NotifyPropertyChangedFor(nameof(IsSidebarUploading))]
    [NotifyPropertyChangedFor(nameof(IsSidebarDownloading))]
    [NotifyPropertyChangedFor(nameof(IsSidebarDeleting))]
    [NotifyPropertyChangedFor(nameof(SidebarSyncAccentBrush))]
    private bool _isSyncActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarGenericSyncing))]
    [NotifyPropertyChangedFor(nameof(IsSidebarUploading))]
    [NotifyPropertyChangedFor(nameof(IsSidebarDownloading))]
    [NotifyPropertyChangedFor(nameof(IsSidebarDeleting))]
    [NotifyPropertyChangedFor(nameof(SidebarSyncAccentBrush))]
    private TransferDirection? _activeSidebarDirection;

    private static readonly IBrush SidebarAccentUpload = SolidColorBrush.Parse("#3B82F6");
    private static readonly IBrush SidebarAccentDownload = SolidColorBrush.Parse("#2EA043");
    private static readonly IBrush SidebarAccentDelete = SolidColorBrush.Parse("#EF4444");

    public bool IsSidebarUploading => IsSyncActive && ActiveSidebarDirection == TransferDirection.Upload;
    public bool IsSidebarDownloading => IsSyncActive && ActiveSidebarDirection == TransferDirection.Download;
    public bool IsSidebarDeleting => IsSyncActive && ActiveSidebarDirection == TransferDirection.Delete;
    public bool IsSidebarGenericSyncing => IsSyncActive && ActiveSidebarDirection is null;

    public IBrush SidebarSyncAccentBrush => ActiveSidebarDirection switch
    {
        TransferDirection.Download => SidebarAccentDownload,
        TransferDirection.Delete => SidebarAccentDelete,
        _ => SidebarAccentUpload
    };

    private bool? _lastKnownOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountAvatar))]
    private Bitmap? _accountAvatarBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScaleFactor))]
    private int _uiScalePercent = 100;

    [ObservableProperty]
    private bool _isUpdateBannerVisible;

    [ObservableProperty]
    private string _updateBannerText = string.Empty;

    private bool _updateNotificationSent;

    public bool HasAccountAvatar => AccountAvatarBitmap is not null;
    public double UiScaleFactor => Math.Clamp(UiScalePercent, 100, 300) / 100.0;

    private bool _userPinnedCompact;

    public double SidebarWidth => IsSidebarCompact ? 60.0 : 254.0;

    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }

    public AccountsViewModel AccountsVm { get; }
    public FileHubViewModel FileHubVm { get; }
    public SyncFoldersViewModel SyncFoldersVm { get; }
    public TransferQueueViewModel TransferQueueVm { get; }
    public ActivityLogsViewModel ActivityLogsVm { get; }
    public SettingsViewModel SettingsVm { get; }
    public AboutViewModel AboutVm { get; }

    public MainWindowViewModel(
        IDriveSyncService syncService,
        IEmbeddedApiServer apiServer,
        IAppSettingsService settingsService,
        IAutostartManager autostartManager,
        ILocalizationService localization,
        IDesktopNotificationService notificationService,
        AccountsViewModel accountsVm,
        FileHubViewModel fileHubVm,
        SyncFoldersViewModel syncFoldersVm,
        TransferQueueViewModel transferQueueVm,
        ActivityLogsViewModel activityLogsVm,
        SettingsViewModel settingsVm,
        AboutViewModel aboutVm)
    {
        _syncService = syncService;
        _apiServer = apiServer;
        _settingsService = settingsService;
        _autostartManager = autostartManager;
        _notificationService = notificationService;
        Loc = localization;

        AccountsVm = accountsVm;
        FileHubVm = fileHubVm;
        SyncFoldersVm = syncFoldersVm;
        TransferQueueVm = transferQueueVm;
        ActivityLogsVm = activityLogsVm;
        SettingsVm = settingsVm;
        AboutVm = aboutVm;

        _currentViewModel = SyncFoldersVm;

        _syncService.AccountChanged += OnAccountChanged;
        _syncService.SyncFoldersChanged += OnSyncFoldersChanged;
        _syncService.TransferUpdated += OnTransferUpdated;
        _syncService.ActivityLogged += OnActivityLogged;
        _syncService.ConnectivityChanged += OnConnectivityChanged;
        Loc.LanguageChanged += (_, _) => UpdateAccountSummary(_syncService.CurrentAccount);
        SettingsVm.UiScaleChanged += (_, scale) => UiScalePercent = scale;
        UiScalePercent = Math.Clamp(_settingsService.Current.UiScalePercent, 100, 300);

        UpdateAccountSummary(_syncService.CurrentAccount);
    }

    partial void OnIsSidebarCompactChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
    }

    partial void OnSelectedNavigationIndexChanged(int value)
    {
        CurrentViewModel = value switch
        {
            0 => FileHubVm,
            1 => SyncFoldersVm,
            2 => TransferQueueVm,
            3 => ActivityLogsVm,
            4 => SettingsVm,
            5 => AboutVm,
            _ => SyncFoldersVm
        };

        if (value == 0)
        {
            _ = FileHubVm.RefreshAsync();
        }
        else
        {
            FileHubVm.ReleaseHeavyMediaResources();
            if (value == 3)
            {
                _ = ActivityLogsVm.RefreshLogsAsync();
            }
            else if (value == 4)
            {
                SettingsVm.LoadFromCurrent();
            }
        }
    }

    [RelayCommand]
    private void OpenTrashFromSidebar()
    {
        SelectedNavigationIndex = 0;
        FileHubVm.SelectedSourceFilterIndex = 2;
    }

    [RelayCommand]
    private void OpenUpdatesFromBanner()
    {
        SelectedNavigationIndex = 5;
        IsUpdateBannerVisible = false;
    }

    [RelayCommand]
    private void DismissUpdateBanner()
    {
        IsUpdateBannerVisible = false;
    }

    [RelayCommand]
    private void ToggleSidebarCompact()
    {
        _userPinnedCompact = !IsSidebarCompact;
        IsSidebarCompact = _userPinnedCompact;
    }

    public void UpdateResponsiveLayout(double windowWidth)
    {
        if (_userPinnedCompact)
        {
            return;
        }

        IsSidebarCompact = windowWidth < 820;
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(RepositoryUrl));
        }
    }

    public async Task TriggerSyncFromTrayAsync()
    {
        await _syncService.TriggerSyncAllAsync();
    }

    public async Task TogglePauseAllFromTrayAsync()
    {
        var folders = await _syncService.GetSyncFoldersAsync();
        var anyActive = folders.Any(f => !f.IsPaused);
        foreach (var folder in folders)
        {
            await _syncService.SetFolderPausedAsync(folder.Id, anyActive);
        }
    }

    public string GetTrayAccountLine() =>
        Loc.Format("TrayAccountFmt", SidebarAccountText);

    public string GetTrayStatusLine() =>
        Loc.Format("TrayStatusFmt", SidebarSyncStatusText);

    public string GetTrayStorageLine()
    {
        var account = _syncService.CurrentAccount;
        if (!account.IsConnected)
        {
            return Loc.Text("TrayStorageDisconnected");
        }

        var used = UiConverters.FormatBytes(account.Quota.UsageBytes);
        var limit = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : Loc.Text("Unlimited");
        return Loc.Format("TrayStorageFmt", used, limit, account.Quota.UsagePercentage);
    }

    public string GetTrayFoldersTransfersLine()
    {
        var activeFolders = SyncFoldersVm.Folders.Count(f => !f.IsPaused);
        var activeTransfers = TransferQueueVm.ActiveCount;
        return Loc.Format("TrayFoldersTransfersFmt", activeFolders, activeTransfers);
    }

    public bool HasAnyActiveSyncFolder() => SyncFoldersVm.Folders.Any(f => !f.IsPaused);

    public string GetDownloadDirectory()
    {
        var dir = _settingsService.Current.DownloadDirectory;
        return string.IsNullOrWhiteSpace(dir) ? AppPaths.DefaultDownloadDirectory : dir;
    }

    public event EventHandler? TraySummaryChanged;

    public async Task InitializeAsync()
    {
        await _syncService.InitializeAsync();
        _lastKnownOnline = _syncService.IsOnline;
        await _apiServer.StartAsync();

        var settings = _settingsService.Current;
        if (settings.StartOnBoot && !_autostartManager.IsEnabled())
        {
            _autostartManager.SetEnabled(true);
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Loc.SetLanguage(settings.Language);
            UiScalePercent = Math.Clamp(settings.UiScalePercent, 100, 300);
            UpdateAccountSummary(_syncService.CurrentAccount);
            await SyncFoldersVm.RefreshFoldersAsync();
            if (SelectedNavigationIndex == 0)
            {
                await FileHubVm.RefreshAsync();
            }
            await ActivityLogsVm.RefreshLogsAsync();
            SettingsVm.LoadFromCurrent();
        });

        if (settings.AutoCheckUpdates)
        {
            _ = CheckForUpdatesOnStartupAsync();
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(3000);
            var result = await AboutVm.CheckForUpdatesAsync();
            if (result is { IsUpdateAvailable: true })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateBannerText = Loc.Format("UpdateBannerTitleFmt", result.LatestVersion);
                    IsUpdateBannerVisible = true;
                });

                if (!_updateNotificationSent && _settingsService.Current.NotificationsEnabled)
                {
                    _updateNotificationSent = true;
                    await _notificationService.NotifyAsync(
                        Loc.Format("UpdateNotifyTitleFmt", result.LatestVersion),
                        Loc.Text("UpdateNotifyBody"));
                }
            }
        }
        catch
        {
            // Quiet startup update check should never interrupt sync startup.
        }
    }

    private void OnAccountChanged(object? sender, DriveAccount account)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            UpdateAccountSummary(account);
            await SyncFoldersVm.RefreshFoldersAsync();
            if (account.IsConnected)
            {
                if (SelectedNavigationIndex == 0)
                {
                    await FileHubVm.RefreshAsync();
                }
                if (SyncFoldersVm.IsRemoteBrowserActive)
                {
                    await SyncFoldersVm.RemoteBrowser.LoadCurrentFolderAsync();
                }
            }
            else
            {
                FileHubVm.ClearWhenDisconnected();
                FileHubVm.SelectedSourceFilterIndex = 1;
                SyncFoldersVm.RemoteBrowser.ClearWhenDisconnected();
            }
        });
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var previousOnline = _lastKnownOnline;
            _lastKnownOnline = isOnline;

            UpdateAccountSummary(_syncService.CurrentAccount);
            if (!isOnline)
            {
                if (previousOnline != false && _syncService.CurrentAccount.IsConnected)
                {
                    _ = _notificationService.NotifyAsync(
                        Loc.Text("NotifyOfflineTitle"),
                        Loc.Text("NotifyOfflineBody"),
                        NotificationUrgency.Normal,
                        bypassThrottle: true);
                }

                FileHubVm.SelectedSourceFilterIndex = 1;
                await FileHubVm.RefreshAsync();
                SyncFoldersVm.RemoteBrowser.ClearWhenOffline();
            }
            else if (_syncService.CurrentAccount.IsConnected)
            {
                if (previousOnline == false)
                {
                    _ = _notificationService.NotifyAsync(
                        Loc.Text("NotifyOnlineTitle"),
                        Loc.Text("NotifyOnlineBody"),
                        NotificationUrgency.Normal,
                        bypassThrottle: true);
                }

                if (SelectedNavigationIndex == 0)
                {
                    await FileHubVm.RefreshAsync();
                }
                if (SyncFoldersVm.IsRemoteBrowserActive)
                {
                    await SyncFoldersVm.RemoteBrowser.LoadCurrentFolderAsync();
                }
            }
        });
    }

    private void OnSyncFoldersChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await SyncFoldersVm.RefreshFoldersAsync();
            RefreshSidebarSyncState("SidebarMonitoring");

            if (!_syncService.IsSyncRunning && SelectedNavigationIndex == 0)
            {
                await FileHubVm.RefreshAsync();
            }
            TraySummaryChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private readonly Dictionary<string, TransferStatus> _lastUiStatusById = new();
    private long _lastProgressTickMs;
    private TransferDirection? _heldSidebarDirection;
    private long _sidebarHoldUntilMs;
    private long _sidebarHoldVersion;

    private void OnTransferUpdated(object? sender, TransferQueueItem item)
    {
        var nowMs = Environment.TickCount64;
        var hasPrev = _lastUiStatusById.TryGetValue(item.Id, out var prevStatus);
        var isRepeatedProgressTick = item.Status == TransferStatus.InProgress
            && hasPrev
            && prevStatus == TransferStatus.InProgress
            && nowMs - _lastProgressTickMs < 100;

        if (isRepeatedProgressTick)
        {
            return;
        }

        _lastUiStatusById[item.Id] = item.Status;
        if (_lastUiStatusById.Count > 300)
        {
            _lastUiStatusById.Clear();
        }
        if (item.Status == TransferStatus.InProgress)
        {
            _lastProgressTickMs = nowMs;
        }

        var isBrandNewActivity = item.Status is TransferStatus.Queued or TransferStatus.InProgress
            || (!hasPrev && item.Status == TransferStatus.Completed);

        Dispatcher.UIThread.Post(() =>
        {
            var prevActive = TransferQueueVm.ActiveCount;
            var prevDirection = TransferQueueVm.ActiveDirection;

            if (isBrandNewActivity)
            {
                _heldSidebarDirection = item.Direction;
                _sidebarHoldUntilMs = Environment.TickCount64 + 1800;
                var version = ++_sidebarHoldVersion;
                _ = ReleaseSidebarDirectionHoldAsync(version, 1850);
            }

            TransferQueueVm.UpsertTransfer(item);
            RefreshSidebarSyncState();
            if (prevActive != TransferQueueVm.ActiveCount || prevDirection != TransferQueueVm.ActiveDirection)
            {
                TraySummaryChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private async Task ReleaseSidebarDirectionHoldAsync(long version, int delayMs)
    {
        await Task.Delay(delayMs);
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _sidebarHoldVersion)
            {
                return;
            }

            _heldSidebarDirection = null;
            RefreshSidebarSyncState("SidebarMonitoring");
            TraySummaryChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnActivityLogged(object? sender, SyncActivityLog log)
    {
        Dispatcher.UIThread.Post(() => ActivityLogsVm.PrependLiveLog(log));
        _ = DispatchNotificationForLogAsync(log);
    }

    internal Task DispatchNotificationForLogAsync(SyncActivityLog log)
    {
        var body = ActivityLogItemViewModel.FormatLocalizedMessage(log, Loc);

        return log.Action switch
        {
            "AccountConnect" => _notificationService.NotifyAsync(
                Loc.Text("NotifyConnectedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "AccountSwitch" => _notificationService.NotifyAsync(
                Loc.Text("NotifyAccountSwitchedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "AccountDisconnect" => _notificationService.NotifyAsync(
                Loc.Text("NotifyAccountDisconnectedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "CredentialsRevoked" or "CredentialsJsonRevoked" => _notificationService.NotifyAsync(
                Loc.Text("NotifyCredentialsRevokedTitle"),
                body,
                NotificationUrgency.Critical,
                bypassThrottle: true),
            "FolderMapped" => _notificationService.NotifyAsync(
                Loc.Text("NotifyFolderMappedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "FolderUnmapped" => _notificationService.NotifyAsync(
                Loc.Text("NotifyFolderUnmappedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "ModeChanged" => _notificationService.NotifyAsync(
                Loc.Text("NotifyModeChangedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "SyncCompleted" when !log.Message.Contains("sem alterações", StringComparison.OrdinalIgnoreCase) =>
                _notificationService.NotifyAsync(
                    Loc.Text("NotifyFileSyncedTitle"),
                    body,
                    NotificationUrgency.Low,
                    targetPath: log.TargetPath),
            "Uploaded" or "ApiUpload" => _notificationService.NotifyAsync(
                Loc.Text("NotifyUploadedTitle"),
                body,
                NotificationUrgency.Low,
                targetPath: log.TargetPath),
            "Downloaded" => _notificationService.NotifyAsync(
                Loc.Text("NotifyDownloadedTitle"),
                body,
                NotificationUrgency.Low,
                targetPath: log.TargetPath),
            "ConflictRenamed" => _notificationService.NotifyAsync(
                Loc.Text("NotifyConflictTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "FolderPaused" => _notificationService.NotifyAsync(
                Loc.Text("NotifyFolderPausedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "FolderResumed" => _notificationService.NotifyAsync(
                Loc.Text("NotifyFolderResumedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "LocalTrashed" => _notificationService.NotifyAsync(
                Loc.Text("NotifyLocalTrashedTitle"),
                body,
                NotificationUrgency.Normal,
                targetPath: log.TargetPath),
            "RemoteTrashed" => _notificationService.NotifyAsync(
                Loc.Text("NotifyRemoteTrashedTitle"),
                body,
                NotificationUrgency.Normal),
            "RemoteRestored" => _notificationService.NotifyAsync(
                Loc.Text("NotifyRestoredTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "RemoteDeletedPermanent" => _notificationService.NotifyAsync(
                Loc.Text("NotifyDeletedPermanentTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "TrashEmptied" => _notificationService.NotifyAsync(
                Loc.Text("NotifyTrashEmptiedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true),
            "ManualDownload" => _notificationService.NotifyAsync(
                Loc.Text("NotifyDownloadedTitle"),
                body,
                NotificationUrgency.Normal,
                bypassThrottle: true,
                targetPath: log.TargetPath),
            "ManualDownloadFailed" => _notificationService.NotifyAsync(
                Loc.Text("NotifyDownloadFailedTitle"),
                body,
                NotificationUrgency.Critical,
                bypassThrottle: true),
            "SyncError" or "ApiUploadFailed" => _notificationService.NotifyAsync(
                Loc.Text("NotifySyncErrorTitle"),
                body,
                NotificationUrgency.Critical,
                bypassThrottle: true),
            _ => Task.CompletedTask
        };
    }

    private void RefreshSidebarSyncState(string idleKey = "SidebarReady")
    {
        var account = _syncService.CurrentAccount;
        var activeDirection = TransferQueueVm.ActiveDirection;
        var heldDirection = activeDirection is null
            && _heldSidebarDirection is not null
            && Environment.TickCount64 < _sidebarHoldUntilMs
            ? _heldSidebarDirection
            : null;
        var effectiveDirection = activeDirection ?? heldDirection;

        IsSyncActive = _syncService.IsSyncRunning || TransferQueueVm.ActiveCount > 0 || heldDirection is not null;
        ActiveSidebarDirection = effectiveDirection;

        if (!account.IsConnected)
        {
            SidebarSyncStatusText = Loc.Text("SidebarWaitingAuth");
            return;
        }

        if (!_syncService.IsOnline)
        {
            SidebarSyncStatusText = Loc.Text("SidebarOffline");
            return;
        }

        if (IsSyncActive)
        {
            SidebarSyncStatusText = effectiveDirection switch
            {
                TransferDirection.Upload => Loc.Text("SidebarUploading"),
                TransferDirection.Download => Loc.Text("SidebarDownloading"),
                TransferDirection.Delete => Loc.Text("SidebarDeleting"),
                _ => Loc.Text("SidebarSyncing")
            };
            return;
        }

        SidebarSyncStatusText = Loc.Text(idleKey);
    }

    private void UpdateAccountSummary(DriveAccount account)
    {
        AccountsVm.ApplyAccount(account);
        FileHubVm.ApplyAccount(account);
        SyncFoldersVm.ApplyAccount(account);
        SidebarAccountText = account.IsConnected
            ? account.EmailAddress
            : Loc.Text("SidebarDisconnected");

        if (account.IsConnected && !string.IsNullOrWhiteSpace(account.PhotoUrl))
        {
            _ = LoadSidebarAvatarAsync(account.PhotoUrl);
        }
        else if (!account.IsConnected)
        {
            AccountAvatarBitmap = null;
        }
        RefreshSidebarSyncState("SidebarReady");

        if (account.IsConnected && account.Quota.LimitBytes > 0 && account.Quota.UsagePercentage >= 80.0)
        {
            var isFull = account.Quota.UsageBytes >= account.Quota.LimitBytes || account.Quota.UsagePercentage >= 99.5;
            var alertBody = isFull
                ? Loc.Format("StorageFullFmt", account.Quota.UsagePercentage)
                : Loc.Format("StorageWarningFmt", account.Quota.UsagePercentage);
            _ = _notificationService.NotifyAsync(
                Loc.Text("StorageLabel"),
                alertBody,
                isFull ? NotificationUrgency.Critical : NotificationUrgency.Normal);
        }

        TraySummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadSidebarAvatarAsync(string photoUrl)
    {
        var bmp = await AvatarLoader.LoadAsync(photoUrl);
        if (bmp is not null)
        {
            Dispatcher.UIThread.Post(() => AccountAvatarBitmap = bmp);
        }
    }

    public void Dispose()
    {
        _syncService.AccountChanged -= OnAccountChanged;
        _syncService.SyncFoldersChanged -= OnSyncFoldersChanged;
        _syncService.TransferUpdated -= OnTransferUpdated;
        _syncService.ActivityLogged -= OnActivityLogged;
        _syncService.ConnectivityChanged -= OnConnectivityChanged;
        FileHubVm.Dispose();
    }
}
