using Avalonia.Controls;
using Avalonia.Input;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class RemoteBrowserView : UserControl
{
    public RemoteBrowserView()
    {
        InitializeComponent();
        KeyDown += OnViewKeyDown;
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel vm || vm.SelectedNode is null)
        {
            return;
        }

        if (vm.SelectedNode.IsFolder)
        {
            vm.OpenNodeCommand.Execute(vm.SelectedNode);
        }
        else if (vm.SelectedNode.DownloadCommand is not null)
        {
            vm.SelectedNode.DownloadCommand.Execute(null);
        }
    }

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is RemoteBrowserViewModel vm && vm.SearchCommand.CanExecute(null))
        {
            await vm.SearchCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private async void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel vm)
        {
            return;
        }

        if ((e.Key == Key.Back || (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))) &&
            e.Source is not TextBox &&
            vm.CanNavigateBack)
        {
            await vm.NavigateBackAsync();
            e.Handled = true;
        }
    }
}
