using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;
using TundraDrive.Desktop.Views;

namespace TundraDrive.Desktop;

public partial class App : Application
{
    public IServiceProvider? Services { get; set; }
    public SingleInstanceGuard? SingleInstance { get; set; }

    private TrayIcon? _trayIcon;
    private bool _isExplicitExit;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            LinuxDesktopIntegrationHelper.InstallDesktopAssets();

            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var settingsService = Services.GetRequiredService<IAppSettingsService>();
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
                    .Select(f => f.Path.LocalPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
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
            mainVm.SettingsVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.AboutVm.OpenExternalUriCallback = OpenExternalUriAsync;
            mainVm.AboutVm.RequestAppShutdownCallback = () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _isExplicitExit = true;
                    desktop.Shutdown();
                });
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

            ConfigureTrayIcon(desktop, mainWindow, mainVm);

            if (SingleInstance is not null)
            {
                SingleInstance.ActivationRequested += (_, _) => ShowOrRestoreWindow(mainWindow, mainVm);
            }

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += async (_, _) =>
            {
                _trayIcon?.Dispose();
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
            };

            memoryCleaner.Start();
            _ = mainVm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
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

        using var iconStream = AssetLoader.Open(new Uri("avares://TundraDrive/Assets/tundradrive-tray.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "TundraDrive",
            Menu = menu,
            IsVisible = true
        };

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
        }

        // Sync tray menu items on demand, language change, or debounced state transitions (never mutating ToolTipText so top bar never blinks).
        var lastMenuSyncUtc = DateTimeOffset.MinValue;
        menu.NeedsUpdate += (_, _) => RefreshTrayMenuTexts();
        mainVm.Loc.LanguageChanged += (_, _) => RefreshTrayMenuTexts();
        mainVm.TraySummaryChanged += (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastMenuSyncUtc).TotalMilliseconds >= 800)
            {
                lastMenuSyncUtc = now;
                RefreshTrayMenuTexts();
            }
        };

        RefreshTrayMenuTexts();
        TrayIcon.SetIcons(this, [_trayIcon]);
    }
}
