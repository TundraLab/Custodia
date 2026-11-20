using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += OnWindowSizeChanged;
        KeyDown += OnWindowKeyDown;
        Opened += OnWindowOpened;
        DataContextChanged += OnMainWindowDataContextChanged;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        RefreshRootLayout();
    }

    private void OnMainWindowDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.AboutVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentViewModel)
            or nameof(MainWindowViewModel.IsUpdateBannerVisible)
            or nameof(MainWindowViewModel.UiScaleFactor)
            or nameof(AboutViewModel.HasReleaseNotes)
            or nameof(AboutViewModel.IsUpdateAvailable)
            or nameof(AboutViewModel.LatestReleaseNotes))
        {
            RefreshRootLayout();
        }
    }

    private void RefreshRootLayout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RootLayoutTransform?.InvalidateMeasure();
            RootLayoutTransform?.InvalidateArrange();
            InvalidateMeasure();
            InvalidateArrange();
        }, DispatcherPriority.Loaded);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.UpdateResponsiveLayout(e.NewSize.Width / vm.UiScaleFactor);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key is Key.OemPlus or Key.Add)
        {
            vm.SettingsVm.AdjustUiScale(25);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            vm.SettingsVm.AdjustUiScale(-25);
            e.Handled = true;
        }
        else if (e.Key is Key.D0 or Key.NumPad0)
        {
            vm.SettingsVm.ResetUiScaleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSidebarStorageBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.NewSize.Width > 0)
        {
            vm.AccountsVm.SetStorageTrackWidth(e.NewSize.Width);
        }
    }
}
