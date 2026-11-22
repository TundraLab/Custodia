using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class FileHubView : UserControl
{
    public FileHubView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnFilesDropped);
        KeyDown += OnViewKeyDown;
    }

    private ListBox? GetActiveFilesListBox() =>
        DataContext is FileHubViewModel { IsGridLayout: true }
            ? GridFilesListBox
            : ListFilesListBox;

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox ||
            !e.GetCurrentPoint(listBox).Properties.IsRightButtonPressed ||
            e.Source is not Visual sourceVisual)
        {
            return;
        }

        var container = sourceVisual.FindAncestorOfType<ListBoxItem>();
        if (container?.DataContext is FileHubItemViewModel item && !container.IsSelected)
        {
            listBox.SelectedItem = item;
        }
    }

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FileHubViewModel vm ||
            sender is not ListBox listBox ||
            !listBox.IsVisible ||
            listBox.SelectedItems is null)
        {
            return;
        }

        vm.UpdateSelectedItems(listBox.SelectedItems.OfType<FileHubItemViewModel>());
    }

    private void OnItemCheckBoxClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not FileHubItemViewModel item)
        {
            return;
        }

        var listBox = checkBox.FindAncestorOfType<ListBox>();
        if (listBox?.SelectedItems is null)
        {
            return;
        }

        if (listBox.SelectedItems.Contains(item))
        {
            listBox.SelectedItems.Remove(item);
        }
        else
        {
            listBox.SelectedItems.Add(item);
        }

        e.Handled = true;
    }

    private void OnSelectAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        GetActiveFilesListBox()?.SelectAll();
    }

    private void OnClearSelectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = GetActiveFilesListBox();
        listBox?.SelectedItems?.Clear();
        if (listBox is not null)
        {
            listBox.SelectedItem = null;
        }
    }

    private async void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FileHubViewModel vm && vm.SelectedItem is not null)
        {
            await vm.ActivateItemAsync(vm.SelectedItem);
        }
    }

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FileHubViewModel vm)
        {
            await vm.RefreshAsync();
            e.Handled = true;
        }
    }

    private async void OnFilesDropped(object? sender, DragEventArgs e)
    {
        if (DataContext is not FileHubViewModel vm)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        var localPaths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (localPaths.Count > 0)
        {
            await vm.UploadDroppedFilesAsync(localPaths);
        }
    }

    private async void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileHubViewModel vm)
        {
            return;
        }

        if (e.Key == Key.F5)
        {
            await vm.RefreshAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is not TextBox)
        {
            GetActiveFilesListBox()?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !vm.IsPreviewVisible && vm.HasSelectedItem && e.Source is not TextBox)
        {
            var listBox = GetActiveFilesListBox();
            listBox?.SelectedItems?.Clear();
            if (listBox is not null)
            {
                listBox.SelectedItem = null;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.IsPreviewVisible)
        {
            vm.ClosePreview();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && vm.SelectedItem is { IsFolder: true })
        {
            await vm.ActivateItemAsync(vm.SelectedItem);
            e.Handled = true;
        }
        else if ((e.Key == Key.Back || (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))) &&
                 e.Source is not TextBox &&
                 vm.CanNavigateBack)
        {
            await vm.NavigateBackAsync();
            e.Handled = true;
        }
    }
}
