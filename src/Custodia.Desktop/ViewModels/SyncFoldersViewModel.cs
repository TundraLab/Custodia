using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class SyncFolderItemViewModel : ViewModelBase
{
    private readonly SyncFoldersViewModel _parent;

    public ILocalizationService Loc { get; }
    public string Id { get; }
    public string LocalPath { get; }
    public string RemoteFolderId { get; }
    public string RemoteFolderName { get; }

    [ObservableProperty]
    private SyncMode _mode;

    [ObservableProperty]
    private FolderSyncState _state;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private DateTimeOffset? _lastSyncedAtUtc;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    private int _trackedFileCount;

    public SyncFolderItemViewModel(
        LocalSyncFolder model,
        SyncFoldersViewModel parent,
        ILocalizationService localization)
    {
        _parent = parent;
        Loc = localization;
        Id = model.Id;
        LocalPath = model.LocalPath;
        RemoteFolderId = model.RemoteFolderId;
        RemoteFolderName = model.RemoteFolderName;
        _mode = model.Mode;
        _state = model.State;
        _isPaused = model.IsPaused;
        _lastSyncedAtUtc = model.LastSyncedAtUtc;
        _lastErrorMessage = model.LastErrorMessage;
        _trackedFileCount = model.TrackedFileCount;
    }

    public void UpdateFrom(LocalSyncFolder model)
    {
        Mode = model.Mode;
        State = model.State;
        IsPaused = model.IsPaused;
        LastSyncedAtUtc = model.LastSyncedAtUtc;
        LastErrorMessage = model.LastErrorMessage;
        TrackedFileCount = model.TrackedFileCount;
        RefreshLocalizedProperties();
        OnPropertyChanged(nameof(IsSyncing));
        OnPropertyChanged(nameof(IsTwoWayMode));
        OnPropertyChanged(nameof(AccentBorderBrush));
    }

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(IsTwoWayMode));
        OnPropertyChanged(nameof(DirectionBadgeText));
        OnPropertyChanged(nameof(DirectionExplanation));
        OnPropertyChanged(nameof(RemoteDestinationDisplay));
        OnPropertyChanged(nameof(SyncModeDisplay));
        OnPropertyChanged(nameof(ModeToggleButtonLabel));
        OnPropertyChanged(nameof(PauseButtonLabel));
        OnPropertyChanged(nameof(LastSyncedDisplay));
        OnPropertyChanged(nameof(StateDisplay));
    }

    public bool IsSyncing => State == FolderSyncState.Syncing;
    public bool IsTwoWayMode => Mode == SyncMode.TwoWay;

    public string DirectionBadgeText => IsTwoWayMode
        ? Loc.Text("DirectionBadgeTwoWay")
        : Loc.Text("DirectionBadgeOneWay");

    public string DirectionExplanation => IsTwoWayMode
        ? Loc.Text("DirectionExplainTwoWay")
        : Loc.Text("DirectionExplainOneWay");

    public string RemoteDestinationDisplay => Loc.Format("RemoteDestFmt", RemoteFolderName);

    public string SyncModeDisplay =>
        Mode == SyncMode.TwoWay ? Loc.Text("ModeTwoWay") : Loc.Text("ModeOneWay");

    public string ModeToggleButtonLabel =>
        Mode == SyncMode.TwoWay ? Loc.Text("SwitchToBackup") : Loc.Text("SwitchToTwoWay");

    public string PauseButtonLabel => IsPaused ? Loc.Text("Resume") : Loc.Text("Pause");

    public string LastSyncedDisplay =>
        Loc.Format("LastSyncedFmt", Loc.FormatRelativeTimestamp(LastSyncedAtUtc), TrackedFileCount);

    public string StateDisplay => State switch
    {
        FolderSyncState.Syncing => Loc.Text("StateSyncing"),
        FolderSyncState.Paused => Loc.Text("StatePaused"),
        FolderSyncState.Error => string.IsNullOrWhiteSpace(LastErrorMessage)
            ? Loc.Text("StateErrorDefault")
            : Loc.Format("StateErrorFmt", LastErrorMessage),
        _ => Loc.Text("StateIdle")
    };

    public IBrush AccentBorderBrush => State switch
    {
        FolderSyncState.Syncing => SolidColorBrush.Parse("#3B82F6"),
        FolderSyncState.Paused => SolidColorBrush.Parse("#6B7280"),
        FolderSyncState.Error => SolidColorBrush.Parse("#EF4444"),
        _ => SolidColorBrush.Parse("#2EA043")
    };

    [RelayCommand]
    private Task SyncNowAsync() => _parent.SyncSingleFolderAsync(Id);

    [RelayCommand]
    private Task TogglePauseAsync() => _parent.TogglePauseFolderAsync(Id, !IsPaused);

    [RelayCommand]
    private Task ToggleModeAsync()
    {
        var nextMode = Mode == SyncMode.TwoWay ? SyncMode.OneWayBackup : SyncMode.TwoWay;
        return _parent.ChangeFolderModeAsync(Id, nextMode);
    }

    [RelayCommand]
    private Task OpenLocalFolderAsync() => _parent.OpenLocalFolderAsync(LocalPath);

    [RelayCommand]
    private Task RemoveAsync() => _parent.RemoveFolderAsync(Id);
}

