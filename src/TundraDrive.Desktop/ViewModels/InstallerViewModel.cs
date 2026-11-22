using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class InstallerViewModel : ViewModelBase
{
    private static readonly string UserHome =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly IBrush ActiveCardBorder = Brush.Parse("#3B82F6");
    private static readonly IBrush InactiveCardBorder = Brush.Parse("#252D3D");

    public string AppVersion => "v2.6.0";
    public string BrandTitle => "TundraDrive by TundraLab";
    public string CompanyName => "TundraLab";
    public string DeveloperName => "Gustavo Henrique Novaes Gomes (@GustaHerin)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRpmSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppImageSelected))]
    [NotifyPropertyChangedFor(nameof(AppImageCardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(RpmCardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedPackagePath))]
    [NotifyPropertyChangedFor(nameof(TargetInstallDestination))]
    private string _selectedMode = "AppImage";

    public bool IsRpmSelected => string.Equals(SelectedMode, "RPM", StringComparison.OrdinalIgnoreCase);
    public bool IsAppImageSelected => !IsRpmSelected;
    public IBrush AppImageCardBorderBrush => IsAppImageSelected ? ActiveCardBorder : InactiveCardBorder;
    public IBrush RpmCardBorderBrush => IsRpmSelected ? ActiveCardBorder : InactiveCardBorder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackagePath))]
    private string _rpmPackagePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackagePath))]
    private string _appImagePackagePath = string.Empty;

    public string SelectedPackagePath => IsRpmSelected ? RpmPackagePath : AppImagePackagePath;

    public string TargetInstallDestination => IsRpmSelected
        ? "/opt/tundradrive  +  /usr/bin/tundradrive  (Sistema / Fedora RPM)"
        : Path.Combine(UserHome, ".local", "opt", "tundradrive", "TundraDrive.AppImage") + "  (Usuário Local)";

    [ObservableProperty]
    private bool _createMenuShortcut = true;

    [ObservableProperty]
    private bool _enableAutostart = true;

    [ObservableProperty]
    private bool _createDesktopShortcut;

    [ObservableProperty]
    private bool _installOptionalMediaPackages;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusHeadline = "Pronto para instalar o TundraDrive by TundraLab.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isInstallCompleted;

    [ObservableProperty]
    private string _installedDetectionSummary = string.Empty;

    public ObservableCollection<string> LogLines { get; } = [];

    public Func<string, Task<string?>>? PickPackageFileCallback { get; set; }
    public Action? CloseWindowCallback { get; set; }

    public InstallerViewModel()
    {
        DetectPackagesAndExistingInstall();
    }

    [RelayCommand]
    private void SelectAppImageMode()
    {
        if (IsBusy)
        {
            return;
        }

        SelectedMode = "AppImage";
        StatusHeadline = "Modo selecionado: AppImage integrado ao sistema (sem exigir root).";
    }

    [RelayCommand]
    private void SelectRpmMode()
    {
        if (IsBusy)
        {
            return;
        }

        SelectedMode = "RPM";
        StatusHeadline = "Modo selecionado: Instalador nativo .rpm (Fedora / RHEL / openSUSE).";
    }

    [RelayCommand]
    private async Task BrowsePackageAsync()
    {
        if (IsBusy || PickPackageFileCallback is null)
        {
            return;
        }

        var filter = IsRpmSelected ? "rpm" : "AppImage";
        var picked = await PickPackageFileCallback(filter);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        if (IsRpmSelected)
        {
            RpmPackagePath = picked;
        }
        else
        {
            AppImagePackagePath = picked;
        }

        AppendLog($"Pacote selecionado manualmente: {picked}");
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsInstallCompleted = false;
        ProgressValue = 5;
        LogLines.Clear();
        AppendLog($"Iniciando Instalador Oficial {BrandTitle} ({AppVersion})...");

        try
        {
            if (IsRpmSelected)
            {
                await InstallRpmPackageAsync();
            }
            else
            {
                await InstallAppImagePackageAsync();
            }

            ProgressValue = 100;
            IsInstallCompleted = true;
            StatusHeadline = "Instalação concluída com sucesso! O TundraDrive está pronto para uso.";
            AppendLog("Instalação finalizada com sucesso.");
            RefreshExistingInstallSummary();
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Falha na instalação: {ex.Message}";
            AppendLog($"ERRO: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ProgressValue = 10;
        LogLines.Clear();
        AppendLog("Iniciando remoção da instalação do TundraDrive...");

        try
        {
            await Task.Run(() =>
            {
                var localOptDir = Path.Combine(UserHome, ".local", "opt", "tundradrive");
                var localBin = Path.Combine(UserHome, ".local", "bin", "tundradrive");
                var localDesktop = Path.Combine(UserHome, ".local", "share", "applications", "tundradrive.desktop");
                var autostartDesktop = Path.Combine(UserHome, ".config", "autostart", "tundradrive.desktop");

                if (Directory.Exists(localOptDir))
                {
                    Directory.Delete(localOptDir, recursive: true);
                    AppendLog($"Removido diretório local: {localOptDir}");
                }

                TryDeleteFile(localBin);
                TryDeleteFile(localDesktop);
                TryDeleteFile(autostartDesktop);
                RemoveDesktopShortcuts();

                if (IsRpmPackageInstalled())
                {
                    AppendLog("Pacote RPM detectado no sistema. Solicitando remoção via Polkit (pkexec)...");
                    var (code, output) = RunProcess("pkexec", ["rpm", "-e", "tundradrive"], timeoutMs: 120000);
                    if (code == 0)
                    {
                        AppendLog("Pacote RPM tundradrive removido do sistema.");
                    }
                    else
                    {
                        AppendLog($"Aviso ao remover RPM (código {code}): {output}");
                    }
                }

                RefreshSystemDesktopCaches();
            });

            ProgressValue = 100;
            IsInstallCompleted = false;
            StatusHeadline = "TundraDrive desinstalado (seus dados em ~/.config/tundradrive foram preservados).";
            AppendLog("Desinstalação concluída.");
            RefreshExistingInstallSummary();
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Erro ao desinstalar: {ex.Message}";
            AppendLog($"ERRO: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LaunchInstalledApp()
    {
        try
        {
            var localAppImage = Path.Combine(UserHome, ".local", "opt", "tundradrive", "TundraDrive.AppImage");
            var localBinary = Path.Combine(UserHome, ".local", "opt", "tundradrive", "TundraDrive");
            var systemBinary = "/usr/bin/tundradrive";
            var optBinary = "/opt/tundradrive/TundraDrive";

            string launchTarget;
            if (!IsRpmSelected && File.Exists(localAppImage))
            {
                launchTarget = localAppImage;
            }
            else if (File.Exists(systemBinary))
            {
                launchTarget = systemBinary;
            }
            else if (File.Exists(optBinary))
            {
                launchTarget = optBinary;
            }
            else if (File.Exists(localAppImage))
            {
                launchTarget = localAppImage;
            }
            else if (File.Exists(localBinary))
            {
                launchTarget = localBinary;
            }
            else
            {
                launchTarget = Environment.ProcessPath ?? "tundradrive";
            }

            var psi = new ProcessStartInfo(launchTarget)
            {
                UseShellExecute = false,
                CreateNoWindow = false
            };
            psi.Environment.Remove("TUNDRA_INSTALLER_MODE");
            Process.Start(psi);

            CloseWindowCallback?.Invoke();
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Não foi possível iniciar o aplicativo: {ex.Message}";
            AppendLog($"ERRO ao abrir TundraDrive: {ex.Message}");
        }
    }

    private async Task InstallAppImagePackageAsync()
    {
        var targetDir = Path.Combine(UserHome, ".local", "opt", "tundradrive");
        var targetAppImage = Path.Combine(targetDir, "TundraDrive.AppImage");
        var localBinDir = Path.Combine(UserHome, ".local", "bin");
        var localWrapper = Path.Combine(localBinDir, "tundradrive");

        StatusHeadline = "Instalando pacote AppImage e integrando ao ambiente desktop...";
        ProgressValue = 20;

        await Task.Run(() =>
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(localBinDir);

            string installedExecPath;

            if (!string.IsNullOrWhiteSpace(AppImagePackagePath) && File.Exists(AppImagePackagePath))
            {
                AppendLog($"Copiando AppImage de: {AppImagePackagePath}");
                if (!string.Equals(Path.GetFullPath(AppImagePackagePath), Path.GetFullPath(targetAppImage), StringComparison.Ordinal))
                {
                    File.Copy(AppImagePackagePath, targetAppImage, overwrite: true);
                }

                RunProcess("chmod", ["755", targetAppImage]);
                installedExecPath = targetAppImage;
                AppendLog($"AppImage instalado em: {targetAppImage}");
            }
            else
            {
                // Fallback: copy current self-contained directory if no external .AppImage file is present
                var appBase = AppContext.BaseDirectory;
                AppendLog($"Pacote .AppImage externo não informado; instalando binários autossuficientes de {appBase}...");
                foreach (var file in Directory.GetFiles(appBase, "*", SearchOption.TopDirectoryOnly))
                {
                    var dest = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }

                var binPath = Path.Combine(targetDir, "TundraDrive");
                if (File.Exists(binPath))
                {
                    RunProcess("chmod", ["755", binPath]);
                }

                installedExecPath = binPath;
            }

            ProgressValue = 50;

            var wrapperScript = $"""
                #!/usr/bin/env bash
                exec "{installedExecPath}" "$@"
                """;
            File.WriteAllText(localWrapper, wrapperScript);
            RunProcess("chmod", ["755", localWrapper]);
            AppendLog($"Comando de terminal configurado em: {localWrapper}");

            ProgressValue = 70;
            InstallUserIconsAndShortcuts(installedExecPath);

            ProgressValue = 90;
            RefreshSystemDesktopCaches();
        });
    }

    private async Task InstallRpmPackageAsync()
    {
        if (string.IsNullOrWhiteSpace(RpmPackagePath) || !File.Exists(RpmPackagePath))
        {
            throw new FileNotFoundException(
                "Pacote .rpm não encontrado. Gere-o com ./packaging/linux/build-rpm.sh ou selecione o arquivo .rpm.");
        }

        StatusHeadline = "Solicitando autorização do sistema (Polkit) para instalar o pacote .rpm...";
        ProgressValue = 25;
        AppendLog($"Instalando pacote RPM: {RpmPackagePath}");

        await Task.Run(() =>
        {
            var args = new List<string>();
            string privilegedCmd;

            if (CommandExists("pkexec"))
            {
                privilegedCmd = "pkexec";
                if (CommandExists("dnf"))
                {
                    args.AddRange(["dnf", "install", "-y", RpmPackagePath]);
                    if (InstallOptionalMediaPackages)
                    {
                        args.AddRange(["poppler-utils", "ghostscript", "ffmpeg", "libreoffice", "ImageMagick"]);
                    }
                }
                else
                {
                    args.AddRange(["rpm", "-Uvh", "--replacepkgs", RpmPackagePath]);
                }
            }
            else
            {
                privilegedCmd = "rpm";
                args.AddRange(["-Uvh", "--replacepkgs", RpmPackagePath]);
            }

            AppendLog($"Executando: {privilegedCmd} {string.Join(' ', args)}");
            var (exitCode, output) = RunProcess(privilegedCmd, args, timeoutMs: 300000);
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(12))
                {
                    AppendLog(line.Trim());
                }
            }

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"O instalador RPM retornou código {exitCode}. Verifique a autenticação Polkit ou dependências.");
            }

            ProgressValue = 75;
            AppendLog("Pacote .rpm instalado com sucesso em /opt/tundradrive e /usr/bin/tundradrive.");

            InstallUserIconsAndShortcuts("/usr/bin/tundradrive");
            ProgressValue = 90;
            RefreshSystemDesktopCaches();
        });
    }

    private void InstallUserIconsAndShortcuts(string execTarget)
    {
        var pngIconPath = Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "256x256", "apps", "tundradrive.png");
        var svgIconPath = Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "scalable", "apps", "tundradrive.svg");

        CopyAssetToDisk("avares://TundraDrive/Assets/tundradrive-icon.png", pngIconPath);
        CopyAssetToDisk("avares://TundraDrive/Assets/tundradrive-icon.svg", svgIconPath);
        AppendLog("Ícones instalados no tema hicolor do usuário.");

        var desktopEntryContent = $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=TundraDrive
            GenericName=Google Drive Client
            Comment=TundraDrive by TundraLab - Cliente do Google Drive para Linux
            Icon={pngIconPath}
            Exec="{execTarget}"
            Terminal=false
            Categories=Network;FileTransfer;Utility;
            StartupWMClass=TundraDrive
            X-TundraLab-Vendor=TundraLab
            """;

        if (CreateMenuShortcut)
        {
            var menuDesktopPath = Path.Combine(UserHome, ".local", "share", "applications", "tundradrive.desktop");
            Directory.CreateDirectory(Path.GetDirectoryName(menuDesktopPath)!);
            File.WriteAllText(menuDesktopPath, desktopEntryContent);
            RunProcess("chmod", ["755", menuDesktopPath]);
            AppendLog($"Atalho adicionado ao menu de aplicativos: {menuDesktopPath}");
        }

        var autostartPath = Path.Combine(UserHome, ".config", "autostart", "tundradrive.desktop");
        if (EnableAutostart)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(autostartPath)!);
            File.WriteAllText(autostartPath, desktopEntryContent);
            AppendLog($"Inicialização automática (XDG Autostart) configurada: {autostartPath}");
        }
        else
        {
            TryDeleteFile(autostartPath);
        }

        if (CreateDesktopShortcut)
        {
            var desktopDir = ResolveUserDesktopDirectory();
            if (!string.IsNullOrWhiteSpace(desktopDir) && Directory.Exists(desktopDir))
            {
                var shortcutPath = Path.Combine(desktopDir, "tundradrive.desktop");
                File.WriteAllText(shortcutPath, desktopEntryContent);
                RunProcess("chmod", ["755", shortcutPath]);
                if (CommandExists("gio"))
                {
                    RunProcess("gio", ["set", shortcutPath, "metadata::trusted", "true"]);
                }

                AppendLog($"Atalho criado na Área de Trabalho: {shortcutPath}");
            }
        }
    }

    private void DetectPackagesAndExistingInstall()
    {
        var searchDirs = new List<string>();

        var appImageEnv = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImageEnv) && File.Exists(appImageEnv))
        {
            AppImagePackagePath = appImageEnv;
            var envDir = Path.GetDirectoryName(appImageEnv);
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                searchDirs.Add(envDir);
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        searchDirs.Add(cwd);
        searchDirs.Add(Path.Combine(cwd, "dist"));

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            searchDirs.Add(baseDir);
            var parent = Directory.GetParent(baseDir)?.FullName;
            for (var i = 0; i < 5 && !string.IsNullOrWhiteSpace(parent); i++)
            {
                searchDirs.Add(parent);
                searchDirs.Add(Path.Combine(parent, "dist"));
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        foreach (var dir in searchDirs.Where(Directory.Exists).Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(AppImagePackagePath))
            {
                var foundAppImage = Directory.GetFiles(dir, "TundraDrive-*.AppImage")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(foundAppImage))
                {
                    AppImagePackagePath = foundAppImage;
                }
            }

            if (string.IsNullOrWhiteSpace(RpmPackagePath))
            {
                var foundRpm = Directory.GetFiles(dir, "TundraDrive-*.rpm")
                    .Concat(Directory.GetFiles(dir, "tundradrive-*.rpm"))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(foundRpm))
                {
                    RpmPackagePath = foundRpm;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(RpmPackagePath) && string.IsNullOrWhiteSpace(AppImagePackagePath))
        {
            SelectedMode = "RPM";
        }

        AppendLog($"Instalador Oficial {BrandTitle} pronto.");
        if (!string.IsNullOrWhiteSpace(AppImagePackagePath))
        {
            AppendLog($"AppImage detectado: {AppImagePackagePath}");
        }

        if (!string.IsNullOrWhiteSpace(RpmPackagePath))
        {
            AppendLog($"Pacote RPM detectado: {RpmPackagePath}");
        }

        RefreshExistingInstallSummary();
    }

    private void RefreshExistingInstallSummary()
    {
        var localAppImage = Path.Combine(UserHome, ".local", "opt", "tundradrive", "TundraDrive.AppImage");
        var rpmInstalled = IsRpmPackageInstalled();
        var localInstalled = File.Exists(localAppImage);

        if (rpmInstalled && localInstalled)
        {
            InstalledDetectionSummary = "Status atual: Instalado via RPM (/opt/tundradrive) e AppImage local.";
        }
        else if (rpmInstalled)
        {
            InstalledDetectionSummary = "Status atual: Pacote RPM instalado no sistema (/opt/tundradrive).";
        }
        else if (localInstalled)
        {
            InstalledDetectionSummary = "Status atual: AppImage instalado no perfil do usuário (~/.local/opt/tundradrive).";
        }
        else
        {
            InstalledDetectionSummary = "Status atual: Nenhuma instalação fixa detectada neste computador.";
        }
    }

    private static bool IsRpmPackageInstalled()
    {
        if (!CommandExists("rpm"))
        {
            return false;
        }

        var (code, _) = RunProcess("rpm", ["-q", "tundradrive"], timeoutMs: 4000);
        return code == 0;
    }

    private void RefreshSystemDesktopCaches()
    {
        var appsDir = Path.Combine(UserHome, ".local", "share", "applications");
        if (Directory.Exists(appsDir) && CommandExists("update-desktop-database"))
        {
            RunProcess("update-desktop-database", [appsDir]);
        }

        var hicolorDir = Path.Combine(UserHome, ".local", "share", "icons", "hicolor");
        if (Directory.Exists(hicolorDir) && CommandExists("gtk-update-icon-cache"))
        {
            RunProcess("gtk-update-icon-cache", ["-f", "-t", hicolorDir]);
        }

        if (CommandExists("kbuildsycoca6"))
        {
            RunProcess("kbuildsycoca6", ["--noincremental"]);
        }
        else if (CommandExists("kbuildsycoca5"))
        {
            RunProcess("kbuildsycoca5", ["--noincremental"]);
        }

        AppendLog("Caches de ícones e atalhos atualizados (GNOME / COSMIC / KDE Plasma).");
    }

    private static string? ResolveUserDesktopDirectory()
    {
        var ptBr = Path.Combine(UserHome, "Área de trabalho");
        if (Directory.Exists(ptBr))
        {
            return ptBr;
        }

        var en = Path.Combine(UserHome, "Desktop");
        return Directory.Exists(en) ? en : null;
    }

    private static void RemoveDesktopShortcuts()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(UserHome, "Área de trabalho", "tundradrive.desktop"),
                     Path.Combine(UserHome, "Desktop", "tundradrive.desktop")
                 })
        {
            TryDeleteFile(candidate);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static void CopyAssetToDisk(string avaresUri, string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var src = AssetLoader.Open(new Uri(avaresUri));
        using var dst = File.Create(destinationPath);
        src.CopyTo(dst);
    }

    private static bool CommandExists(string command)
    {
        var (code, _) = RunProcess("which", [command], timeoutMs: 2500);
        return code == 0;
    }

    private static (int ExitCode, string Output) RunProcess(
        string fileName,
        IReadOnlyList<string> args,
        int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return (-1, string.Empty);
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(timeoutMs);
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}".Trim();
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Dispatcher.UIThread.CheckAccess())
        {
            LogLines.Add(line);
        }
        else
        {
            Dispatcher.UIThread.Post(() => LogLines.Add(line));
        }
    }
}
