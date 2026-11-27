using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public enum InstallerStepState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public sealed partial class InstallerStepViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    [NotifyPropertyChangedFor(nameof(TitleForeground))]
    private InstallerStepState _state = InstallerStepState.Pending;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detail = string.Empty;

    public bool IsPending => State == InstallerStepState.Pending;
    public bool IsRunning => State == InstallerStepState.Running;
    public bool IsCompleted => State == InstallerStepState.Completed;
    public bool IsFailed => State == InstallerStepState.Failed;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public IBrush RowBackground => State == InstallerStepState.Running
        ? ThemeAccentService.ResolveThemeBrush("#282A2F", "#E6EAF2")
        : Brushes.Transparent;

    public IBrush TitleForeground => State switch
    {
        InstallerStepState.Running => ThemeAccentService.ResolveThemeBrush("#E2E2E9", "#1A1C20"),
        InstallerStepState.Failed => ThemeAccentService.ResolveThemeBrush("#FFB4AB", "#B3261E"),
        InstallerStepState.Completed => ThemeAccentService.ResolveThemeBrush("#C4C6D0", "#44474F"),
        _ => ThemeAccentService.ResolveThemeBrush("#9296A4", "#5F6368")
    };

    public InstallerStepViewModel(string title)
    {
        Title = title;
    }
}

public sealed partial class InstallerViewModel : ViewModelBase, IDisposable
{
    private readonly IAppUpdateService _updateService;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _installCts;
    private AppUpdateCheckResult? _initialCheckResult;