public sealed partial class SyncFoldersViewModel : ViewModelBase
{
    private const string GoogleOneStorageUrl = "https://one.google.com/storage";

    private readonly IDriveSyncService _syncService;
    private readonly IGoogleDriveClient _driveClient;
    private DriveAccount _lastAccount = new();
    private double _storageTrackWidth = 320.0;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private bool _isRemoteBrowserActive;

    [ObservableProperty]
    private bool _isAccountConnected;

    [ObservableProperty]
    private double _quotaPercentage;

    [ObservableProperty]
    private string _quotaSummaryText = string.Empty;

    [ObservableProperty]
    private bool _hasStorageAlert;

    [ObservableProperty]
    private string _storageAlertMessage = string.Empty;

    [ObservableProperty]
    private IBrush _storageProgressBrush = SolidColorBrush.Parse("#3B82F6");

    [ObservableProperty]
    private IBrush _storageAlertBorderBrush = SolidColorBrush.Parse("#2A303B");

    [ObservableProperty]
    private IBrush _storageAlertTextBrush = SolidColorBrush.Parse("#D29922");

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasNoFolders = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(SelectedPauseLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedModeToggleLabel))]
    private SyncFolderItemViewModel? _selectedFolder;

    [ObservableProperty]
    private double _driveBarWidth;

    [ObservableProperty]
    private double _otherBarWidth;

    [ObservableProperty]
    private double _trashBarWidth;

    [ObservableProperty]
    private string _driveLegendText = string.Empty;

    [ObservableProperty]
    private string _otherLegendText = string.Empty;

    [ObservableProperty]
    private string _trashLegendText = string.Empty;

    public bool HasSelectedFolder => SelectedFolder is not null;
    public string SelectedPauseLabel => SelectedFolder?.PauseButtonLabel ?? Loc.Text("Pause");
    public string SelectedModeToggleLabel => SelectedFolder?.ModeToggleButtonLabel ?? Loc.Text("SwitchToBackup");

    public ObservableCollection<SyncFolderItemViewModel> Folders { get; } = [];

    public RemoteBrowserViewModel RemoteBrowser { get; }

    public Func<Task<string?>>? PickLocalFolderCallback { get; set; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }

    public SyncFoldersViewModel(
        IDriveSyncService syncService,
        IGoogleDriveClient driveClient,
        RemoteBrowserViewModel remoteBrowser,
        ILocalizationService localization)
    {
        _syncService = syncService;
        _driveClient = driveClient;
        RemoteBrowser = remoteBrowser;
        Loc = localization;
        RemoteBrowser.RemoteFolderSelectedForSync += OnRemoteFolderSelectedAsync;

        Loc.LanguageChanged += (_, _) =>
        {
            ApplyAccount(_lastAccount);
            foreach (var folder in Folders)
            {
                folder.RefreshLocalizedProperties();
            }
            OnPropertyChanged(nameof(SelectedPauseLabel));
            OnPropertyChanged(nameof(SelectedModeToggleLabel));
        };

        ApplyAccount(_syncService.CurrentAccount);
    }

    public async Task RefreshFoldersAsync()
    {
        var list = await _syncService.GetSyncFoldersAsync();
        var incomingById = list.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);

        for (var i = Folders.Count - 1; i >= 0; i--)
        {
            if (!incomingById.ContainsKey(Folders[i].Id))
            {
                Folders.RemoveAt(i);
            }
        }

        foreach (var model in list)
        {
            var existing = Folders.FirstOrDefault(f => f.Id == model.Id);
            if (existing is not null)
            {
                existing.UpdateFrom(model);
            }
            else
            {
                Folders.Add(new SyncFolderItemViewModel(model, this, Loc));
            }
        }

        HasNoFolders = Folders.Count == 0;
        OnPropertyChanged(nameof(SelectedPauseLabel));
        OnPropertyChanged(nameof(SelectedModeToggleLabel));
    }

    public void ApplyAccount(DriveAccount account)
    {
        _lastAccount = account;
        IsAccountConnected = account.IsConnected;
        QuotaPercentage = account.Quota.UsagePercentage;
        var limit = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : Loc.Text("Unlimited");
        QuotaSummaryText = account.IsConnected
            ? Loc.Format("QuotaSummaryFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limit)
            : Loc.Text("AccountDisconnected");

        var isFull = account.IsConnected && account.Quota.LimitBytes > 0 &&
            (account.Quota.UsageBytes >= account.Quota.LimitBytes || account.Quota.UsagePercentage >= 99.5);
        HasStorageAlert = account.IsConnected && account.Quota.LimitBytes > 0 && account.Quota.UsagePercentage >= 80.0;
        StorageAlertMessage = isFull
            ? Loc.Format("StorageFullFmt", account.Quota.UsagePercentage)
            : HasStorageAlert
                ? Loc.Format("StorageWarningFmt", account.Quota.UsagePercentage)
                : string.Empty;
        StorageProgressBrush = isFull
            ? SolidColorBrush.Parse("#EF4444")
            : HasStorageAlert
                ? SolidColorBrush.Parse("#D29922")
                : SolidColorBrush.Parse("#3B82F6");
        StorageAlertBorderBrush = isFull
            ? SolidColorBrush.Parse("#EF4444")
            : HasStorageAlert
                ? SolidColorBrush.Parse("#D29922")
                : SolidColorBrush.Parse("#2A303B");
        StorageAlertTextBrush = isFull
            ? SolidColorBrush.Parse("#F87171")
            : SolidColorBrush.Parse("#D29922");

        UpdateSegmentedBarWidths(account);
    }

    public void SetStorageTrackWidth(double width)
    {
        if (width <= 20 || Math.Abs(width - _storageTrackWidth) < 1.0)
        {
            return;
        }

        _storageTrackWidth = width;
        if (_lastAccount.IsConnected)
        {
            UpdateSegmentedBarWidths(_lastAccount);
        }
    }

    private void UpdateSegmentedBarWidths(DriveAccount account)
    {
        var trackWidth = Math.Max(120.0, _storageTrackWidth);
        if (account.IsConnected && account.Quota.LimitBytes > 0)
        {
            var otherBytes = Math.Max(0, account.Quota.UsageBytes - account.Quota.UsageInDriveBytes - account.Quota.UsageInTrashBytes);
            DriveLegendText = Loc.Format("StorageDriveValueFmt", UiConverters.FormatBytes(account.Quota.UsageInDriveBytes));
            OtherLegendText = Loc.Format("StorageOtherValueFmt", UiConverters.FormatBytes(otherBytes));
            TrashLegendText = Loc.Format("StorageTrashValueFmt", UiConverters.FormatBytes(account.Quota.UsageInTrashBytes));

            (DriveBarWidth, OtherBarWidth, TrashBarWidth) = ComputeStorageBarWidths(account.Quota, trackWidth);
        }
        else
        {
            DriveLegendText = Loc.Format("StorageDriveValueFmt", "0 B");
            OtherLegendText = Loc.Format("StorageOtherValueFmt", "0 B");
            TrashLegendText = Loc.Format("StorageTrashValueFmt", "0 B");
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
        }
    }

    [RelayCommand]
    private Task SyncSelectedFolderAsync() => SelectedFolder is not null
        ? SyncSingleFolderAsync(SelectedFolder.Id)
        : Task.CompletedTask;

    [RelayCommand]
    private Task TogglePauseSelectedFolderAsync() => SelectedFolder is not null
        ? TogglePauseFolderAsync(SelectedFolder.Id, !SelectedFolder.IsPaused)
        : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleModeSelectedFolderAsync() => SelectedFolder is not null
        ? ChangeFolderModeAsync(SelectedFolder.Id, SelectedFolder.Mode == SyncMode.TwoWay ? SyncMode.OneWayBackup : SyncMode.TwoWay)
        : Task.CompletedTask;

    [RelayCommand]
    private Task OpenSelectedFolderAsync() => SelectedFolder is not null
        ? OpenLocalFolderAsync(SelectedFolder.LocalPath)
        : Task.CompletedTask;

    [RelayCommand]
    private Task RemoveSelectedFolderAsync() => SelectedFolder is not null
        ? RemoveFolderAsync(SelectedFolder.Id)
        : Task.CompletedTask;

    public Task OpenLocalFolderAsync(string localPath)
    {
        if (OpenLocalFolderCallback is null || string.IsNullOrWhiteSpace(localPath))
        {
            return Task.CompletedTask;
        }

        return OpenLocalFolderCallback.Invoke(localPath);
    }

    [RelayCommand]
    private async Task BuyMoreStorageAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(GoogleOneStorageUrl));
        }
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusMessage = Loc.Text("ConnectBeforeSync");
            return;
        }

        await RefreshFoldersAsync();
        if (Folders.Count == 0)
        {
            StatusMessage = Loc.Text("EmptyFoldersHint");
            return;
        }

        if (Folders.All(f => f.IsPaused))
        {
            StatusMessage = Loc.Text("AllFoldersPausedHint");
            return;
        }

        StatusMessage = Loc.Text("SyncingAllFolders");
        try
        {
            await _syncService.TriggerSyncAllAsync();
            await RefreshFoldersAsync();
            StatusMessage = Loc.Text("SyncFinished");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddLocalFolderToRootAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusMessage = Loc.Text("ConnectBeforeSync");
            return;
        }

        if (PickLocalFolderCallback is null)
        {
            return;
        }

        var localPath = await PickLocalFolderCallback.Invoke();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        try
        {
            var folderName = new DirectoryInfo(localPath).Name;
            var remoteId = "root";
            var remoteDisplay = Loc.Text("MyDriveLabel");

            if (_driveClient.IsConnected && !string.IsNullOrWhiteSpace(folderName))
            {
                var remoteFolder = await _driveClient.FindOrCreateFolderAsync(folderName, "root");
                remoteId = remoteFolder.Id;
                remoteDisplay = $"{Loc.Text("MyDriveLabel")}/{remoteFolder.Name}";
            }

            await _syncService.AddSyncFolderAsync(localPath, remoteId, remoteDisplay, SyncMode.TwoWay);
            IsRemoteBrowserActive = false;
            await RefreshFoldersAsync();
            StatusMessage = Loc.Format("FolderMappedFmt", localPath, remoteDisplay);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenRemoteBrowserAsync()
    {
        IsRemoteBrowserActive = true;
        await RemoteBrowser.LoadCurrentFolderAsync();
    }

    [RelayCommand]
    private void ShowMappedFolders()
    {
        IsRemoteBrowserActive = false;
    }

    public async Task SyncSingleFolderAsync(string folderId)
    {
        await _syncService.TriggerFolderSyncAsync(folderId);
        await RefreshFoldersAsync();
    }

    public async Task TogglePauseFolderAsync(string folderId, bool pause)
    {
        await _syncService.SetFolderPausedAsync(folderId, pause);
        await RefreshFoldersAsync();
    }

    public async Task ChangeFolderModeAsync(string folderId, SyncMode mode)
    {
        await _syncService.UpdateSyncModeAsync(folderId, mode);
        await RefreshFoldersAsync();
    }

    public async Task RemoveFolderAsync(string folderId)
    {
        await _syncService.RemoveSyncFolderAsync(folderId);
        await RefreshFoldersAsync();
    }

    private async Task OnRemoteFolderSelectedAsync(string remoteFolderId, string remoteFolderName)
    {
        await PromptAndMapFolderAsync(remoteFolderId, remoteFolderName);
        IsRemoteBrowserActive = false;
    }

    private async Task PromptAndMapFolderAsync(string remoteFolderId, string remoteFolderName)
    {
        if (PickLocalFolderCallback is null)
        {
            return;
        }

        var localPath = await PickLocalFolderCallback.Invoke();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        try
        {
            await _syncService.AddSyncFolderAsync(localPath, remoteFolderId, remoteFolderName, SyncMode.TwoWay);
            await RefreshFoldersAsync();
            StatusMessage = Loc.Format("FolderMappedFmt", localPath, remoteFolderName);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }
}
