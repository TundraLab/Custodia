using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLinuxProcessIdentity("TundraDrive");

        var services = ConfigureServices();

        var connectionFactory = services.GetRequiredService<SqliteConnectionFactory>();
        DatabaseInitializer.InitializeAsync(connectionFactory).GetAwaiter().GetResult();

        BuildAvaloniaApp(services)
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null);

    public static AppBuilder BuildAvaloniaApp(IServiceProvider? services) =>
        AppBuilder.Configure(() => new App { Services = services })
            .UsePlatformDetect()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 24 * 1024 * 1024
            })
            .With(new X11PlatformOptions
            {
                WmClass = "TundraDrive",
                UseDBusMenu = true,
                UseDBusFilePicker = true
            })
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureLinuxProcessIdentity(string processName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            // PR_SET_NAME updates /proc/<pid>/comm so Linux system monitors display "TundraDrive".
            const int prSetName = 15;
            prctl(prSetName, processName, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Non-fatal if libc cannot be invoked.
        }
    }

    [DllImport("libc.so.6", EntryPoint = "prctl", CharSet = CharSet.Ansi)]
    private static extern int prctl(int option, string arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IAutostartManager, LinuxAutostartManager>();
        services.AddSingleton<IAuthCredentialManager, AuthCredentialManager>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IDesktopNotificationService, LinuxNotificationService>();
        services.AddSingleton<FilePreviewService>();
        services.AddSingleton<MemoryCleanerService>();

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ISyncStateRepository, SqliteSyncStateRepository>();

        services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
        services.AddSingleton<LocalFolderWatcherService>();
        services.AddSingleton<IDriveSyncService, DriveSyncService>();

        services.AddSingleton<IEmbeddedApiServer, EmbeddedApiServer>();

        services.AddSingleton<AccountsViewModel>();
        services.AddSingleton<FileHubViewModel>();
        services.AddSingleton<RemoteBrowserViewModel>();
        services.AddSingleton<SyncFoldersViewModel>();
        services.AddSingleton<TransferQueueViewModel>();
        services.AddSingleton<ActivityLogsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
