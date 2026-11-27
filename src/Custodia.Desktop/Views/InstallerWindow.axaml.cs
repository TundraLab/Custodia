using Avalonia.Controls;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class InstallerWindow : Window
{
    public InstallerWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is InstallerViewModel vm)
        {
            _ = vm.RunInstallationAsync();
        }
    }
}