    public ILocalizationService Loc { get; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Action? RequestCloseCallback { get; set; }
    public Action? RequestShutdownForUpdateCallback { get; set; }
    internal string? AppImageTargetPathOverride { get; set; }

    public ObservableCollection<InstallerStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(CanLaunchApp))]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _isFailed;

    [ObservableProperty]
    private string _versionLabel = $"v{AppVersionInfo.CurrentVersion}";

    [ObservableProperty]
    private string _sourceHostLabel = "github.com/TundraLab/Custodia";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _progressSummaryText = "0%";

    [ObservableProperty]
    private string _speedAndEtaText = string.Empty;

    [ObservableProperty]
    private string _elapsedSecondsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsToggleLabel))]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private string _logOutputText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanLaunchApp => IsCompleted && !IsFailed;
    public string DetailsToggleLabel => IsDetailsOpen ? Loc.InstallerDetailsHide : Loc.InstallerDetailsShow;

    public string HeaderTitle => IsFailed
        ? Loc.InstallerTitleFailed
        : IsCompleted
            ? Loc.InstallerTitleCompleted
            : Loc.InstallerTitleInstalling;

    public InstallerViewModel(
        IAppUpdateService updateService,
        ILocalizationService localization,
        AppUpdateCheckResult? preloadedCheckResult = null)
    {
        _updateService = updateService;
        Loc = localization;
        _initialCheckResult = preloadedCheckResult;
        if (!string.IsNullOrWhiteSpace(preloadedCheckResult?.LatestVersion))
        {
            VersionLabel = $"v{preloadedCheckResult.LatestVersion}";
        }

        Steps.Add(new InstallerStepViewModel(Loc.InstallerStepCheckEnv));
        Steps.Add(new InstallerStepViewModel(Loc.InstallerStepDownload));
        Steps.Add(new InstallerStepViewModel(Loc.InstallerStepVerify));
        Steps.Add(new InstallerStepViewModel(Loc.InstallerStepInstall));
        Steps.Add(new InstallerStepViewModel(Loc.InstallerStepFinish));
        ElapsedSecondsText = Loc.Format("InstallerElapsedFmt", 0);
    }

    public async Task RunInstallationAsync()
    {
        if (IsRunning)
        {
            return;
        }

        _installCts?.Cancel();
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        var ct = _installCts.Token;

        IsRunning = true;
        IsCompleted = false;
        IsFailed = false;
        ErrorMessage = string.Empty;
        ProgressPercentage = 0;
        ProgressSummaryText = "0%";
        SpeedAndEtaText = string.Empty;
        OnPropertyChanged(nameof(HasError));

        var sw = Stopwatch.StartNew();
        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RunElapsedTickerAsync(sw, timerCts.Token);

        try
        {
            // Step 1: Check environment and resolve release metadata
            SetStepRunning(0, Environment.OSVersion.VersionString);
            AppendLog("Checking system environment and target release...");
            var checkResult = _initialCheckResult ?? await _updateService.CheckForUpdatesAsync(ct);
            if (!string.IsNullOrWhiteSpace(checkResult.LatestVersion))
            {
                VersionLabel = $"v{checkResult.LatestVersion}";
            }

            if (checkResult.PreferredAsset is null)
            {
                throw new InvalidOperationException("No compatible Linux package asset (.rpm / .AppImage) was found for this release.");
            }

            AppendLog($"Target package: {checkResult.PreferredAsset.Name} ({checkResult.InstallKind})");
            CompleteStep(0);

            // Step 2: Download official package
            var assetName = checkResult.PreferredAsset.Name;
            SetStepRunning(1, assetName);
            AppendLog($"Downloading {assetName}...");
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = Math.Clamp(p.Percentage, 0, 100);
                    var dlMb = p.BytesDownloaded / (1024.0 * 1024.0);
                    var totMb = p.TotalBytes > 0 ? p.TotalBytes / (1024.0 * 1024.0) : dlMb;
                    ProgressSummaryText = $"{ProgressPercentage:0}% • {dlMb:0.0} MB / ~{totMb:0.0} MB";

                    if (p.MegabytesPerSecond > 0.05 && p.TotalBytes > p.BytesDownloaded)
                    {
                        var remainingSec = (int)Math.Ceiling((p.TotalBytes - p.BytesDownloaded) / (p.MegabytesPerSecond * 1024.0 * 1024.0));
                        var eta = TimeSpan.FromSeconds(Math.Clamp(remainingSec, 0, 3599)).ToString(@"m\:ss");
                        SpeedAndEtaText = $"{p.MegabytesPerSecond:0.0} MB/s • {Loc.Format("InstallerRemainingFmt", eta)}";
                    }
                    else
                    {
                        SpeedAndEtaText = p.MegabytesPerSecond > 0.05 ? $"{p.MegabytesPerSecond:0.0} MB/s" : string.Empty;
                    }
                });
            });

            var downloaded = await _updateService.DownloadUpdatePackageAsync(checkResult, progress, ct);
            ProgressPercentage = 85;
            CompleteStep(1);

            // Step 3: Integrity verification
            SetStepRunning(2, downloaded.Sha256Verified ? "SHA-256 OK" : assetName);
            AppendLog(downloaded.Sha256Verified
                ? $"SHA-256 verified successfully for {assetName}."
                : $"Checksum verification skipped (no SHA256SUMS asset).");
            await Task.Delay(200, ct);
            CompleteStep(2);

            // Step 4: Silent background installation
            SetStepRunning(3, downloaded.LocalFilePath);
            AppendLog("Installing package in background...");

            if (_initialCheckResult is not null)
            {
                // Launched from AboutView to self-update current installation
                var launch = await _updateService.LaunchUpdateInstallerAsync(downloaded, ct);
                AppendLog($"Detached updater helper started: {launch.Message}");
                ProgressPercentage = 100;
                CompleteStep(3);
                SetStepRunning(4, string.Empty);
                CompleteStep(4);
                IsCompleted = true;
                if (launch.ShouldShutdownApp)
                {
                    await Task.Delay(600, CancellationToken.None);
                    RequestShutdownForUpdateCallback?.Invoke();
                    return;
                }
            }
            else
            {
                await InstallPackageSilentlyInBackgroundAsync(downloaded, ct);
                ProgressPercentage = 100;
                CompleteStep(3);

                var installedTargetLabel = ResolveInstalledBinaryDisplayPath();
                SetStepRunning(4, installedTargetLabel);
                await Task.Delay(250, ct);
                CompleteStep(4);
                IsCompleted = true;
            }
        }
        catch (OperationCanceledException)
        {
            MarkActiveStepFailed();
            ErrorMessage = Loc.Text("TransferCancelled");
            IsFailed = true;
            AppendLog("Installation cancelled by user.");
        }
        catch (Exception ex)
        {
            MarkActiveStepFailed();
            ErrorMessage = ex.Message;
            IsFailed = true;
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            timerCts.Cancel();
            sw.Stop();
            IsRunning = false;
            OnPropertyChanged(nameof(HasError));
        }
    }

    private async Task InstallPackageSilentlyInBackgroundAsync(DownloadedUpdatePackage downloaded, CancellationToken ct)
    {
        if (downloaded.LocalFilePath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var target = !string.IsNullOrWhiteSpace(AppImageTargetPathOverride)
                ? AppImageTargetPathOverride
                : Path.Combine(home, ".local", "opt", "custodia", "Custodia.AppImage");
            var appDir = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(appDir);
            var tempTarget = $"{target}.tmp";
            File.Copy(downloaded.LocalFilePath, tempTarget, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tempTarget, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            File.Move(tempTarget, target, overwrite: true);
            if (string.IsNullOrWhiteSpace(AppImageTargetPathOverride))
            {
                LinuxDesktopIntegrationHelper.InstallPostInstallDesktopIntegration(target, enableAutostart: true);
            }
            AppendLog($"Installed AppImage to {target}");
            return;
        }

        // Native RPM background install using pkexec graphical auth dialog (no visible terminal window)
        var escapedPkg = downloaded.LocalFilePath.Replace("'", "'\\''", StringComparison.Ordinal);
        var cmd = $"if command -v dnf >/dev/null 2>&1; then pkexec dnf install -y '{escapedPkg}'; elif command -v zypper >/dev/null 2>&1; then pkexec zypper --non-interactive install -y --allow-unsigned-rpm '{escapedPkg}'; else pkexec rpm -Uvh --replacepkgs '{escapedPkg}'; fi";
        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(cmd);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start background installer process.");
        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                AppendLog(line);
            }
        }

        var err = await proc.StandardError.ReadToEndAsync(ct);
        if (!string.IsNullOrWhiteSpace(err))
        {
            AppendLog(err.Trim());
        }

        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Background package installation exited with code {proc.ExitCode}.");
        }

        LinuxDesktopIntegrationHelper.InstallPostInstallDesktopIntegration(installedAppImagePath: null, enableAutostart: true);
    }

    private async Task RunElapsedTickerAsync(Stopwatch sw, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var secs = (int)sw.Elapsed.TotalSeconds;
                DispatchToUi(() => ElapsedSecondsText = Loc.Format("InstallerElapsedFmt", secs));
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetStepRunning(int index, string detail)
    {
        DispatchToUi(() =>
        {
            if (index < Steps.Count)
            {
                Steps[index].State = InstallerStepState.Running;
                Steps[index].Detail = detail;
            }
        });
    }

    private void CompleteStep(int index)
    {
        DispatchToUi(() =>
        {
            if (index < Steps.Count)
            {
                Steps[index].State = InstallerStepState.Completed;
                Steps[index].Detail = string.Empty;
            }
        });
    }

    private void MarkActiveStepFailed()
    {
        DispatchToUi(() =>
        {
            foreach (var step in Steps)
            {
                if (step.State == InstallerStepState.Running)
                {
                    step.State = InstallerStepState.Failed;
                }
            }
        });
    }

    private void AppendLog(string message)
    {
        _logBuilder.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").AppendLine(message);
        var snapshot = _logBuilder.ToString();
        DispatchToUi(() => LogOutputText = snapshot);
    }

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        if (Avalonia.Application.Current is not null)
        {
            Dispatcher.UIThread.Post(action);
            return;
        }

        action();
    }

    [RelayCommand]
    private void ToggleDetails() => IsDetailsOpen = !IsDetailsOpen;

    [RelayCommand]
    private void CancelInstall()
    {
        if (IsRunning)
        {
            _installCts?.Cancel();
        }
        else
        {
            RequestCloseCallback?.Invoke();
        }
    }

    private static string ResolveInstalledBinaryDisplayPath()
    {
        if (File.Exists("/usr/bin/custodia"))
        {
            return "/usr/bin/custodia";
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appImage = Path.Combine(home, ".local", "opt", "custodia", "Custodia.AppImage");
        if (File.Exists(appImage))
        {
            return appImage;
        }

        var localBin = Path.Combine(home, ".local", "bin", "custodia");
        return File.Exists(localBin) ? localBin : "/usr/bin/custodia";
    }

    [RelayCommand]
    private void LaunchInstalledApp()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localBin = Path.Combine(home, ".local", "bin", "custodia");
            var localAppImage = Path.Combine(home, ".local", "opt", "custodia", "Custodia.AppImage");
            var bin = File.Exists("/usr/bin/custodia")
                ? "/usr/bin/custodia"
                : File.Exists(localBin)
                    ? localBin
                    : File.Exists(localAppImage)
                        ? localAppImage
                        : "custodia";
            Process.Start(new ProcessStartInfo(bin) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
        }

        RequestCloseCallback?.Invoke();
    }

    [RelayCommand]
    private Task OpenProjectPageAsync() => OpenUriAsync("https://github.com/TundraLab/Custodia");

    [RelayCommand]
    private Task OpenGuideAsync() => OpenUriAsync("https://custodia.tundralab.com.br");

    [RelayCommand]
    private Task OpenIssuesAsync() => OpenUriAsync("https://github.com/TundraLab/Custodia/issues");

    private Task OpenUriAsync(string url) =>
        OpenExternalUriCallback is not null ? OpenExternalUriCallback.Invoke(new Uri(url)) : Task.CompletedTask;

    public void Dispose()
    {
        _installCts?.Cancel();
        _installCts?.Dispose();
    }
}
