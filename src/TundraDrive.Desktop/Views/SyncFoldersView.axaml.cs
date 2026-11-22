using Avalonia.Controls;
using Avalonia.Input;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class SyncFoldersView : UserControl
{
    public SyncFoldersView()
    {
        InitializeComponent();
    }

    private async void OnFolderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SyncFoldersViewModel vm && vm.SelectedFolder is not null)
        {
            await vm.OpenLocalFolderAsync(vm.SelectedFolder.LocalPath);
        }
    }

    private void OnStorageBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is SyncFoldersViewModel vm && e.NewSize.Width > 0)
        {
            vm.SetStorageTrackWidth(e.NewSize.Width);
        }
    }
}
