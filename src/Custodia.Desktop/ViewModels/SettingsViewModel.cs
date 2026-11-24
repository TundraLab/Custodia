using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class SettingsAccountItemViewModel : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }
    public string EmailAddress { get; }
    public string QuotaSummary { get; }
    public bool IsActive { get; }
    public bool CanSwitch => !IsActive;
    public string ActiveBadgeText { get; }
    public string JsonBadgeText { get; }
    public IBrush JsonBadgeBgBrush { get; }
    public IBrush JsonBadgeFgBrush { get; }
    public string JsonDetailText { get; }
    public string AccountFolderDisplay { get; }
    public string SwitchButtonText { get; }
    public string ReconnectButtonText { get; }
    public string ReplaceJsonButtonText { get; }
    public string OpenFolderButtonText { get; }
    public string RevokeButtonText { get; }
    public IAsyncRelayCommand SwitchCommand { get; }
    public IAsyncRelayCommand ReconnectCommand { get; }
    public IAsyncRelayCommand ReplaceJsonCommand { get; }
    public IAsyncRelayCommand OpenFolderCommand { get; }
    public IAsyncRelayCommand RevokeCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatarBitmap;

    public bool HasAvatar => AvatarBitmap is not null;

    public SettingsAccountItemViewModel(
        DriveAccount account,
        bool isActive,
        CredentialsFileMetadata? jsonMeta,
        string accountFolderPath,
        ILocalizationService loc,
        Func<string, Task> onSwitch,
        Func<string, Task> onReconnect,
        Func<string, Task> onReplaceJson,
        Func<string, string, Task> onOpenFolder,
        Func<string, Task> onRevoke)
    {
        Id = account.Id;
        DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.EmailAddress : account.DisplayName;
        EmailAddress = account.EmailAddress;
        var limitText = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : loc.Text("Unlimited");
        QuotaSummary = loc.Format("QuotaUsageFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limitText);
        IsActive = isActive;

        if (jsonMeta is null)
        {
            JsonBadgeText = loc.Text("CredentialsMissingBadge");
            JsonBadgeBgBrush = SolidColorBrush.Parse("#4A1C20");
            JsonBadgeFgBrush = SolidColorBrush.Parse("#FFDAD6");
            JsonDetailText = loc.Text("AccountJsonMissingDetail");
        }
        else if (jsonMeta.IsPerAccount)
        {
            JsonBadgeText = loc.Text("AccountJsonCustomBadge");
            JsonBadgeBgBrush = SolidColorBrush.Parse("#374955");
            JsonBadgeFgBrush = SolidColorBrush.Parse("#D3E4F3");
            JsonDetailText = string.IsNullOrWhiteSpace(jsonMeta.ProjectId)
                ? jsonMeta.MaskedClientId
                : $"{jsonMeta.ProjectId} • {jsonMeta.MaskedClientId}";
        }
        else
        {
            JsonBadgeText = loc.Text("AccountJsonGlobalBadge");
            JsonBadgeBgBrush = SolidColorBrush.Parse("#282A2F");
            JsonBadgeFgBrush = SolidColorBrush.Parse("#C4C6D0");
            JsonDetailText = string.IsNullOrWhiteSpace(jsonMeta.ProjectId)
                ? jsonMeta.MaskedClientId
                : $"{jsonMeta.ProjectId} • {jsonMeta.MaskedClientId}";
        }

        AccountFolderDisplay = CompactHomePath(accountFolderPath);
        ActiveBadgeText = loc.Text("ActiveAccountBadge");
        SwitchButtonText = loc.Text("SwitchToAccountButton");
        ReconnectButtonText = loc.Text("ReconnectAccountButton");
        ReplaceJsonButtonText = loc.Text("ReplaceAccountJsonButton");
        OpenFolderButtonText = loc.Text("OpenAccountFolderButton");
        RevokeButtonText = loc.Text("RevokeAccountButton");

        SwitchCommand = new AsyncRelayCommand(() => onSwitch(account.Id));
        ReconnectCommand = new AsyncRelayCommand(() => onReconnect(account.Id));
        ReplaceJsonCommand = new AsyncRelayCommand(() => onReplaceJson(account.Id));
        OpenFolderCommand = new AsyncRelayCommand(() => onOpenFolder(account.Id, account.EmailAddress));
        RevokeCommand = new AsyncRelayCommand(() => onRevoke(account.Id));

        if (!string.IsNullOrWhiteSpace(account.PhotoUrl))
        {
            _ = LoadAvatarAsync(account.PhotoUrl);
        }
    }

    private static string CompactHomePath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..];
        }
        return path;
    }

    private async Task LoadAvatarAsync(string photoUrl)
    {
        var bmp = await AvatarLoader.LoadAsync(photoUrl);
        if (bmp is not null)
        {
            Dispatcher.UIThread.Post(() => AvatarBitmap = bmp);
        }
    }
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IEmbeddedApiServer _apiServer;
    private readonly IAutostartManager _autostartManager;
    private readonly IDesktopNotificationService _notificationService;
    private readonly IAuthCredentialManager _authManager;
    private readonly IDriveSyncService _syncService;
    private readonly MemoryCleanerService _memoryCleaner;
    private readonly NotificationSoundService? _soundService;
    private CancellationTokenSource? _saveFeedbackCts;
    private CancellationTokenSource? _reloadFeedbackCts;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab))]
    [NotifyPropertyChangedFor(nameof(IsAccountsTab))]
    [NotifyPropertyChangedFor(nameof(IsSyncTab))]
    [NotifyPropertyChangedFor(nameof(IsApiTab))]
    private int _selectedSettingsTabIndex;

    public bool IsGeneralTab => SelectedSettingsTabIndex == 0;
    public bool IsAccountsTab => SelectedSettingsTabIndex == 1;
    public bool IsSyncTab => SelectedSettingsTabIndex == 2;
    public bool IsApiTab => SelectedSettingsTabIndex == 3;

    [RelayCommand]
    private void SelectGeneralTab() => SelectedSettingsTabIndex = 0;

    [RelayCommand]
    private void SelectAccountsTab() => SelectedSettingsTabIndex = 1;

    [RelayCommand]
    private void SelectSyncTab() => SelectedSettingsTabIndex = 2;

    [RelayCommand]
    private void SelectApiTab() => SelectedSettingsTabIndex = 3;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedAccentIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScaleDisplay))]
    private int _uiScalePercent = 100;

    public string UiScaleDisplay => $"{Math.Clamp(UiScalePercent, 100, 300)}%";

    public event EventHandler<int>? UiScaleChanged;

    [ObservableProperty]
    private bool _startOnBoot;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _notificationSoundEnabled = true;

    [ObservableProperty]
    private int _selectedNotificationSoundIndex;

    [ObservableProperty]
    private bool _gmailAccountAlertsEnabled = true;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private int _selectedUpdateFrequencyIndex;

    [ObservableProperty]
    private string _downloadDirectory = AppPaths.DefaultDownloadDirectory;

    [ObservableProperty]
    private bool _isAutostartSupported;

    [ObservableProperty]
    private decimal? _syncIntervalSeconds = 120;

    [ObservableProperty]
    private bool _watchLocalChanges = true;

    [ObservableProperty]
    private bool _embeddedApiEnabled = true;

    [ObservableProperty]
    private decimal? _embeddedApiPort = 5080;

    [ObservableProperty]
    private bool _autoMemoryCleanupEnabled = true;

    [ObservableProperty]
    private decimal? _memoryCleanupIntervalMinutes = 5;

    [ObservableProperty]
    private string _memoryCleanerStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCleaningMemory;

    [ObservableProperty]
    private int _selectedConflictPolicyIndex;

    [ObservableProperty]
    private string _ignorePatternsText = string.Empty;

    [ObservableProperty]
    private string _apiEndpointStatus = string.Empty;

    [ObservableProperty]
    private string _credentialsPathDisplay = string.Empty;

    [ObservableProperty]
    private string _tokensPathDisplay = string.Empty;

    [ObservableProperty]
    private string _databasePathDisplay = string.Empty;

    [ObservableProperty]
    private string _autostartPathDisplay = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSaveIcon))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSaveIcon))]
    private bool _isSavedFeedback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReloadButtonText))]
    private bool _isReloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReloadButtonText))]
    private bool _isReloadedFeedback;

    public bool ShowDefaultSaveIcon => !IsSaving && !IsSavedFeedback;

    public string SaveButtonText =>
        IsSaving ? Loc.SavingButton :
        IsSavedFeedback ? Loc.SavedButton :
        Loc.SaveChangesButton;

    public string ReloadButtonText =>
        IsReloading ? Loc.ReloadingButton :
        IsReloadedFeedback ? Loc.ReloadedButton :
        Loc.ReloadButton;

    public string RepositoryUrl => MainWindowViewModel.RepositoryUrl;
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }
    public Func<Task<string?>>? PickDownloadDirectoryCallback { get; set; }
    public Func<Task<string?>>? PickCredentialsFileCallback { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialsStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(CanRevokeCredentialsJson))]
    private bool _hasCredentialsJson;

    [ObservableProperty]
    private string _credentialsJsonStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentialsMetadata))]
    private string _credentialsMetadataText = string.Empty;

    public bool HasCredentialsMetadata => !string.IsNullOrWhiteSpace(CredentialsMetadataText);

    [ObservableProperty]
    private string _credentialsFilePathDisplay = AppPaths.CredentialsFilePath;

    [ObservableProperty]
    private string _accountsRootDirectoryDisplay = AppPaths.AccountsDirectory;

    [ObservableProperty]
    private bool _hasDetectedDownloadsJson;

    [ObservableProperty]
    private string _detectedDownloadsJsonPath = string.Empty;

    [ObservableProperty]
    private string _detectedDownloadsSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentialsFeedback))]
    private string _credentialsFeedbackMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialsFeedbackBgBrush))]
    [NotifyPropertyChangedFor(nameof(CredentialsFeedbackBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CredentialsFeedbackTextBrush))]
    private bool _isCredentialsFeedbackError;

    public bool HasCredentialsFeedback => !string.IsNullOrWhiteSpace(CredentialsFeedbackMessage);

    public IBrush CredentialsFeedbackBgBrush => IsCredentialsFeedbackError
        ? SolidColorBrush.Parse("#4A1C20")
        : SolidColorBrush.Parse("#1B3927");

    public IBrush CredentialsFeedbackBorderBrush => IsCredentialsFeedbackError
        ? SolidColorBrush.Parse("#F28B82")
        : SolidColorBrush.Parse("#6DD58C");

    public IBrush CredentialsFeedbackTextBrush => IsCredentialsFeedbackError
        ? SolidColorBrush.Parse("#FFDAD6")
        : SolidColorBrush.Parse("#B8F3C8");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevokeCredentialsJson))]
    private bool _isCredentialsBusy;

    [ObservableProperty]
    private bool _isAddAccountPromptVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAccountRemoval))]
    [NotifyPropertyChangedFor(nameof(PendingRemoveAccountPromptText))]
    private string? _pendingRemoveAccountId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingRemoveAccountPromptText))]
    private string _pendingRemoveAccountEmail = string.Empty;

    [ObservableProperty]
    private bool _pendingRemoveDeleteFolders;

    public bool HasPendingAccountRemoval => !string.IsNullOrWhiteSpace(PendingRemoveAccountId);

    public string PendingRemoveAccountPromptText => string.IsNullOrWhiteSpace(PendingRemoveAccountEmail)
        ? Loc.Text("RemoveAccountPromptDefault")
        : Loc.Format("RemoveAccountPromptFmt", PendingRemoveAccountEmail);

    public string CredentialsStatusBadgeText => HasCredentialsJson
        ? Loc.Text("CredentialsActiveBadge")
        : Loc.Text("CredentialsMissingBadge");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectedAccounts))]
    private int _connectedAccountsCount;

    public bool HasConnectedAccounts => ConnectedAccountsCount > 0;
    public bool CanRevokeCredentialsJson => HasCredentialsJson && !IsCredentialsBusy;

    public ObservableCollection<SettingsAccountItemViewModel> ConnectedAccounts { get; } = [];

    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        "Português (Brasil)",
        "English (US)"
    ];

    public ObservableCollection<string> AccentOptions { get; } = [];
    public ObservableCollection<string> ConflictPolicies { get; } = [];
    public ObservableCollection<string> UpdateFrequencies { get; } = [];
    public ObservableCollection<string> NotificationSoundOptions { get; } = [];

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IEmbeddedApiServer apiServer,
        IAutostartManager autostartManager,
        ILocalizationService localization,
        IDesktopNotificationService notificationService,
        IAuthCredentialManager authManager,
        IDriveSyncService syncService,
        MemoryCleanerService memoryCleaner,
        NotificationSoundService? soundService = null)
    {
        _settingsService = settingsService;
        _apiServer = apiServer;
        _autostartManager = autostartManager;
        _notificationService = notificationService;
        _authManager = authManager;
        _syncService = syncService;
        _memoryCleaner = memoryCleaner;
        _soundService = soundService;
        Loc = localization;

        IsAutostartSupported = _autostartManager.IsSupported;
        RebuildLocalizedLists();

        Loc.LanguageChanged += (_, _) =>
        {
            RebuildLocalizedLists();
            RefreshPathAndApiTexts();
            RefreshCredentialsAndAccounts();
            RefreshMemoryCleanerStatus();
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(ReloadButtonText));
        };

        _syncService.AccountsListChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCredentialsAndAccounts);
        _syncService.AccountChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCredentialsAndAccounts);
        _memoryCleaner.CleanupCompleted += (_, _) => Dispatcher.UIThread.Post(RefreshMemoryCleanerStatus);

        LoadFromCurrent();
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var code = value == 1 ? "en-US" : "pt-BR";
        Loc.SetLanguage(code);
    }

    partial void OnSelectedAccentIndexChanged(int value)
    {
        var accent = value == 1 ? ThemeAccentService.EmeraldAccentId : ThemeAccentService.BlueAccentId;
        ThemeAccentService.ApplyAccent(accent);
    }

    partial void OnUiScalePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 100, 300);
        if (clamped != value)
        {
            UiScalePercent = clamped;
            return;
        }

        UiScaleChanged?.Invoke(this, clamped);
    }

    private async Task PersistUiScaleAsync(int scalePercent)
    {
        try
        {
            var current = _settingsService.Current;
            current.UiScalePercent = scalePercent;
            await _settingsService.SaveAsync(current);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void ResetUiScale()
    {
        UiScalePercent = 100;
        _ = PersistUiScaleAsync(100);
    }

    public void AdjustUiScale(int delta, bool persistImmediately = true)
    {
        var clamped = Math.Clamp(UiScalePercent + delta, 100, 300);
        UiScalePercent = clamped;
        if (persistImmediately)
        {
            _ = PersistUiScaleAsync(clamped);
        }
    }

    public void LoadFromCurrent()
    {
        var current = _settingsService.Current;
        SelectedLanguageIndex = string.Equals(current.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        SelectedAccentIndex = ThemeAccentService.NormalizeAccent(current.UiAccent) == ThemeAccentService.EmeraldAccentId ? 1 : 0;
        ThemeAccentService.ApplyAccent(current.UiAccent);
        UiScalePercent = Math.Clamp(current.UiScalePercent, 100, 300);
        StartOnBoot = current.StartOnBoot || _autostartManager.IsEnabled();
        NotificationsEnabled = current.NotificationsEnabled;
        NotificationSoundEnabled = current.NotificationSoundEnabled;
        SelectedNotificationSoundIndex = NotificationSoundService.GetPresetIndex(current.NotificationSoundTone);
        GmailAccountAlertsEnabled = current.GmailAccountAlertsEnabled;
        MinimizeToTrayOnClose = current.MinimizeToTrayOnClose;
        AutoCheckUpdates = current.AutoCheckUpdates;
        SelectedUpdateFrequencyIndex = current.UpdateCheckIntervalDays switch
        {
            0 => 3,
            7 => 2,
            15 => 1,
            _ => 0
        };
        DownloadDirectory = string.IsNullOrWhiteSpace(current.DownloadDirectory)
            ? AppPaths.DefaultDownloadDirectory
            : current.DownloadDirectory;
        SyncIntervalSeconds = current.SyncIntervalSeconds;
        WatchLocalChanges = current.WatchLocalChanges;
        EmbeddedApiEnabled = current.EmbeddedApiEnabled;
        EmbeddedApiPort = current.EmbeddedApiPort;
        AutoMemoryCleanupEnabled = current.AutoMemoryCleanupEnabled;
        MemoryCleanupIntervalMinutes = Math.Clamp(current.MemoryCleanupIntervalMinutes, 1, 60);
        SelectedConflictPolicyIndex = (int)current.ConflictPolicy;
        IgnorePatternsText = string.Join(Environment.NewLine, current.IgnorePatterns);

        RefreshPathAndApiTexts();
        RefreshCredentialsAndAccounts();
        RefreshMemoryCleanerStatus();
    }

    private void RefreshMemoryCleanerStatus()
    {
        var currentMb = MemoryCleanerService.GetCurrentProcessPrivateMemoryBytes() / 1_000_000.0;
        if (_memoryCleaner.LastCleanedAtUtc.HasValue)
        {
            var freedMb = _memoryCleaner.LastFreedBytes / 1_000_000.0;
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCleanedFmt", currentMb, freedMb);
        }
        else
        {
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCurrentFmt", currentMb);
        }
    }

    [RelayCommand]
    private async Task CleanMemoryNowAsync()
    {
        if (IsCleaningMemory)
        {
            return;
        }

        IsCleaningMemory = true;
        try
        {
            var result = await _memoryCleaner.CleanNowAsync(forceReleaseMedia: true);
            var afterMb = result.AfterBytes / 1_000_000.0;
            var freedMb = result.FreedBytes / 1_000_000.0;
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCleanedFmt", afterMb, freedMb);
        }
        finally
        {
            IsCleaningMemory = false;
        }
    }

    public void RefreshCredentialsAndAccounts()
    {
        HasCredentialsJson = File.Exists(_authManager.CredentialsFilePath);
        CredentialsFilePathDisplay = CompactPath(_authManager.CredentialsFilePath);
        AccountsRootDirectoryDisplay = CompactPath(_authManager.AccountsDirectoryPath);
        var globalMeta = _authManager.InspectCredentialsFile();
        CredentialsJsonStatusText = HasCredentialsJson
            ? Loc.Format("CredentialsJsonStatusReadyFmt", CredentialsFilePathDisplay)
            : Loc.Text("CredentialsJsonStatusMissing");
        CredentialsMetadataText = globalMeta is not null
            ? (string.IsNullOrWhiteSpace(globalMeta.ProjectId)
                ? Loc.Format("CredentialsClientOnlyFmt", globalMeta.MaskedClientId)
                : Loc.Format("CredentialsProjectClientFmt", globalMeta.ProjectId, globalMeta.MaskedClientId))
            : string.Empty;
        OnPropertyChanged(nameof(CredentialsStatusBadgeText));
        OnPropertyChanged(nameof(CanRevokeCredentialsJson));

        var candidates = _authManager.FindDownloadsCredentialsCandidates();
        var topCandidate = candidates.FirstOrDefault(c =>
            globalMeta is null || !c.MaskedClientId.Equals(globalMeta.MaskedClientId, StringComparison.Ordinal));
        if (topCandidate is not null)
        {
            DetectedDownloadsJsonPath = topCandidate.FilePath;
            DetectedDownloadsSummary = string.IsNullOrWhiteSpace(topCandidate.ProjectId)
                ? $"{topCandidate.FileName} ({topCandidate.MaskedClientId})"
                : $"{topCandidate.FileName} ({topCandidate.ProjectId})";
            HasDetectedDownloadsJson = true;
        }
        else
        {
            DetectedDownloadsJsonPath = string.Empty;
            DetectedDownloadsSummary = string.Empty;
            HasDetectedDownloadsJson = false;
        }

        ConnectedAccounts.Clear();
        var activeId = _syncService.CurrentAccount.IsConnected ? _syncService.CurrentAccount.Id : null;
        foreach (var account in _syncService.SavedAccounts.Where(a => a.IsConnected))
        {
            var accountMeta = _authManager.InspectCredentialsFile(account.Id, account.EmailAddress);
            var accountDir = _authManager.GetAccountFolderPath(account.Id, account.EmailAddress);
            ConnectedAccounts.Add(new SettingsAccountItemViewModel(
                account,
                string.Equals(account.Id, activeId, StringComparison.Ordinal),
                accountMeta,
                accountDir,
                Loc,
                SwitchAccountFromSettingsAsync,
                ReconnectAccountFromSettingsAsync,
                ReplaceAccountJsonFromSettingsAsync,
                OpenAccountFolderFromSettingsAsync,
                RevokeSingleAccountAsync));
        }

        ConnectedAccountsCount = ConnectedAccounts.Count;
    }

    private void SetCredentialsFeedback(string message, bool isError)
    {
        CredentialsFeedbackMessage = message;
        IsCredentialsFeedbackError = isError;
    }

    private static string CompactPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..];
        }
        return path;
    }

    private async Task SwitchAccountFromSettingsAsync(string accountId)
    {
        IsCredentialsBusy = true;
        try
        {
            var switched = await _syncService.SwitchAccountAsync(accountId);
            RefreshCredentialsAndAccounts();
            if (switched is not null)
            {
                SetCredentialsFeedback(Loc.Format("SwitchedToFmt", switched.EmailAddress), isError: false);
            }
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("ConnectErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private async Task ReconnectAccountFromSettingsAsync(string accountId)
    {
        IsCredentialsBusy = true;
        SetCredentialsFeedback(Loc.Text("OpeningBrowserAuth"), isError: false);
        try
        {
            var reconnected = await _syncService.ReconnectAccountAsync(accountId);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Format("ConnectedAsFmt", reconnected.EmailAddress), isError: false);
        }
        catch (OperationCanceledException)
        {
            SetCredentialsFeedback(Loc.Text("AuthCancelledMessage"), isError: true);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("ConnectErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private async Task ReplaceAccountJsonFromSettingsAsync(string accountId)
    {
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var selected = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        IsCredentialsBusy = true;
        SetCredentialsFeedback(Loc.Text("OpeningBrowserAuth"), isError: false);
        try
        {
            var updated = await _syncService.ReconnectAccountAsync(accountId, selected);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Format("AccountJsonReplacedFmt", updated.EmailAddress), isError: false);
        }
        catch (OperationCanceledException)
        {
            SetCredentialsFeedback(Loc.Text("AuthCancelledMessage"), isError: true);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("CredentialsImportErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private async Task OpenAccountFolderFromSettingsAsync(string accountId, string email)
    {
        var dir = _authManager.GetAccountFolderPath(accountId, email);
        Directory.CreateDirectory(dir);
        if (OpenLocalFolderCallback is not null)
        {
            await OpenLocalFolderCallback.Invoke(dir);
        }
    }

    private Task RevokeSingleAccountAsync(string accountId)
    {
        var target = ConnectedAccounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));
        PendingRemoveAccountId = accountId;
        PendingRemoveAccountEmail = target?.EmailAddress ?? string.Empty;
        PendingRemoveDeleteFolders = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ConfirmRemoveAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(PendingRemoveAccountId))
        {
            return;
        }

        var targetId = PendingRemoveAccountId;
        var deleteFolders = PendingRemoveDeleteFolders;
        PendingRemoveAccountId = null;
        PendingRemoveAccountEmail = string.Empty;
        PendingRemoveDeleteFolders = false;

        IsCredentialsBusy = true;
        try
        {
            await _syncService.DisconnectAccountAsync(targetId, deleteFolderConfiguration: deleteFolders);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(
                deleteFolders
                    ? Loc.Text("AccountAndFoldersRemovedFeedback")
                    : Loc.Text("AccountRemovedFoldersKeptFeedback"),
                isError: false);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("DisconnectErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelRemoveAccount()
    {
        PendingRemoveAccountId = null;
        PendingRemoveAccountEmail = string.Empty;
        PendingRemoveDeleteFolders = false;
    }

    [RelayCommand]
    private async Task AddAccountFromSettingsAsync()
    {
        if (!_authManager.CredentialsFileExists())
        {
            await ImportJsonAndAddAccountFromSettingsAsync();
            return;
        }

        IsAddAccountPromptVisible = !IsAddAccountPromptVisible;
    }

    [RelayCommand]
    private async Task ImportJsonAndAddAccountFromSettingsAsync()
    {
        IsAddAccountPromptVisible = false;
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var path = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsCredentialsBusy = true;
        SetCredentialsFeedback(Loc.Text("OpeningBrowserAuth"), isError: false);
        try
        {
            var account = await _syncService.ConnectAccountAsync(addNewAccount: true, customCredentialsJsonPath: path);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Format("ConnectedAsFmt", account.EmailAddress), isError: false);
        }
        catch (OperationCanceledException)
        {
            SetCredentialsFeedback(Loc.Text("AuthCancelledMessage"), isError: true);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("ConnectErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueWithCurrentJsonFromSettingsAsync()
    {
        IsAddAccountPromptVisible = false;
        IsCredentialsBusy = true;
        SetCredentialsFeedback(Loc.Text("OpeningBrowserAuth"), isError: false);
        try
        {
            var account = await _syncService.ConnectAccountAsync(addNewAccount: true);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Format("ConnectedAsFmt", account.EmailAddress), isError: false);
        }
        catch (OperationCanceledException)
        {
            SetCredentialsFeedback(Loc.Text("AuthCancelledMessage"), isError: true);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("ConnectErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAddAccountPromptFromSettings() => IsAddAccountPromptVisible = false;

    [RelayCommand]
    private async Task ImportCredentialsJsonAsync()
    {
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var selected = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        await ImportCredentialsFromPathAsync(selected);
    }

    [RelayCommand]
    private async Task ImportDetectedDownloadsJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(DetectedDownloadsJsonPath))
        {
            return;
        }

        await ImportCredentialsFromPathAsync(DetectedDownloadsJsonPath);
    }

    public async Task ImportCredentialsFromPathAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        IsCredentialsBusy = true;
        try
        {
            await _authManager.ImportCredentialsFileAsync(sourcePath);
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Text("CredentialsImportedSuccess"), isError: false);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("CredentialsImportErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenCredentialsFolderAsync()
    {
        if (OpenLocalFolderCallback is null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(_authManager.CredentialsFilePath);
        await OpenLocalFolderCallback.Invoke(string.IsNullOrWhiteSpace(dir) ? AppPaths.ConfigDirectory : dir);
    }

    [RelayCommand]
    private async Task OpenAccountsRootFolderAsync()
    {
        Directory.CreateDirectory(_authManager.AccountsDirectoryPath);
        if (OpenLocalFolderCallback is not null)
        {
            await OpenLocalFolderCallback.Invoke(_authManager.AccountsDirectoryPath);
        }
    }

    [RelayCommand]
    private async Task RevokeCredentialsJsonAsync()
    {
        IsCredentialsBusy = true;
        try
        {
            await _syncService.RevokeCredentialsFileAsync();
            RefreshCredentialsAndAccounts();
            SetCredentialsFeedback(Loc.Text("CredentialsJsonRevokedFeedback"), isError: false);
        }
        catch (Exception ex)
        {
            SetCredentialsFeedback(Loc.Format("CredentialsImportErrorFmt", ex.Message), isError: true);
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private void RebuildLocalizedLists()
    {
        var savedAccentIndex = Math.Max(0, SelectedAccentIndex);
        AccentOptions.Clear();
        AccentOptions.Add(Loc.Text("UiAccentBlueOption"));
        AccentOptions.Add(Loc.Text("UiAccentEmeraldOption"));
        SelectedAccentIndex = Math.Clamp(savedAccentIndex, 0, AccentOptions.Count - 1);

        var savedIndex = Math.Max(0, SelectedConflictPolicyIndex);
        ConflictPolicies.Clear();
        ConflictPolicies.Add(Loc.Text("ConflictKeepNewest"));
        ConflictPolicies.Add(Loc.Text("ConflictPreferLocal"));
        ConflictPolicies.Add(Loc.Text("ConflictPreferRemote"));
        ConflictPolicies.Add(Loc.Text("ConflictRenameCopy"));
        SelectedConflictPolicyIndex = Math.Clamp(savedIndex, 0, ConflictPolicies.Count - 1);

        var savedFreqIndex = Math.Max(0, SelectedUpdateFrequencyIndex);
        UpdateFrequencies.Clear();
        UpdateFrequencies.Add(Loc.Text("UpdateFrequencyMonthly"));
        UpdateFrequencies.Add(Loc.Text("UpdateFrequencyBiweekly"));
        UpdateFrequencies.Add(Loc.Text("UpdateFrequencyWeekly"));
        UpdateFrequencies.Add(Loc.Text("UpdateFrequencyOnStartup"));
        SelectedUpdateFrequencyIndex = Math.Clamp(savedFreqIndex, 0, UpdateFrequencies.Count - 1);

        var savedToneIndex = Math.Max(0, SelectedNotificationSoundIndex);
        NotificationSoundOptions.Clear();
        foreach (var preset in NotificationSoundService.Presets)
        {
            NotificationSoundOptions.Add(Loc.Text(preset.LabelKey));
        }
        SelectedNotificationSoundIndex = Math.Clamp(savedToneIndex, 0, Math.Max(0, NotificationSoundOptions.Count - 1));
    }

    private void RefreshPathAndApiTexts()
    {
        ApiEndpointStatus = _apiServer.IsRunning
            ? Loc.Format("ApiStatusActiveFmt", _apiServer.BoundAddress)
            : string.IsNullOrWhiteSpace(_apiServer.LastError)
                ? Loc.Text("ApiStatusInactive")
                : Loc.Format("ApiStatusErrorFmt", _apiServer.LastError);

        CredentialsPathDisplay = Loc.Format("PathCredsFmt", AppPaths.CredentialsFilePath);
        TokensPathDisplay = Loc.Format("PathTokensFmt", AppPaths.TokensDirectory);
        DatabasePathDisplay = Loc.Format("PathDbFmt", AppPaths.DatabaseFilePath);
        AutostartPathDisplay = Loc.Format("PathAutostartFmt", _autostartManager.DesktopEntryPath);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _saveFeedbackCts?.Cancel();
        _saveFeedbackCts?.Dispose();
        _saveFeedbackCts = new CancellationTokenSource();
        var token = _saveFeedbackCts.Token;

        IsSavedFeedback = false;
        IsSaving = true;

        try
        {
            var patterns = IgnorePatternsText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var portValue = (int)(EmbeddedApiPort ?? 5080);
            var intervalValue = (int)(SyncIntervalSeconds ?? 120);
            var languageCode = SelectedLanguageIndex == 1 ? "en-US" : "pt-BR";

            var updateIntervalDays = SelectedUpdateFrequencyIndex switch
            {
                3 => 0,
                2 => 7,
                1 => 15,
                _ => 30
            };

            var updated = new AppSettings
            {
                Language = languageCode,
                UiAccent = SelectedAccentIndex == 1 ? ThemeAccentService.EmeraldAccentId : ThemeAccentService.BlueAccentId,
                ActiveAccountId = _settingsService.Current.ActiveAccountId,
                UiScalePercent = Math.Clamp(UiScalePercent, 100, 300),
                StartOnBoot = StartOnBoot,
                NotificationsEnabled = NotificationsEnabled,
                NotificationSoundEnabled = NotificationSoundEnabled,
                NotificationSoundTone = NotificationSoundService.GetPresetIdByIndex(SelectedNotificationSoundIndex),
                GmailAccountAlertsEnabled = GmailAccountAlertsEnabled,
                MailerWebhookUrl = _settingsService.Current.MailerWebhookUrl,
                AcceptedLegalVersion = _settingsService.Current.AcceptedLegalVersion,
                FolderAppearances = new Dictionary<string, FolderAppearanceOverride>(_settingsService.Current.FolderAppearances, StringComparer.Ordinal),
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                AutoCheckUpdates = AutoCheckUpdates,
                UpdateCheckIntervalDays = updateIntervalDays,
                LastUpdateCheckUtc = _settingsService.Current.LastUpdateCheckUtc,
                DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory)
                    ? AppPaths.DefaultDownloadDirectory
                    : Path.GetFullPath(DownloadDirectory.Trim()),
                SyncIntervalSeconds = Math.Clamp(intervalValue, 30, 86400),
                WatchLocalChanges = WatchLocalChanges,
                EmbeddedApiEnabled = EmbeddedApiEnabled,
                EmbeddedApiPort = portValue is >= 1024 and <= 65535 ? portValue : 5080,
                AutoMemoryCleanupEnabled = AutoMemoryCleanupEnabled,
                MemoryCleanupIntervalMinutes = Math.Clamp((int)(MemoryCleanupIntervalMinutes ?? 5), 1, 60),
                ConflictPolicy = (ConflictResolutionPolicy)Math.Clamp(SelectedConflictPolicyIndex, 0, 3),
                IgnorePatterns = patterns
            };

            _autostartManager.SetEnabled(StartOnBoot);
            await _settingsService.SaveAsync(updated);
            _memoryCleaner.NotifySettingsUpdated();
            Loc.SetLanguage(languageCode);

            // Brief pause so the spinning indicator is clearly perceived before switching to "Saved!".
            await Task.Delay(320, token);
            IsSaving = false;
            IsSavedFeedback = true;
            StatusMessage = string.Empty;
            _ = ClearSaveFeedbackLaterAsync(token);
        }
        catch (OperationCanceledException)
        {
            IsSaving = false;
        }
    }

    private async Task ClearSaveFeedbackLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(2000, token);
            IsSavedFeedback = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task ReloadSettingsAsync()
    {
        _reloadFeedbackCts?.Cancel();
        _reloadFeedbackCts?.Dispose();
        _reloadFeedbackCts = new CancellationTokenSource();
        var token = _reloadFeedbackCts.Token;

        IsReloadedFeedback = false;
        IsReloading = true;

        try
        {
            await _settingsService.LoadAsync();
            LoadFromCurrent();

            await Task.Delay(420, token);
            IsReloading = false;
            IsReloadedFeedback = true;
            StatusMessage = string.Empty;
            _ = ClearReloadFeedbackLaterAsync(token);
        }
        catch (OperationCanceledException)
        {
            IsReloading = false;
        }
    }

    private async Task ClearReloadFeedbackLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(1800, token);
            IsReloadedFeedback = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task BrowseDownloadDirectoryAsync()
    {
        if (PickDownloadDirectoryCallback is null)
        {
            return;
        }

        var selected = await PickDownloadDirectoryCallback.Invoke();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            DownloadDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task TestNotificationAsync()
    {
        await _notificationService.NotifyAsync(
            Loc.Text("TestNotificationTitle"),
            Loc.Text("TestNotificationBody"),
            NotificationUrgency.Normal,
            bypassThrottle: true);
    }

    [RelayCommand]
    private async Task PreviewNotificationSoundAsync()
    {
        if (_soundService is null)
        {
            return;
        }

        var toneId = NotificationSoundService.GetPresetIdByIndex(SelectedNotificationSoundIndex);
        await _soundService.PlayToneAsync(toneId, isError: false, bypassDebounce: true);
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
    private async Task OpenApiDocsAsync()
    {
        if (OpenExternalUriCallback is null)
        {
            return;
        }

        var pdfPath = ResolveLocalDocsPath("Custodia-API.pdf") ?? ResolveLocalDocsPath("TundraDrive-API.pdf");
        if (pdfPath is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(pdfPath));
            return;
        }

        var localDocsPath = ResolveLocalDocsPath("docs", "API.md");
        var target = localDocsPath is not null
            ? new Uri(localDocsPath)
            : new Uri($"{RepositoryUrl}/blob/main/Custodia-API.pdf");
        await OpenExternalUriCallback.Invoke(target);
    }

    [RelayCommand]
    private async Task OpenApiSpecAsync()
    {
        if (OpenExternalUriCallback is not null && _apiServer.IsRunning)
        {
            await OpenExternalUriCallback.Invoke(new Uri($"{_apiServer.BoundAddress}/api/v1/openapi.json"));
        }
    }

    private static string? ResolveLocalDocsPath(params string[] segments)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
