using Avalonia.Controls;
using Avalonia.Input;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class ActivityLogsView : UserControl
{
    public ActivityLogsView()
    {
        InitializeComponent();
    }

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ActivityLogsViewModel vm)
        {
            await vm.RefreshLogsAsync();
            e.Handled = true;
        }
    }
}
