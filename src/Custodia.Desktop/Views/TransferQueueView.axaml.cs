using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class TransferQueueView : UserControl
{
    private TransferQueueViewModel? _observedVm;

    public TransferQueueView()
    {
        InitializeComponent();
        DataContextChanged += OnTransferQueueDataContextChanged;
    }

    private void OnTransferQueueDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedVm is not null)
        {
            _observedVm.ScrollToTopRequested -= OnScrollToTopRequested;
        }

        _observedVm = DataContext as TransferQueueViewModel;
        if (_observedVm is not null)
        {
            _observedVm.ScrollToTopRequested += OnScrollToTopRequested;
        }
    }

    private void OnScrollToTopRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            QueueScrollViewer.Offset = new Vector(0, 0);
        });
    }
}
