using Avalonia.Controls;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
    }

    private void OnStorageBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is AccountsViewModel vm && e.NewSize.Width > 0)
        {
            vm.SetStorageTrackWidth(e.NewSize.Width);
        }
    }
}
