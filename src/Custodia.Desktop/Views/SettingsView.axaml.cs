using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnCredentialsDragOver);
        AddHandler(DragDrop.DropEvent, OnCredentialsDropAsync);
    }

    private static void OnCredentialsDragOver(object? sender, DragEventArgs e)
    {
        if (HasDroppedJsonFile(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.None;
    }

    private async void OnCredentialsDropAsync(object? sender, DragEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var path = ExtractFirstDroppedJsonPath(e.DataTransfer);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        e.Handled = true;
        await vm.ImportCredentialsFromPathAsync(path);
    }

    private static bool HasDroppedJsonFile(IDataTransfer data) =>
        ExtractFirstDroppedJsonPath(data) is not null;

    private static string? ExtractFirstDroppedJsonPath(IDataTransfer data)
    {
        var files = data.TryGetFiles();
        if (files is null)
        {
            return null;
        }

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)
                && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
