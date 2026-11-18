using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class InstallerWindow : Window
{
    public InstallerWindow()
    {
        InitializeComponent();
    }

    public InstallerWindow(InstallerViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseWindowCallback = Close;
        vm.PickPackageFileCallback = async ext =>
        {
            var isRpm = string.Equals(ext, "rpm", StringComparison.OrdinalIgnoreCase);
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = isRpm ? "Selecionar pacote .rpm do TundraDrive" : "Selecionar pacote .AppImage do TundraDrive",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(isRpm ? "Pacote RPM (*.rpm)" : "Pacote AppImage (*.AppImage)")
                    {
                        Patterns = isRpm ? ["*.rpm"] : ["*.AppImage", "*.appimage"]
                    }
                ]
            });

            return files.FirstOrDefault()?.Path.LocalPath;
        };
    }
}
