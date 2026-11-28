using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Sync;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;
using TundraDrive.Desktop.Views;

namespace TundraDrive.Desktop;

public partial class App : Application
{
    public IServiceProvider? Services { get; set; }
    public SingleInstanceGuard? SingleInstance { get; set; }

    private TrayIcon? _trayIcon;
    private TrayIconAnimator? _trayIconAnimator;
    private EventHandler<string>? _trayLanguageChangedHandler;
    private EventHandler? _traySummaryChangedHandler;
    private bool _isExplicitExit;
    private int _trayRecoveryInProgress;
    private readonly CancellationTokenSource _appLifetimeCts = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            var isInstallerMode = desktop.Args?.Any(a =>
                string.Equals(a, "--installer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase)) == true;

            if (isInstallerMode)
            {
                var loc = Services.GetRequiredService<ILocalizationService>();
                var updater = Services.GetRequiredService<IAppUpdateService>();
                var appSettings = Services.GetRequiredService<IAppSettingsService>();
                loc.SetLanguage(appSettings.Current.Language);
                ThemeAccentService.ApplyTheme(appSettings.Current.UiTheme, appSettings.Current.UiAccent);

                var installerVm = new InstallerViewModel(updater, loc);
                var installerWindow = new InstallerWindow { DataContext = installerVm };
                installerVm.OpenExternalUriCallback = uri =>
                    installerWindow.Launcher.LaunchUriAsync(uri);
                installerVm.RequestCloseCallback = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => installerWindow.Close());
                installerWindow.Closed += (_, _) => installerVm.Dispose();
                desktop.MainWindow = installerWindow;
                base.OnFrameworkInitializationCompleted();
                return;
            }

            LinuxDesktopIntegrationHelper.InstallDesktopAssets();

            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var settingsService = Services.GetRequiredService<IAppSettingsService>();
            ThemeAccentService.ApplyTheme(settingsService.Current.UiTheme, settingsService.Current.UiAccent);
            var memoryCleaner = Services.GetRequiredService<MemoryCleanerService>();
            var mainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            memoryCleaner.ReleaseUiCachesCallback = async forceRelease =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (forceRelease ||
                        !mainWindow.IsVisible ||
                        mainWindow.WindowState == WindowState.Minimized ||
                        mainVm.SelectedNavigationIndex != 0)
                    {
                        mainVm.FileHubVm.ReleaseHeavyMediaResources();
                    }
                });
            };

            mainVm.SyncFoldersVm.PickLocalFolderCallback = async () =>
            {
                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                    new Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("MapLocalFolderButton"),
                        AllowMultiple = false
                    });

                return result.FirstOrDefault()?.Path.LocalPath;
            };

            mainVm.SyncFoldersVm.PickLocalFoldersCallback = async () =>
            {
                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                    new Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("MapMultipleFoldersTitle"),
                        AllowMultiple = true
                    });

                return result
                    .Select(f => f.Path.LocalPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToArray();
            };

            mainVm.AccountsVm.PickCredentialsFileCallback = async () =>
            {
                var result = await mainWindow.StorageProvider.OpenFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("ImportCredentialsButton"),
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new Avalonia.Platform.Storage.FilePickerFileType("JSON")
                            {
                                Patterns = ["*.json"]
                            }
                        ]
                    });

                return result.FirstOrDefault()?.Path.LocalPath;
            };
            mainVm.SettingsVm.PickCredentialsFileCallback = mainVm.AccountsVm.PickCredentialsFileCallback;

            mainVm.FileHubVm.PickLocalFileForUploadCallback = async () =>
            {
                var result = await mainWindow.StorageProvider.OpenFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("UploadFileButton"),
                        AllowMultiple = true
                    });

                return result
                    .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            };

            mainVm.FileHubVm.PickSaveFileCallback = async suggestedName =>
            {
                var file = await mainWindow.StorageProvider.SaveFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = mainVm.Loc.Text("SaveAsDialogTitle"),
                        SuggestedFileName = suggestedName
                    });
                return file?.TryGetLocalPath() ?? file?.Path.LocalPath;
            };

            mainVm.FileHubVm.PickDownloadFolderCallback = async () =>
            {
                var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                    new Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("ChooseDownloadFolderTitle"),
                        AllowMultiple = false
                    });
                return folders.FirstOrDefault()?.TryGetLocalPath() ?? folders.FirstOrDefault()?.Path.LocalPath;
            };

            static bool TryLaunchWithXdgOpen(string target)
            {
                if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(target))
                {
                    return false;
                }

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("xdg-open")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(target);
                    using var proc = System.Diagnostics.Process.Start(psi);
                    return proc is not null;
                }
                catch
                {
                    return false;
                }
            }

            mainVm.FileHubVm.OpenLocalFileCallback = async localPath =>
            {
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    return;
                }

                if (TryLaunchWithXdgOpen(localPath))
                {
                    return;
                }

                var storageFile = await mainWindow.StorageProvider.TryGetFileFromPathAsync(new Uri(localPath));
                if (storageFile is not null)
                {
                    await mainWindow.Launcher.LaunchFileAsync(storageFile);
                }
            };

            mainVm.SettingsVm.PickDownloadDirectoryCallback = async () =>
            {
                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                    new Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = mainVm.Loc.Text("DownloadDirectoryLabel"),
                        AllowMultiple = false
                    });

                return result.FirstOrDefault()?.Path.LocalPath;
            };

            Task OpenExternalUriAsync(Uri uri) =>
                TryLaunchWithXdgOpen(uri.AbsoluteUri)
                    ? Task.CompletedTask
                    : mainWindow.Launcher.LaunchUriAsync(uri);
            mainVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.AccountsVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.FileHubVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.SyncFoldersVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.SyncFoldersVm.RemoteBrowser.OpenExternalUriCallback = OpenExternalUriAsync;
            void RequestCleanShutdown()
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _isExplicitExit = true;
                    desktop.Shutdown();
                });
            }

            mainVm.SettingsVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.AboutVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.AboutVm.RequestAppShutdownCallback = RequestCleanShutdown;
            mainVm.AboutVm.OpenInstallerWindowCallback = checkResult =>
            {
                var updater = Services.GetRequiredService<IAppUpdateService>();
                var installerVm = new InstallerViewModel(updater, mainVm.Loc, checkResult);
                var installerWindow = new InstallerWindow { DataContext = installerVm };
                installerVm.OpenExternalUriCallback = OpenExternalUriAsync;
                installerVm.RequestCloseCallback = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => installerWindow.Close());
                installerVm.RequestShutdownForUpdateCallback = RequestCleanShutdown;
                installerWindow.Closed += (_, _) => installerVm.Dispose();
                return installerWindow.ShowDialog(mainWindow);
            };

            async Task CopyTextAsync(string text)
            {
                if (mainWindow.Clipboard is not null && !string.IsNullOrEmpty(text))
                {
                    await mainWindow.Clipboard.SetTextAsync(text);
                }
            }

            mainVm.AccountsVm.CopyTextCallback = CopyTextAsync;
            mainVm.FileHubVm.CopyTextCallback = CopyTextAsync;
            mainVm.SyncFoldersVm.RemoteBrowser.CopyTextCallback = CopyTextAsync;
            mainVm.TransferQueueVm.CopyTextCallback = CopyTextAsync;
            mainVm.ActivityLogsVm.CopyTextCallback = CopyTextAsync;
            mainVm.AboutVm.CopyTextCallback = CopyTextAsync;
            mainVm.SettingsVm.CopyTextCallback = CopyTextAsync;

            async Task OpenLocalFolderAsync(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var targetDir = Directory.Exists(path)
                    ? path
                    : File.Exists(path)
                        ? Path.GetDirectoryName(path)
                        : null;

                if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                {
                    return;
                }

                if (TryLaunchWithXdgOpen(targetDir))
                {
                    return;
                }

                var folder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(new Uri(targetDir));
                if (folder is not null)
                {
                    await mainWindow.Launcher.LaunchFileAsync(folder);
                }
            }

            mainVm.SyncFoldersVm.OpenLocalFolderCallback = OpenLocalFolderAsync;
            mainVm.TransferQueueVm.OpenLocalFolderCallback = OpenLocalFolderAsync;
            mainVm.ActivityLogsVm.OpenLocalFolderCallback = OpenLocalFolderAsync;
            mainVm.SettingsVm.OpenLocalFolderCallback = OpenLocalFolderAsync;

            mainWindow.Closing += (_, e) =>
            {
                if (_isExplicitExit)
                {
                    return;
                }

                if (settingsService.Current.MinimizeToTrayOnClose)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                    _ = memoryCleaner.CleanNowAsync(forceReleaseMedia: true);
                }
                else
                {
                    _isExplicitExit = true;
                    desktop.Shutdown();
                }
            };

            void OnUiThreadUnhandledException(object sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                if (!IsRecoverableTrayDbusException(e.Exception))
                {
                    return;
                }

                e.Handled = true;
                ScheduleTrayIconRecovery(desktop, mainWindow, mainVm);
            }

            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
            _ = InitializeTrayIconWhenReadyAsync(desktop, mainWindow, mainVm);

            IDisposable? sigTermRegistration = null;
            IDisposable? sigIntRegistration = null;
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    RequestCleanShutdown();
                });
                sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
                {
                    ctx.Cancel = true;
                    RequestCleanShutdown();
                });
            }

            if (SingleInstance is not null)
            {
                SingleInstance.ActivationRequested += (_, _) => ShowOrRestoreWindow(mainWindow, mainVm);
                SingleInstance.ShutdownRequested += (_, _) => RequestCleanShutdown();
            }

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += async (_, _) =>
            {
                _appLifetimeCts.Cancel();
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= OnUiThreadUnhandledException;
                sigTermRegistration?.Dispose();
                sigIntRegistration?.Dispose();
                DisposeCurrentTrayIcon(mainVm);
                SingleInstance?.Dispose();
                memoryCleaner.Dispose();
                Services.GetRequiredService<FilePreviewService>().Dispose();
                var apiServer = Services.GetRequiredService<IEmbeddedApiServer>();
                await apiServer.StopAsync();
                mainVm.Dispose();
                if (Services.GetRequiredService<IDriveSyncService>() is IDisposable disposableSync)
                {
                    disposableSync.Dispose();
                }
                if (Services.GetRequiredService<IDesktopNotificationService>() is IDisposable disposableNotifications)
                {
                    disposableNotifications.Dispose();
                }
                _appLifetimeCts.Dispose();
            };

            memoryCleaner.Start();
            _ = mainVm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static bool IsRecoverableTrayDbusException(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        if (ex is Tmds.DBus.Protocol.DBusException)
        {
            return true;
        }

        var text = ex.ToString();
        return text.Contains("DBusTrayIconImpl", StringComparison.Ordinal) ||
               text.Contains("StatusNotifier", StringComparison.Ordinal);
    }

    private async Task InitializeTrayIconWhenReadyAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainWindowViewModel mainVm)
    {
        try
        {
            await LinuxDesktopIntegrationHelper.WaitForStatusNotifierWatcherReadyAsync(_appLifetimeCts.Token);
            if (_isExplicitExit || _appLifetimeCts.IsCancellationRequested)
            {
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isExplicitExit || _appLifetimeCts.IsCancellationRequested)
                {
                    return;
                }

                ConfigureTrayIcon(desktop, mainWindow, mainVm);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ScheduleTrayIconRecovery(desktop, mainWindow, mainVm);
        }
    }

    private void ScheduleTrayIconRecovery(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainWindowViewModel mainVm)
    {
        if (_isExplicitExit || Interlocked.CompareExchange(ref _trayRecoveryInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => DisposeCurrentTrayIcon(mainVm));
                await Task.Delay(4000, _appLifetimeCts.Token);
                await LinuxDesktopIntegrationHelper.WaitForStatusNotifierWatcherReadyAsync(_appLifetimeCts.Token);

                if (_isExplicitExit || _appLifetimeCts.IsCancellationRequested)
                {
                    return;
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_isExplicitExit || _appLifetimeCts.IsCancellationRequested)
                    {
                        return;
                    }

                    ConfigureTrayIcon(desktop, mainWindow, mainVm);
                });
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _trayRecoveryInProgress, 0);
            }
        });
    }

    private void DisposeCurrentTrayIcon(MainWindowViewModel mainVm)
    {
        if (_trayLanguageChangedHandler is not null)
        {
            mainVm.Loc.LanguageChanged -= _trayLanguageChangedHandler;
            _trayLanguageChangedHandler = null;
        }

        if (_traySummaryChangedHandler is not null)
        {
            mainVm.TraySummaryChanged -= _traySummaryChangedHandler;
            _traySummaryChangedHandler = null;
        }

        if (_trayIconAnimator is not null)
        {
            _trayIconAnimator.Dispose();
            _trayIconAnimator = null;
        }

        if (_trayIcon is not null)
        {
            try
            {
                TrayIcon.SetIcons(this, []);
            }
            catch
            {
            }

            try
            {
                _trayIcon.Dispose();
            }
            catch
            {
            }

            _trayIcon = null;
        }
    }

    private static void ShowOrRestoreWindow(
        MainWindow mainWindow,
        MainWindowViewModel mainVm,
        int? navigateToTab = null)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (navigateToTab.HasValue)
            {
                mainVm.SelectedNavigationIndex = navigateToTab.Value;
            }

            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
        });
    }

    private void ConfigureTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainWindowViewModel mainVm)
    {
        DisposeCurrentTrayIcon(mainVm);

        void ShowWindow(int? navigateToTab = null) =>
            ShowOrRestoreWindow(mainWindow, mainVm, navigateToTab);

        var accountInfoItem = new NativeMenuItem(mainVm.GetTrayAccountLine()) { IsEnabled = false };
        var statusInfoItem = new NativeMenuItem(mainVm.GetTrayStatusLine()) { IsEnabled = false };
        var storageInfoItem = new NativeMenuItem(mainVm.GetTrayStorageLine()) { IsEnabled = false };
        var foldersTransfersItem = new NativeMenuItem(mainVm.GetTrayFoldersTransfersLine()) { IsEnabled = false };

        var openItem = new NativeMenuItem(mainVm.Loc.Text("TrayOpen"));
        openItem.Click += (_, _) => ShowWindow();

        var openHubItem = new NativeMenuItem(mainVm.Loc.Text("TrayOpenFileHub"));
        openHubItem.Click += (_, _) => ShowWindow(0);

        var syncItem = new NativeMenuItem(mainVm.Loc.Text("TraySyncNow"));
        syncItem.Click += async (_, _) => await mainVm.TriggerSyncFromTrayAsync();

        var pauseResumeItem = new NativeMenuItem(
            mainVm.HasAnyActiveSyncFolder() ? mainVm.Loc.Text("TrayPauseAll") : mainVm.Loc.Text("TrayResumeAll"));
        pauseResumeItem.Click += async (_, _) => await mainVm.TogglePauseAllFromTrayAsync();

        var openDownloadsItem = new NativeMenuItem(mainVm.Loc.Text("TrayOpenDownloads"));
        openDownloadsItem.Click += async (_, _) =>
        {
            var dir = mainVm.GetDownloadDirectory();
            Directory.CreateDirectory(dir);
            var folder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir));
            if (folder is not null)
            {
                await mainWindow.Launcher.LaunchFileAsync(folder);
            }
        };

        var settingsItem = new NativeMenuItem(mainVm.Loc.Text("TraySettings"));
        settingsItem.Click += (_, _) => ShowWindow(4);

        var exitItem = new NativeMenuItem(mainVm.Loc.Text("TrayExit"));
        exitItem.Click += (_, _) =>
        {
            _isExplicitExit = true;
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(accountInfoItem);
        menu.Items.Add(statusInfoItem);
        menu.Items.Add(storageInfoItem);
        menu.Items.Add(foldersTransfersItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(openHubItem);
        menu.Items.Add(syncItem);
        menu.Items.Add(pauseResumeItem);
        menu.Items.Add(openDownloadsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://Custodia/Assets/custodia-tray.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = $"Custódia v{AppVersionInfo.CurrentVersion}",
            Menu = menu,
            IsVisible = true
        };
        _trayIconAnimator = new TrayIconAnimator(_trayIcon);

        _trayIcon.Clicked += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (mainWindow.IsVisible &&
                    mainWindow.WindowState != WindowState.Minimized &&
                    mainWindow.IsActive)
                {
                    mainWindow.Hide();
                }
                else
                {
                    ShowWindow();
                }
            });
        };

        static void SetHeaderIfChanged(NativeMenuItem item, string value)
        {
            if (!string.Equals(item.Header, value, StringComparison.Ordinal))
            {
                item.Header = value;
            }
        }

        void RefreshTrayMenuTexts()
        {
            SetHeaderIfChanged(accountInfoItem, mainVm.GetTrayAccountLine());
            SetHeaderIfChanged(statusInfoItem, mainVm.GetTrayStatusLine());
            SetHeaderIfChanged(storageInfoItem, mainVm.GetTrayStorageLine());
            SetHeaderIfChanged(foldersTransfersItem, mainVm.GetTrayFoldersTransfersLine());
            SetHeaderIfChanged(openItem, mainVm.Loc.Text("TrayOpen"));
            SetHeaderIfChanged(openHubItem, mainVm.Loc.Text("TrayOpenFileHub"));
            SetHeaderIfChanged(syncItem, mainVm.Loc.Text("TraySyncNow"));
            SetHeaderIfChanged(pauseResumeItem, mainVm.HasAnyActiveSyncFolder()
                ? mainVm.Loc.Text("TrayPauseAll")
                : mainVm.Loc.Text("TrayResumeAll"));
            SetHeaderIfChanged(openDownloadsItem, mainVm.Loc.Text("TrayOpenDownloads"));
            SetHeaderIfChanged(settingsItem, mainVm.Loc.Text("TraySettings"));
            SetHeaderIfChanged(exitItem, mainVm.Loc.Text("TrayExit"));
            _trayIconAnimator?.SetState(mainVm.GetTrayVisualState());
        }

        // Coalesce rapid menu text refreshes on a trailing timer while flushing state transitions immediately so tray status never freezes.
        var lastMenuSyncUtc = DateTimeOffset.MinValue;
        var lastVisualState = mainVm.GetTrayVisualState();
        var pendingMenuRefreshTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        pendingMenuRefreshTimer.Tick += (_, _) =>
        {
            pendingMenuRefreshTimer.Stop();
            lastMenuSyncUtc = DateTimeOffset.UtcNow;
            lastVisualState = mainVm.GetTrayVisualState();
            RefreshTrayMenuTexts();
        };

        menu.NeedsUpdate += (_, _) =>
        {
            pendingMenuRefreshTimer.Stop();
            lastMenuSyncUtc = DateTimeOffset.UtcNow;
            lastVisualState = mainVm.GetTrayVisualState();
            RefreshTrayMenuTexts();
        };
        _trayLanguageChangedHandler = (_, _) =>
        {
            pendingMenuRefreshTimer.Stop();
            lastMenuSyncUtc = DateTimeOffset.UtcNow;
            lastVisualState = mainVm.GetTrayVisualState();
            RefreshTrayMenuTexts();
        };
        _traySummaryChangedHandler = (_, _) =>
        {
            var nextState = mainVm.GetTrayVisualState();
            _trayIconAnimator?.SetState(nextState);
            var now = DateTimeOffset.UtcNow;
            if (nextState != lastVisualState || (now - lastMenuSyncUtc).TotalMilliseconds >= 250)
            {
                pendingMenuRefreshTimer.Stop();
                lastMenuSyncUtc = now;
                lastVisualState = nextState;
                RefreshTrayMenuTexts();
            }
            else if (!pendingMenuRefreshTimer.IsEnabled)
            {
                pendingMenuRefreshTimer.Start();
            }
        };
        mainVm.Loc.LanguageChanged += _trayLanguageChangedHandler;
        mainVm.TraySummaryChanged += _traySummaryChangedHandler;

        RefreshTrayMenuTexts();
        TrayIcon.SetIcons(this, [_trayIcon]);
    }
}
