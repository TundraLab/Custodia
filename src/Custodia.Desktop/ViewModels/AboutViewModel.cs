using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
    private static readonly IBrush BrushPrimary = SolidColorBrush.Parse("#F0F4F8");
    private static readonly IBrush BrushError = SolidColorBrush.Parse("#FCA5A5");

    private readonly IAppUpdateService? _updateService;
    private readonly IAppSettingsService? _settingsService;
    private readonly ISyncStateRepository? _repository;
    private AppUpdateCheckResult? _latestCheckResult;

    public TimeSpan MinimumCheckAnimationDuration { get; set; } = TimeSpan.FromSeconds(5);

    public ILocalizationService Loc { get; }
    public string CompanyName => "TundraLab";
    public string ProductName => "Custódia";
    public string DeveloperName => "Gustavo Henrique Novaes Gomes";
    public string RepositoryUrl => MainWindowViewModel.RepositoryUrl;
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? CopyTextCallback { get; set; }
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
    private string _updateScheduleSummary = string.Empty;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _installedModeSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateStatusBrush))]
    private bool _isUpdateError;

    public IBrush UpdateStatusBrush => IsUpdateError ? BrushError : BrushPrimary;

    [ObservableProperty]
    private bool _isBugReportOpen;

    [ObservableProperty]
    private string _bugTitle = string.Empty;

    [ObservableProperty]
    private string _bugDescription = string.Empty;

    [ObservableProperty]
    private bool _isSuggestionOpen;

    [ObservableProperty]
    private string _suggestionTitle = string.Empty;

    [ObservableProperty]
    private string _suggestionDescription = string.Empty;

    [ObservableProperty]
    private int _selectedSuggestionCategoryIndex;

    [ObservableProperty]
    private string _feedbackStatusMessage = string.Empty;

    public ObservableCollection<string> SuggestionCategories { get; } = [];

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
        IAppSettingsService? settingsService = null,
        ISyncStateRepository? repository = null)
    {
        Loc = localization;
        _updateService = updateService;
        _settingsService = settingsService;
        _repository = repository;
        RebuildSuggestionCategories();
        RefreshInstalledModeSummary();
        RefreshLastCheckSummary();
        RefreshUpdateScheduleSummary();

        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged += (_, _) =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    RefreshLastCheckSummary();
                    RefreshUpdateScheduleSummary();
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RefreshLastCheckSummary();
                        RefreshUpdateScheduleSummary();
                    });
                }
            };
        }

        Loc.LanguageChanged += (_, _) =>
        {
            RebuildSuggestionCategories();
            RefreshInstalledModeSummary();
            RefreshLastCheckSummary();
            RefreshUpdateScheduleSummary();
            OnPropertyChanged(nameof(CheckUpdatesButtonText));
            RefreshLocalizedStatusFromCache();
        };
    }

    public void RefreshLastCheckSummary()
    {
        var lastUtc = _settingsService?.Current.LastUpdateCheckUtc;
        if (!lastUtc.HasValue)
        {
            LastCheckSummary = Loc.Format("UpdateLastCheckFmt", Loc.Text("UpdateLastCheckNever"));
            return;
        }

        var relative = Loc.FormatRelativeTimestamp(lastUtc);
        var formattedDate = lastUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo(Loc.CurrentLanguage));
        LastCheckSummary = Loc.Format("UpdateLastCheckWithDateFmt", relative, formattedDate);
    }

    public void RefreshUpdateScheduleSummary()
    {
        var settings = _settingsService?.Current;
        if (settings is null || !settings.AutoCheckUpdates)
        {
            UpdateScheduleSummary = Loc.Text("UpdateScheduleDisabled");
            return;
        }

        var freqLabel = settings.UpdateCheckIntervalDays switch
        {
            0 => Loc.Text("UpdateFrequencyOnStartup"),
            7 => Loc.Text("UpdateFrequencyWeekly"),
            15 => Loc.Text("UpdateFrequencyBiweekly"),
            _ => Loc.Text("UpdateFrequencyMonthly")
        };

        var nextUtc = UpdateSchedulePolicy.GetNextScheduledCheckUtc(settings);
        if (!nextUtc.HasValue)
        {
            UpdateScheduleSummary = Loc.Format("UpdateScheduleStartupFmt", freqLabel);
            return;
        }

        var remaining = nextUtc.Value - DateTimeOffset.UtcNow;
        var daysLeft = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        var nextDate = nextUtc.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo(Loc.CurrentLanguage));
        var nextCountdown = daysLeft <= 0
            ? Loc.Text("UpdateNextSoon")
            : Loc.Format("UpdateNextInDaysFmt", nextDate, daysLeft);

        UpdateScheduleSummary = Loc.Format("UpdateScheduleAutoFmt", freqLabel, nextCountdown);
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

        using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var checkTask = _updateService.CheckForUpdatesAsync(checkCts.Token);

            if (useSearchAnimation && MinimumCheckAnimationDuration > TimeSpan.Zero)
            {
                await RunSearchAnimationAsync(checkTask, MinimumCheckAnimationDuration, checkCts.Token);
            }

            var result = await checkTask;
            await RecordLastCheckTimestampAsync();
            ApplyCheckResult(result);
            return result;
        }
        catch (UpdateOfflineException)
        {
            IsUpdateError = true;
            UpdateStatusText = Loc.Text("UpdateOfflineError");
            return null;
        }
        catch (HttpRequestException)
        {
            IsUpdateError = true;
            UpdateStatusText = Loc.Text("UpdateOfflineError");
            return null;
        }
        catch (OperationCanceledException)
        {
            IsUpdateError = true;
            UpdateStatusText = Loc.Text("UpdateOfflineError");
            return null;
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
            RefreshLastCheckSummary();
            RefreshUpdateScheduleSummary();
        }
    }

    private async Task RunSearchAnimationAsync(Task checkTask, TimeSpan duration, CancellationToken ct)
    {
        var totalMs = Math.Max(100.0, duration.TotalMilliseconds);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < duration || !checkTask.IsCompleted)
        {
            if (checkTask.IsFaulted || checkTask.IsCanceled || ct.IsCancellationRequested)
            {
                break;
            }

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
            try
            {
                await Task.Delay(nextTickDelay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        UpdateCheckProgress = checkTask.IsFaulted || checkTask.IsCanceled ? 0 : 100;
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

        var assetName = _latestCheckResult.PreferredAsset?.Name ?? "Custodia";
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

    private void RebuildSuggestionCategories()
    {
        var prevIndex = Math.Max(0, SelectedSuggestionCategoryIndex);
        SuggestionCategories.Clear();
        SuggestionCategories.Add(Loc.Text("SuggestionCatFeature"));
        SuggestionCategories.Add(Loc.Text("SuggestionCatSync"));
        SuggestionCategories.Add(Loc.Text("SuggestionCatInterface"));
        SuggestionCategories.Add(Loc.Text("SuggestionCatIntegration"));
        SelectedSuggestionCategoryIndex = Math.Clamp(prevIndex, 0, SuggestionCategories.Count - 1);
    }

    [RelayCommand]
    private void ToggleBugReport()
    {
        IsBugReportOpen = !IsBugReportOpen;
        if (IsBugReportOpen)
        {
            IsSuggestionOpen = false;
        }
        FeedbackStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ToggleSuggestion()
    {
        IsSuggestionOpen = !IsSuggestionOpen;
        if (IsSuggestionOpen)
        {
            IsBugReportOpen = false;
        }
        FeedbackStatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SubmitBugReportGitHubAsync()
    {
        if (string.IsNullOrWhiteSpace(BugTitle) && string.IsNullOrWhiteSpace(BugDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        var title = string.IsNullOrWhiteSpace(BugTitle) ? "[Bug] Custódia" : $"[Bug] {BugTitle.Trim()}";
        var body = await BuildBugReportMarkdownAsync();
        await OpenGitHubIssueAsync(title, body, "bug");
    }

    [RelayCommand]
    private async Task CopyBugReportAsync()
    {
        if (string.IsNullOrWhiteSpace(BugTitle) && string.IsNullOrWhiteSpace(BugDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        var title = string.IsNullOrWhiteSpace(BugTitle) ? "[Bug] Custódia" : $"[Bug] {BugTitle.Trim()}";
        var body = $"# {title}\n\n{await BuildBugReportMarkdownAsync()}";
        if (CopyTextCallback is not null)
        {
            await CopyTextCallback.Invoke(body);
            FeedbackStatusMessage = Loc.Text("FeedbackCopiedStatus");
        }
    }

    [RelayCommand]
    private async Task SubmitSuggestionGitHubAsync()
    {
        if (string.IsNullOrWhiteSpace(SuggestionTitle) && string.IsNullOrWhiteSpace(SuggestionDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        var category = SelectedSuggestionCategoryIndex >= 0 && SelectedSuggestionCategoryIndex < SuggestionCategories.Count
            ? SuggestionCategories[SelectedSuggestionCategoryIndex]
            : Loc.Text("SuggestionCatFeature");
        var title = string.IsNullOrWhiteSpace(SuggestionTitle)
            ? $"[Sugestão] {category}"
            : $"[Sugestão] {SuggestionTitle.Trim()}";
        var body = BuildSuggestionMarkdown(category);
        await OpenGitHubIssueAsync(title, body, "enhancement");
    }

    [RelayCommand]
    private async Task CopySuggestionAsync()
    {
        if (string.IsNullOrWhiteSpace(SuggestionTitle) && string.IsNullOrWhiteSpace(SuggestionDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        var category = SelectedSuggestionCategoryIndex >= 0 && SelectedSuggestionCategoryIndex < SuggestionCategories.Count
            ? SuggestionCategories[SelectedSuggestionCategoryIndex]
            : Loc.Text("SuggestionCatFeature");
        var title = string.IsNullOrWhiteSpace(SuggestionTitle)
            ? $"[Sugestão] {category}"
            : $"[Sugestão] {SuggestionTitle.Trim()}";
        var body = $"# {title}\n\n{BuildSuggestionMarkdown(category)}";
        if (CopyTextCallback is not null)
        {
            await CopyTextCallback.Invoke(body);
            FeedbackStatusMessage = Loc.Text("FeedbackCopiedStatus");
        }
    }

    private string BuildSuggestionMarkdown(string category)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Categoria:** {category}");
        sb.AppendLine($"**Versão:** Custódia v{AppVersionInfo.CurrentVersion}");
        sb.AppendLine();
        sb.AppendLine("## Descrição da sugestão");
        sb.AppendLine(string.IsNullOrWhiteSpace(SuggestionDescription) ? "(sem detalhes)" : SuggestionDescription.Trim());
        return sb.ToString();
    }

    private Task<string> BuildBugReportMarkdownAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## O que aconteceu");
        sb.AppendLine(string.IsNullOrWhiteSpace(BugDescription) ? "(descreva o comportamento observado)" : BugDescription.Trim());
        return Task.FromResult(sb.ToString());
    }

    private async Task OpenGitHubIssueAsync(string title, string body, string label)
    {
        if (OpenExternalUriCallback is null)
        {
            return;
        }

        const int maxBodyLen = 3200;
        var truncatedBody = body.Length > maxBodyLen ? body[..maxBodyLen] + "\n..." : body;
        var url = $"{RepositoryUrl}/issues/new?labels={Uri.EscapeDataString(label)}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(truncatedBody)}";
        await OpenExternalUriCallback.Invoke(new Uri(url));
        FeedbackStatusMessage = Loc.Text("FeedbackOpenedStatus");
    }
}
