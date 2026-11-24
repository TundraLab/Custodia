using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed record AccountChoiceItem(string Id, string Email, string DisplayLabel)
{
    public override string ToString() => DisplayLabel;
}

public sealed partial class AccountsViewModel : ViewModelBase
{
    private const string GoogleDriveApiLibraryUrl = "https://console.cloud.google.com/apis/library/drive.googleapis.com";
    private const string GoogleCloudCredentialsUrl = "https://console.cloud.google.com/apis/credentials";
    private const string GoogleOneStorageUrl = "https://one.google.com/storage";

    private readonly IDriveSyncService _syncService;
    private readonly IAuthCredentialManager _authManager;
    private DriveAccount _lastAccount = new();
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _refreshCts;
    private double _storageTrackWidth = 540.0;
    private bool _isUpdatingAccountSelection;

    public ILocalizationService Loc { get; }

    public ObservableCollection<AccountChoiceItem> SavedAccounts { get; } = [];

    [ObservableProperty]
    private int _selectedAccountIndex = -1;

    public bool HasMultipleAccounts => SavedAccounts.Count > 1;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _emailAddress = string.Empty;

    [ObservableProperty]
    private double _quotaPercentage;

    [ObservableProperty]
    private string _quotaUsageText = string.Empty;

    [ObservableProperty]
    private string _quotaBreakdownText = string.Empty;

    [ObservableProperty]
    private string _shortQuotaText = string.Empty;

    [ObservableProperty]
    private string _driveLegendText = string.Empty;

    [ObservableProperty]
    private string _otherLegendText = string.Empty;

    [ObservableProperty]
    private string _trashLegendText = string.Empty;

    [ObservableProperty]
    private bool _hasStorageAlert;

    [ObservableProperty]
    private bool _isStorageFull;

    [ObservableProperty]
    private string _storageAlertMessage = string.Empty;

    [ObservableProperty]
    private IBrush _storageProgressBrush = SolidColorBrush.Parse("#3B82F6");

