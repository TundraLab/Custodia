using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
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
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
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
    public Func<AppUpdateCheckResult, Task>? OpenInstallerWindowCallback { get; set; }
    public Action? RequestAppShutdownCallback { get; set; }
    public Action? ReviewLegalTermsCallback { get; set; }

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

    public IBrush UpdateStatusBrush => IsUpdateError
        ? ThemeAccentService.ResolveThemeBrush("#FFB4AB", "#B3261E")
        : ThemeAccentService.ResolveThemeBrush("#E2E2E9", "#1A1C20");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBugMode))]
    [NotifyPropertyChangedFor(nameof(FeedbackTitleLabel))]
    [NotifyPropertyChangedFor(nameof(FeedbackTitleWatermark))]
    [NotifyPropertyChangedFor(nameof(FeedbackDescriptionLabel))]
    [NotifyPropertyChangedFor(nameof(FeedbackDescriptionWatermark))]
    private bool _isSuggestionMode;

    public bool IsBugMode => !IsSuggestionMode;

    [ObservableProperty]
    private string _feedbackTitle = string.Empty;

    [ObservableProperty]
    private string _feedbackDescription = string.Empty;

    [ObservableProperty]
    private int _selectedSuggestionCategoryIndex;

    [ObservableProperty]
    private bool _includeDiagnostics;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewDiagnosticsButtonText))]
    private bool _isDiagnosticsPreviewOpen;

    [ObservableProperty]
    private string _diagnosticsPreviewText = string.Empty;

    [ObservableProperty]
    private string _feedbackStatusMessage = string.Empty;

    public ObservableCollection<string> SuggestionCategories { get; } = [];

    public string FeedbackTitleLabel => IsSuggestionMode ? Loc.SuggestionTitleLabel : Loc.BugTitleLabel;
    public string FeedbackTitleWatermark => IsSuggestionMode ? Loc.SuggestionTitleWatermark : Loc.BugTitleWatermark;
    public string FeedbackDescriptionLabel => IsSuggestionMode ? Loc.SuggestionDescriptionLabel : Loc.BugDescriptionLabel;
    public string FeedbackDescriptionWatermark => IsSuggestionMode ? Loc.SuggestionDescriptionWatermark : Loc.BugDescriptionWatermark;
    public string PreviewDiagnosticsButtonText => IsDiagnosticsPreviewOpen ? Loc.HideDiagnosticsButton : Loc.PreviewDiagnosticsButton;

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
            OnPropertyChanged(nameof(FeedbackTitleLabel));
            OnPropertyChanged(nameof(FeedbackTitleWatermark));
            OnPropertyChanged(nameof(FeedbackDescriptionLabel));
            OnPropertyChanged(nameof(FeedbackDescriptionWatermark));
            OnPropertyChanged(nameof(PreviewDiagnosticsButtonText));
            RefreshLocalizedStatusFromCache();
        };

        ThemeAccentService.ThemeChanged += (_, _) =>
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(UpdateStatusBrush)));
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
    private async Task OpenPrivacyPolicyAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(
                MainWindowViewModel.ResolveLegalDocumentUri("PRIVACY.md", MainWindowViewModel.PrivacyPolicyUrl));
        }
    }

    [RelayCommand]
    private async Task OpenTermsOfUseAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(
                MainWindowViewModel.ResolveLegalDocumentUri("TERMS.md", MainWindowViewModel.TermsOfUseUrl));
        }
    }

    [RelayCommand]
    private async Task OpenGoogleApiPolicyAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(MainWindowViewModel.GoogleApiUserDataPolicyUrl));
        }
    }

    [RelayCommand]
    private void ReviewLegalTerms()
    {
        ReviewLegalTermsCallback?.Invoke();
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

        if (OpenInstallerWindowCallback is not null)
        {
            await OpenInstallerWindowCallback.Invoke(_latestCheckResult);
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

    public const int MaxGitHubIssueUrlLength = 5800;

    private static readonly Regex EmailRedactRegex = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);

    private static readonly Regex HomePathRedactRegex = new(
        @"(/home/|/Users/)[^/\s]+",
        RegexOptions.Compiled);

    private static readonly Regex WindowsUserPathRedactRegex = new(
        @"(C:\\Users\\)[^\\/\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [RelayCommand]
    private void SelectBugMode()
    {
        IsSuggestionMode = false;
        FeedbackStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SelectSuggestionMode()
    {
        IsSuggestionMode = true;
        IsDiagnosticsPreviewOpen = false;
        FeedbackStatusMessage = string.Empty;
    }

    partial void OnIncludeDiagnosticsChanged(bool value)
    {
        if (IsDiagnosticsPreviewOpen)
        {
            _ = RefreshDiagnosticsPreviewAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleDiagnosticsPreviewAsync()
    {
        IsDiagnosticsPreviewOpen = !IsDiagnosticsPreviewOpen;
        if (IsDiagnosticsPreviewOpen)
        {
            await RefreshDiagnosticsPreviewAsync();
        }
    }

    private async Task RefreshDiagnosticsPreviewAsync()
    {
        DiagnosticsPreviewText = await BuildSanitizedDiagnosticsMarkdownAsync(maxLogLines: 20);
    }

    [RelayCommand]
    public async Task SubmitFeedbackGitHubAsync()
    {
        if (string.IsNullOrWhiteSpace(FeedbackTitle) && string.IsNullOrWhiteSpace(FeedbackDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        if (OpenExternalUriCallback is null)
        {
            return;
        }

        var (title, label) = BuildIssueTitleAndLabel();
        var body = await BuildFeedbackMarkdownAsync(maxLogLines: 20);
        var url = BuildGitHubIssueUrl(title, body, label, out var wasTrimmed);
        if (wasTrimmed && IncludeDiagnostics)
        {
            var compactBody = await BuildFeedbackMarkdownAsync(maxLogLines: 8);
            var compactUrl = BuildGitHubIssueUrl(title, compactBody, label, out var stillTrimmed);
            if (!stillTrimmed)
            {
                url = compactUrl;
            }
            else
            {
                var minBody = await BuildFeedbackMarkdownAsync(maxLogLines: 0);
                url = BuildGitHubIssueUrl(title, minBody, label, out _);
            }
        }

        await OpenExternalUriCallback.Invoke(new Uri(url));
        FeedbackStatusMessage = wasTrimmed
            ? Loc.Text("FeedbackUrlTrimmedStatus")
            : Loc.Text("FeedbackOpenedStatus");
    }

    [RelayCommand]
    public async Task CopyFeedbackAsync()
    {
        if (string.IsNullOrWhiteSpace(FeedbackTitle) && string.IsNullOrWhiteSpace(FeedbackDescription))
        {
            FeedbackStatusMessage = Loc.Text("FeedbackValidationHint");
            return;
        }

        var (title, _) = BuildIssueTitleAndLabel();
        var body = $"# {title}\n\n{await BuildFeedbackMarkdownAsync(maxLogLines: 100)}";
        if (CopyTextCallback is not null)
        {
            await CopyTextCallback.Invoke(body);
            FeedbackStatusMessage = Loc.Text("FeedbackCopiedStatus");
        }
    }

    private (string Title, string Label) BuildIssueTitleAndLabel()
    {
        if (IsSuggestionMode)
        {
            var category = SelectedSuggestionCategoryIndex >= 0 && SelectedSuggestionCategoryIndex < SuggestionCategories.Count
                ? SuggestionCategories[SelectedSuggestionCategoryIndex]
                : Loc.Text("SuggestionCatFeature");
            var title = string.IsNullOrWhiteSpace(FeedbackTitle)
                ? $"[Sugestão] {category}"
                : $"[Sugestão] {FeedbackTitle.Trim()}";
            return (title, "enhancement");
        }

        var bugTitle = string.IsNullOrWhiteSpace(FeedbackTitle)
            ? "[Bug] Custódia"
            : $"[Bug] {FeedbackTitle.Trim()}";
        return (bugTitle, "bug");
    }

    private async Task<string> BuildFeedbackMarkdownAsync(int maxLogLines)
    {
        var sb = new StringBuilder();
        if (IsSuggestionMode)
        {
            var category = SelectedSuggestionCategoryIndex >= 0 && SelectedSuggestionCategoryIndex < SuggestionCategories.Count
                ? SuggestionCategories[SelectedSuggestionCategoryIndex]
                : Loc.Text("SuggestionCatFeature");
            sb.AppendLine($"**Categoria:** {category}");
            sb.AppendLine($"**Versão:** Custódia v{AppVersionInfo.CurrentVersion}");
            sb.AppendLine();
            sb.AppendLine("## Benefício esperado e detalhes");
            sb.AppendLine(string.IsNullOrWhiteSpace(FeedbackDescription) ? "(sem detalhes)" : FeedbackDescription.Trim());
        }
        else
        {
            sb.AppendLine("## O que aconteceu");
            sb.AppendLine(string.IsNullOrWhiteSpace(FeedbackDescription) ? "(descreva o comportamento observado)" : FeedbackDescription.Trim());
        }

        if (!IsSuggestionMode && IncludeDiagnostics)
        {
            sb.AppendLine();
            sb.Append(await BuildSanitizedDiagnosticsMarkdownAsync(maxLogLines));
        }

        return sb.ToString();
    }

    internal async Task<string> BuildSanitizedDiagnosticsMarkdownAsync(int maxLogLines)
    {
        var kind = _updateService?.DetectInstallKind(out _) ?? AppInstallKind.Standalone;
        var modeName = kind switch
        {
            AppInstallKind.Rpm => "RPM",
            AppInstallKind.AppImage => "AppImage",
            _ => "Standalone"
        };

        var sb = new StringBuilder();
        sb.AppendLine("## Diagnóstico técnico (sem dados pessoais)");
        sb.AppendLine($"- Versão: Custódia v{AppVersionInfo.CurrentVersion} ({modeName})");
        sb.AppendLine($"- Sistema: {SanitizeDiagnosticText(RuntimeInformation.OSDescription)} ({RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"- .NET: {Environment.Version}");
        sb.AppendLine($"- Idioma: {Loc.CurrentLanguage}");

        if (_repository is null)
        {
            return sb.ToString();
        }

        try
        {
            var recent = await _repository.GetActivityLogsAsync(minSeverity: ActivitySeverity.Warning, limit: 150);
            var alerts = recent
                .Where(l => l.Severity is ActivitySeverity.Warning or ActivitySeverity.Error)
                .OrderByDescending(l => l.CreatedAtUtc)
                .ToList();

            sb.AppendLine();
            sb.AppendLine($"### Últimos avisos/erros ({alerts.Count} registrado(s))");

            if (alerts.Count == 0)
            {
                sb.AppendLine("- (nenhum aviso/erro recente)");
                return sb.ToString();
            }

            if (maxLogLines <= 0)
            {
                sb.AppendLine("- (... log omitido para caber na URL; use 'Copiar texto' para ver completo)");
                return sb.ToString();
            }

            var shown = alerts.Take(maxLogLines).ToList();
            foreach (var entry in shown)
            {
                var level = entry.Severity == ActivitySeverity.Error ? "ERRO" : "AVISO";
                var msg = SanitizeDiagnosticText(entry.Message);
                if (msg.Length > 140)
                {
                    msg = msg[..137] + "...";
                }
                sb.AppendLine($"- [{entry.CreatedAtUtc.ToLocalTime():dd/MM HH:mm}] [{level}] {entry.Action}: {msg}");
            }

            if (alerts.Count > shown.Count)
            {
                sb.AppendLine($"- (... +{alerts.Count - shown.Count} mais; use 'Copiar texto' para o log completo)");
            }
        }
        catch
        {
            sb.AppendLine("- (não foi possível ler os registros recentes)");
        }

        return sb.ToString();
    }

    internal static string SanitizeDiagnosticText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cleaned = EmailRedactRegex.Replace(input, "[email]");
        cleaned = HomePathRedactRegex.Replace(cleaned, "$1[user]");
        cleaned = WindowsUserPathRedactRegex.Replace(cleaned, "$1[user]");
        return cleaned.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    internal static string BuildGitHubIssueUrl(string title, string body, string label, out bool wasTrimmed)
    {
        wasTrimmed = false;
        var basePrefix = $"{MainWindowViewModel.RepositoryUrl}/issues/new?labels={Uri.EscapeDataString(label)}&title={Uri.EscapeDataString(title)}&body=";
        var fullUrl = basePrefix + Uri.EscapeDataString(body);
        if (fullUrl.Length <= MaxGitHubIssueUrlLength)
        {
            return fullUrl;
        }

        wasTrimmed = true;
        const string suffix = "\n\n... (texto resumido pelo limite de URL; use 'Copiar texto' para enviar completo)";
        var low = 0;
        var high = body.Length;
        var best = suffix;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            var candidate = body[..mid] + suffix;
            if (basePrefix.Length + Uri.EscapeDataString(candidate).Length <= MaxGitHubIssueUrlLength)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return basePrefix + Uri.EscapeDataString(best);
    }
}
