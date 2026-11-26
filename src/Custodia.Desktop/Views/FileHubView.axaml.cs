using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class FileHubView : UserControl
{
    private FileHubViewModel? _observedVm;
    private bool _isSyncingGridSelection;

    public FileHubView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnFilesDropped);
        ListFilesListBox.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        GridFoldersListBox.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        GridFilesListBox.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        KeyDown += OnViewKeyDown;
        DataContextChanged += OnFileHubDataContextChanged;
    }

    private void OnFileHubDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedVm is not null)
        {
            _observedVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedVm = DataContext as FileHubViewModel;
        if (_observedVm is not null)
        {
            _observedVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileHubViewModel.IsCreateFolderOpen) &&
            _observedVm is { IsCreateFolderOpen: true })
        {
            Dispatcher.UIThread.Post(() =>
            {
                NewFolderTextBox.Focus();
                NewFolderTextBox.SelectAll();
            });
        }
    }

    private IEnumerable<ListBox> GetActiveFilesListBoxes()
    {
        if (DataContext is FileHubViewModel { IsGridLayout: true })
        {
            return [GridFoldersListBox, GridFilesListBox];
        }

        return [ListFilesListBox];
    }

    private ListBox? GetOtherGridListBox(ListBox current)
    {
        if (ReferenceEquals(current, GridFoldersListBox))
        {
            return GridFilesListBox;
        }

        if (ReferenceEquals(current, GridFilesListBox))
        {
            return GridFoldersListBox;
        }

        return null;
    }

    private void ClearListBoxSelection(ListBox listBox)
    {
        listBox.SelectedItems?.Clear();
        listBox.SelectedItem = null;
    }

    private void SyncGridSelectionToViewModel()
    {
        if (_isSyncingGridSelection || DataContext is not FileHubViewModel vm || !vm.IsGridLayout)
        {
            return;
        }

        var combined = new List<FileHubItemViewModel>();
        if (GridFoldersListBox.SelectedItems is not null)
        {
            combined.AddRange(GridFoldersListBox.SelectedItems.OfType<FileHubItemViewModel>());
        }

        if (GridFilesListBox.SelectedItems is not null)
        {
            combined.AddRange(GridFilesListBox.SelectedItems.OfType<FileHubItemViewModel>());
        }

        vm.UpdateSelectedItems(combined);
    }

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox || e.Source is not Visual sourceVisual)
        {
            return;
        }

        var point = e.GetCurrentPoint(listBox);
        var container = sourceVisual.FindAncestorOfType<ListBoxItem>();
        var otherGrid = GetOtherGridListBox(listBox);

        if (point.Properties.IsRightButtonPressed)
        {
            if (container?.DataContext is FileHubItemViewModel item)
            {
                if (!container.IsSelected)
                {
                    _isSyncingGridSelection = true;
                    try
                    {
                        if (otherGrid is not null)
                        {
                            ClearListBoxSelection(otherGrid);
                        }

                        listBox.SelectedItems?.Clear();
                        listBox.SelectedItem = item;
                    }
                    finally
                    {
                        _isSyncingGridSelection = false;
                    }

                    if (otherGrid is not null)
                    {
                        SyncGridSelectionToViewModel();
                    }
                }
            }
            else
            {
                // Right-clicking empty whitespace clears item selection so folder actions stand alone cleanly.
                ClearAllSelections();
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var clickedCheckBox = sourceVisual.FindAncestorOfType<CheckBox>() is not null;
        if (clickedCheckBox && container?.DataContext is FileHubItemViewModel checkedItem && listBox.SelectedItems is not null)
        {
            _isSyncingGridSelection = true;
            try
            {
                if (listBox.SelectedItems.Contains(checkedItem))
                {
                    listBox.SelectedItems.Remove(checkedItem);
                }
                else
                {
                    listBox.SelectedItems.Add(checkedItem);
                }
            }
            finally
            {
                _isSyncingGridSelection = false;
            }

            if (otherGrid is not null)
            {
                SyncGridSelectionToViewModel();
            }
            else if (DataContext is FileHubViewModel vm)
            {
                vm.UpdateSelectedItems(listBox.SelectedItems.OfType<FileHubItemViewModel>());
            }

            e.Handled = true;
            return;
        }

        // Let inline action buttons (e.g., the favorite toggle) execute without disturbing multi-selection.
        if (sourceVisual.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        if (otherGrid is null)
        {
            return;
        }

        if (container is null)
        {
            ClearAllSelections();
            return;
        }

        var hasMultiModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!hasMultiModifier && otherGrid.SelectedItems?.Count > 0)
        {
            _isSyncingGridSelection = true;
            try
            {
                ClearListBoxSelection(otherGrid);
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
        }
    }

    private void ClearAllSelections()
    {
        _isSyncingGridSelection = true;
        try
        {
            ClearListBoxSelection(ListFilesListBox);
            ClearListBoxSelection(GridFoldersListBox);
            ClearListBoxSelection(GridFilesListBox);
        }
        finally
        {
            _isSyncingGridSelection = false;
        }

        if (DataContext is FileHubViewModel vm)
        {
            vm.UpdateSelectedItems([]);
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

    private void OnGridFoldersSelectionChanged(object? sender, SelectionChangedEventArgs e) => SyncGridSelectionToViewModel();

    private void OnGridFilesSelectionChanged(object? sender, SelectionChangedEventArgs e) => SyncGridSelectionToViewModel();

    private void OnItemCheckBoxClick(object? sender, RoutedEventArgs e)
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

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        _isSyncingGridSelection = true;
        try
        {
            foreach (var listBox in GetActiveFilesListBoxes())
            {
                listBox.SelectAll();
            }
        }
        finally
        {
            _isSyncingGridSelection = false;
        }

        SyncGridSelectionToViewModel();
    }

    private void OnClearSelectionClick(object? sender, RoutedEventArgs e) => ClearAllSelections();

    private async void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not FileHubViewModel vm ||
            (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext is not FileHubItemViewModel tappedItem)
        {
            return;
        }

        await vm.ActivateItemAsync(tappedItem);
    }

    private async void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FileHubViewModel vm)
        {
            await vm.RefreshAsync();
            e.Handled = true;
        }
    }

    private async void OnNewFolderKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileHubViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            await vm.CreateFolderCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.IsCreateFolderOpen = false;
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
            OnSelectAllClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !vm.IsPreviewVisible && vm.HasSelectedItem && e.Source is not TextBox)
        {
            ClearAllSelections();
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