    [ObservableProperty]
    private IBrush _storageAlertTextBrush = SolidColorBrush.Parse("#D29922");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountAvatar))]
    private Bitmap? _accountAvatarBitmap;

    public bool HasAccountAvatar => AccountAvatarBitmap is not null;

    [ObservableProperty]
    private double _driveBarWidth;

    [ObservableProperty]
    private double _otherBarWidth;

    [ObservableProperty]
    private double _trashBarWidth;

    [ObservableProperty]
    private string _credentialsPath = string.Empty;

    [ObservableProperty]
    private bool _credentialsFilePresent;

    [ObservableProperty]
    private string _credentialsStatusText = string.Empty;

    [ObservableProperty]
    private string _tokensDirectoryPath = string.Empty;

    [ObservableProperty]
    private string _tokenSecurityText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefaultRefreshIcon))]
    private bool _isRefreshingQuota;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefaultRefreshIcon))]
    private bool _isQuotaRefreshed;

    public bool ShowDefaultRefreshIcon => !IsRefreshingQuota && !IsQuotaRefreshed;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isAddAccountPromptVisible;

    [ObservableProperty]
    private bool _isDisconnectPromptVisible;

    [ObservableProperty]
    private bool _deleteFoldersOnDisconnect;

    public Func<Task<string?>>? PickCredentialsFileCallback { get; set; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? CopyTextCallback { get; set; }

    [RelayCommand]
    private async Task CopyEmailAsync()
    {
        if (CopyTextCallback is not null && !string.IsNullOrWhiteSpace(EmailAddress))
        {
            await CopyTextCallback.Invoke(EmailAddress);
        }
    }

    public AccountsViewModel(
        IDriveSyncService syncService,
        IAuthCredentialManager authManager,
        ILocalizationService localization)
    {
        _syncService = syncService;
        _authManager = authManager;
        Loc = localization;

        Loc.LanguageChanged += (_, _) => ApplyAccount(_lastAccount);
        _syncService.AccountsListChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            RefreshCredentialDiagnostics();
            RefreshSavedAccountsList();
        });
        RefreshCredentialDiagnostics();
        RefreshSavedAccountsList();
        ApplyAccount(_syncService.CurrentAccount);
    }

    partial void OnSelectedAccountIndexChanged(int value)
    {
        if (_isUpdatingAccountSelection || value < 0 || value >= SavedAccounts.Count)
        {
            return;
        }

        var chosen = SavedAccounts[value];
        if (!string.Equals(chosen.Id, _syncService.CurrentAccount.Id, StringComparison.Ordinal) || !_syncService.CurrentAccount.IsConnected)
        {
            _ = SwitchAccountInternalAsync(chosen.Id);
        }
    }

    private void RefreshSavedAccountsList()
    {
        _isUpdatingAccountSelection = true;
        try
        {
            SavedAccounts.Clear();
            var list = _syncService.SavedAccounts
                .Where(a => a.IsConnected)
                .ToList();

            foreach (var acc in list)
            {
                var label = string.IsNullOrWhiteSpace(acc.EmailAddress)
                    ? (string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.Id : acc.DisplayName)
                    : acc.EmailAddress;
                SavedAccounts.Add(new AccountChoiceItem(acc.Id, acc.EmailAddress, label));
            }

            SelectedAccountIndex = list.FindIndex(a => string.Equals(a.Id, _syncService.CurrentAccount.Id, StringComparison.Ordinal));
            OnPropertyChanged(nameof(HasMultipleAccounts));
        }
        finally
        {
            _isUpdatingAccountSelection = false;
        }
    }

    private async Task SwitchAccountInternalAsync(string accountId)
    {
        IsBusy = true;
        try
        {
            var switched = await _syncService.SwitchAccountAsync(accountId);
            if (switched is not null)
            {
                ApplyAccount(switched);
                StatusMessage = Loc.Format("SwitchedToFmt", switched.EmailAddress);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("ConnectErrorFmt", ex.Message);
        }
        finally
        {
            RefreshSavedAccountsList();
            IsBusy = false;
        }
    }

    public void RefreshCredentialDiagnostics()
    {
        CredentialsPath = _authManager.CredentialsFilePath;
        TokensDirectoryPath = _authManager.TokensDirectoryPath;
        CredentialsFilePresent = _authManager.CredentialsFileExists();

        CredentialsStatusText = CredentialsFilePresent
            ? Loc.Text("CredentialsReady")
            : Loc.Text("CredentialsMissing");

        var dirPerm = PosixPermissionHelper.GetOctalPermissions(TokensDirectoryPath) ?? "----";
        TokenSecurityText = Loc.Format("TokenSecurityFmt", dirPerm);
    }

    public void ApplyAccount(DriveAccount account)
    {
        _lastAccount = account;
        IsConnected = account.IsConnected;

        if (account.IsConnected)
        {
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName)
                ? Loc.Text("GoogleAccountFallback")
                : account.DisplayName;
            EmailAddress = account.EmailAddress;
            QuotaPercentage = account.Quota.UsagePercentage;
            var limitStr = account.Quota.LimitBytes > 0
                ? UiConverters.FormatBytes(account.Quota.LimitBytes)
                : Loc.Text("Unlimited");
            QuotaUsageText = Loc.Format("QuotaUsageFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limitStr);
            ShortQuotaText = Loc.Format("StorageShortFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limitStr);
            var otherUsageBytes = Math.Max(0, account.Quota.UsageBytes - account.Quota.UsageInDriveBytes - account.Quota.UsageInTrashBytes);
            var driveFormatted = UiConverters.FormatBytes(account.Quota.UsageInDriveBytes);
            var otherFormatted = UiConverters.FormatBytes(otherUsageBytes);
            var trashFormatted = UiConverters.FormatBytes(account.Quota.UsageInTrashBytes);
            QuotaBreakdownText = Loc.Format("QuotaBreakdownFmt", driveFormatted, otherFormatted, trashFormatted);
            DriveLegendText = Loc.Format("StorageDriveValueFmt", driveFormatted);
            OtherLegendText = Loc.Format("StorageOtherValueFmt", otherFormatted);
            TrashLegendText = Loc.Format("StorageTrashValueFmt", trashFormatted);

            UpdateSegmentedBarWidths(account);

            if (!string.IsNullOrWhiteSpace(account.PhotoUrl))
            {
                _ = LoadAvatarAsync(account.PhotoUrl);
            }

            IsStorageFull = account.Quota.LimitBytes > 0 &&
                (account.Quota.UsageBytes >= account.Quota.LimitBytes || account.Quota.UsagePercentage >= 99.5);
            HasStorageAlert = account.Quota.LimitBytes > 0 && account.Quota.UsagePercentage >= 80.0;
            StorageAlertMessage = IsStorageFull
                ? Loc.Format("StorageFullFmt", account.Quota.UsagePercentage)
                : HasStorageAlert
                    ? Loc.Format("StorageWarningFmt", account.Quota.UsagePercentage)
                    : string.Empty;
            StorageProgressBrush = IsStorageFull
                ? SolidColorBrush.Parse("#EF4444")
                : HasStorageAlert
                    ? SolidColorBrush.Parse("#D29922")
                    : SolidColorBrush.Parse("#3B82F6");
            StorageAlertTextBrush = IsStorageFull
                ? SolidColorBrush.Parse("#F87171")
                : SolidColorBrush.Parse("#D29922");
        }
        else
        {
            DisplayName = Loc.Text("NoAccountConnected");
            EmailAddress = Loc.Text("ConnectHintSubtitle");
            QuotaPercentage = 0;
            QuotaUsageText = Loc.Format("QuotaUsageFmt", "0 B", "0 B");
            ShortQuotaText = Loc.Text("AccountDisconnected");
            QuotaBreakdownText = Loc.Format("QuotaBreakdownFmt", "0 B", "0 B", "0 B");
            DriveLegendText = Loc.Format("StorageDriveValueFmt", "0 B");
            OtherLegendText = Loc.Format("StorageOtherValueFmt", "0 B");
            TrashLegendText = Loc.Format("StorageTrashValueFmt", "0 B");
            IsStorageFull = false;
            HasStorageAlert = false;
            StorageAlertMessage = string.Empty;
            StorageProgressBrush = SolidColorBrush.Parse("#3B82F6");
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
            AccountAvatarBitmap = null;
        }

        RefreshCredentialDiagnostics();
    }

    private async Task LoadAvatarAsync(string photoUrl)
    {
        var bmp = await AvatarLoader.LoadAsync(photoUrl);
        if (bmp is not null)
        {
            Dispatcher.UIThread.Post(() => AccountAvatarBitmap = bmp);
        }
    }

    [RelayCommand]
    private async Task OpenEnableDriveApiAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(GoogleDriveApiLibraryUrl));
        }
    }

    [RelayCommand]
    private async Task OpenCloudConsoleAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(GoogleCloudCredentialsUrl));
        }
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
    private async Task ImportCredentialsAsync()
    {
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var selectedFile = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _authManager.ImportCredentialsFileAsync(selectedFile);
            RefreshCredentialDiagnostics();
            StatusMessage = Loc.Text("CredentialsImportedSuccess");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("CredentialsImportErrorFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ConnectAsync() => ConnectInternalAsync(addNewAccount: false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddAccountAsync()
    {
        RefreshCredentialDiagnostics();
        if (!CredentialsFilePresent)
        {
            await ImportJsonAndAddAccountAsync();
            return;
        }

        IsAddAccountPromptVisible = !IsAddAccountPromptVisible;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ImportJsonAndAddAccountAsync()
    {
        IsAddAccountPromptVisible = false;
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var selectedFile = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(selectedFile))
        {
            StatusMessage = Loc.Text("MissingCredsPromptPicker");
            return;
        }

        await ConnectInternalAsync(addNewAccount: true, customCredentialsJsonPath: selectedFile);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ContinueWithCurrentJsonAsync()
    {
        IsAddAccountPromptVisible = false;
        await ConnectInternalAsync(addNewAccount: true);
    }

    [RelayCommand]
    private void CancelAddAccountPrompt() => IsAddAccountPromptVisible = false;

    private async Task ConnectInternalAsync(bool addNewAccount, string? customCredentialsJsonPath = null)
    {
        _connectCts?.Cancel();
        IsAddAccountPromptVisible = false;

        RefreshCredentialDiagnostics();
        if (string.IsNullOrWhiteSpace(customCredentialsJsonPath) && !CredentialsFilePresent)
        {
            await ImportCredentialsAsync();
            RefreshCredentialDiagnostics();
            if (!CredentialsFilePresent)
            {
                StatusMessage = Loc.Text("MissingCredsPromptPicker");
                return;
            }
        }

        var localCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _connectCts = localCts;

        IsBusy = true;
        IsConnecting = true;
        StatusMessage = Loc.Text("OpeningBrowserAuth");
        try
        {
            var account = await _syncService.ConnectAccountAsync(addNewAccount, customCredentialsJsonPath, localCts.Token);
            ApplyAccount(account);
            RefreshSavedAccountsList();
            StatusMessage = Loc.Format("ConnectedAsFmt", account.EmailAddress);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_connectCts, localCts))
            {
                StatusMessage = Loc.Text("AuthCancelledMessage");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("ConnectErrorFmt", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_connectCts, localCts))
            {
                _connectCts = null;
                IsConnecting = false;
                IsBusy = false;
            }
            localCts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelConnect()
    {
        _connectCts?.Cancel();
    }

    [RelayCommand]
    private void Disconnect()
    {
        DeleteFoldersOnDisconnect = false;
        IsDisconnectPromptVisible = !IsDisconnectPromptVisible;
    }

    [RelayCommand]
    private async Task ConfirmDisconnectAsync()
    {
        var deleteFolders = DeleteFoldersOnDisconnect;
        IsDisconnectPromptVisible = false;
        DeleteFoldersOnDisconnect = false;

        IsBusy = true;
        StatusMessage = Loc.Text("RevokingTokens");
        try
        {
            await _syncService.DisconnectAccountAsync(deleteFolderConfiguration: deleteFolders);
            ApplyAccount(_syncService.CurrentAccount);
            StatusMessage = deleteFolders
                ? Loc.Text("AccountAndFoldersRemovedFeedback")
                : Loc.Text("AccountRemovedFoldersKeptFeedback");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("DisconnectErrorFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDisconnect()
    {
        IsDisconnectPromptVisible = false;
        DeleteFoldersOnDisconnect = false;
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
        if (account.Quota.LimitBytes > 0 || account.Quota.UsageBytes > 0)
        {
            (DriveBarWidth, OtherBarWidth, TrashBarWidth) = ComputeStorageBarWidths(account.Quota, trackWidth);
        }
        else
        {
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        RefreshCredentialDiagnostics();
        if (!IsConnected)
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _refreshCts = cts;

        IsQuotaRefreshed = false;
        IsRefreshingQuota = true;
        IsBusy = true;
        try
        {
            var updated = await _syncService.RefreshAccountInfoAsync(cts.Token);
            if (updated is not null)
            {
                ApplyAccount(updated);
                StatusMessage = Loc.Text("AccountUpdated");
                IsQuotaRefreshed = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.Text("AuthCancelledMessage");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("AccountUpdateErrorFmt", ex.Message);
        }
        finally
        {
            IsRefreshingQuota = false;
            IsBusy = false;
        }

        if (IsQuotaRefreshed)
        {
            try
            {
                await Task.Delay(1800, cts.Token);
                IsQuotaRefreshed = false;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
