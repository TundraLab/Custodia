using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class TransferItemViewModel : ViewModelBase
{
    private readonly TransferQueueViewModel _parent;

    public ILocalizationService Loc { get; }
    public string Id { get; }
    public string SyncFolderId { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public string LocalPath { get; }
    public TransferDirection Direction { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private long _animateUntilMs;
    private long _errorAnimateUntilMs;
    private long _holdVersion;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoteFileId))]
    private string? _remoteFileId;

    [ObservableProperty]
    private DateTimeOffset? _completedAtUtc;

    [ObservableProperty]
    private TransferStatus _status;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _transferredBytes;

    [ObservableProperty]
    private string? _errorMessage;

    public TransferItemViewModel(
        TransferQueueItem item,
        TransferQueueViewModel parent,
        ILocalizationService localization)
    {
        _parent = parent;
        Loc = localization;
        Id = item.Id;
        SyncFolderId = item.SyncFolderId;
        FileName = item.FileName;
        RelativePath = item.RelativePath;
        LocalPath = item.LocalPath;
        RemoteFileId = item.RemoteFileId;
        Direction = item.Direction;
        CreatedAtUtc = item.CreatedAtUtc;
        UpdateFrom(item);
    }

    public void UpdateFrom(TransferQueueItem item)
    {
        RemoteFileId = item.RemoteFileId ?? RemoteFileId;
        Status = item.Status;
        TotalBytes = item.TotalBytes;
        TransferredBytes = item.TransferredBytes;
        ErrorMessage = item.ErrorMessage;
        CompletedAtUtc = item.CompletedAtUtc;

        if (item.Status is TransferStatus.Queued or TransferStatus.InProgress)
        {
            LatchAnimationHold();
        }
        else if (item.Status == TransferStatus.Failed)
        {
            var ageMs = item.CompletedAtUtc.HasValue
                ? (DateTimeOffset.UtcNow - item.CompletedAtUtc.Value).TotalMilliseconds
                : 0;
            if (ageMs < 6000)
            {
                LatchErrorAnimationHold();
            }
        }
        else if (item.Status == TransferStatus.Completed && _animateUntilMs == 0 && item.CompletedAtUtc.HasValue)
        {
            var ageMs = (DateTimeOffset.UtcNow - item.CompletedAtUtc.Value).TotalMilliseconds;
            if (ageMs >= -500 && ageMs < 1500)
            {
                LatchAnimationHold();
            }
        }

        RefreshLocalizedProperties();
    }

    private void LatchAnimationHold()
    {
        _animateUntilMs = Environment.TickCount64 + 1600;
        var version = ++_holdVersion;
        _ = ReleaseAnimationHoldAsync(version, 1650);
    }

    private void LatchErrorAnimationHold()
    {
        _errorAnimateUntilMs = Environment.TickCount64 + 6000;
        var version = ++_holdVersion;
        _ = ReleaseAnimationHoldAsync(version, 6050);
    }

    private async Task ReleaseAnimationHoldAsync(long version, int delayMs)
    {
        await Task.Delay(delayMs);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (version != _holdVersion)
            {
                return;
            }

            RefreshActiveIndicatorProperties();
        });
    }

    private void RefreshActiveIndicatorProperties()
    {
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsFailedActive));
        OnPropertyChanged(nameof(IsFailedStatic));
        OnPropertyChanged(nameof(IsAnimationActive));
        OnPropertyChanged(nameof(IsUploadingActive));
        OnPropertyChanged(nameof(IsDownloadingActive));
        OnPropertyChanged(nameof(IsDeletingActive));
        OnPropertyChanged(nameof(IsUploadingStatic));
        OnPropertyChanged(nameof(IsDownloadingStatic));
        OnPropertyChanged(nameof(IsDeletingStatic));
        OnPropertyChanged(nameof(ErrorHeadlineText));
    }

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(TimestampDisplay));
        OnPropertyChanged(nameof(StartedDisplay));
        OnPropertyChanged(nameof(CompletedDisplay));
        OnPropertyChanged(nameof(HasCompletedTime));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(SpeedDisplay));
        OnPropertyChanged(nameof(HasSpeedDisplay));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(DirectionDisplay));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(HasError));
        RefreshActiveIndicatorProperties();
        OnPropertyChanged(nameof(TransferProgressBrush));
        OnPropertyChanged(nameof(AccentBorderBrush));
    }

    public string SubtitleText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RelativePath)
                && !string.Equals(RelativePath, FileName, StringComparison.Ordinal))
            {
                return RelativePath;
            }

            if (HasLocalPath)
            {
                var dir = Path.GetDirectoryName(LocalPath);
                return string.IsNullOrWhiteSpace(dir) ? LocalPath : dir;
            }

            return "Google Drive";
        }
    }

    public string TimestampDisplay => (CompletedAtUtc ?? CreatedAtUtc)
        .ToLocalTime()
        .ToString("dd/MM HH:mm:ss", CultureInfo.GetCultureInfo(Loc.CurrentLanguage));

    public string StartedDisplay => CreatedAtUtc
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo(Loc.CurrentLanguage));

    public bool HasCompletedTime => CompletedAtUtc.HasValue;

    public string CompletedDisplay => CompletedAtUtc?
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo(Loc.CurrentLanguage)) ?? string.Empty;

    public string DurationDisplay
    {
        get
        {
            if (!CompletedAtUtc.HasValue)
            {
                return "—";
            }

            var elapsed = CompletedAtUtc.Value - CreatedAtUtc;
            if (elapsed.TotalSeconds < 1.0)
            {
                return $"{Math.Max(1, elapsed.TotalMilliseconds):0} ms";
            }

            if (elapsed.TotalMinutes < 1.0)
            {
                return $"{elapsed.TotalSeconds:0.0} s";
            }

            return $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds:00} s";
        }
    }

    public bool HasSpeedDisplay => CompletedAtUtc.HasValue
        && TransferredBytes > 0
        && (CompletedAtUtc.Value - CreatedAtUtc).TotalSeconds > 0.01;

    public string SpeedDisplay
    {
        get
        {
            if (!HasSpeedDisplay || !CompletedAtUtc.HasValue)
            {
                return string.Empty;
            }

            var sec = Math.Max(0.01, (CompletedAtUtc.Value - CreatedAtUtc).TotalSeconds);
            return $"{UiConverters.FormatBytes((long)(TransferredBytes / sec))}/s";
        }
    }

    public bool HasLocalPath => !string.IsNullOrWhiteSpace(LocalPath) && Path.IsPathRooted(LocalPath);
    public bool HasRemoteFileId => !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsInProgress => Status == TransferStatus.InProgress;
    public bool IsFailed => Status == TransferStatus.Failed;
    public bool IsFailedActive => IsFailed && Environment.TickCount64 < _errorAnimateUntilMs;
    public bool IsFailedStatic => IsFailed && !IsFailedActive;

    public string ErrorHeadlineText
    {
        get
        {
            if (!IsFailed)
            {
                return string.Empty;
            }

            var detail = string.IsNullOrWhiteSpace(ErrorMessage)
                ? Loc.Text("TransferFailed")
                : ErrorMessage;
            return Direction switch
            {
                TransferDirection.Upload => Loc.Format("TransferUploadErrorFmt", FileName, detail),
                TransferDirection.Download => Loc.Format("TransferDownloadErrorFmt", FileName, detail),
                _ => Loc.Format("TransferFailedFmt", detail)
            };
        }
    }

    public bool IsAnimationActive => IsInProgress
        || Status == TransferStatus.Queued
        || (Status == TransferStatus.Completed && Environment.TickCount64 < _animateUntilMs);
    public bool IsUploadingActive => IsAnimationActive && !IsFailed && Direction == TransferDirection.Upload;
    public bool IsDownloadingActive => IsAnimationActive && !IsFailed && Direction == TransferDirection.Download;
    public bool IsDeletingActive => IsAnimationActive && !IsFailed && Direction == TransferDirection.Delete;

    public bool IsUploadingStatic => !IsAnimationActive && !IsFailed && Direction == TransferDirection.Upload;
    public bool IsDownloadingStatic => !IsAnimationActive && !IsFailed && Direction == TransferDirection.Download;
    public bool IsDeletingStatic => !IsAnimationActive && !IsFailed && Direction == TransferDirection.Delete;

    public double ProgressPercentage =>
        TotalBytes <= 0
            ? (Status == TransferStatus.Completed ? 100.0 : 0.0)
            : Math.Clamp((double)TransferredBytes / TotalBytes * 100.0, 0.0, 100.0);

    public string DirectionDisplay => Direction switch
    {
        TransferDirection.Upload => Loc.Text("TransferDirectionUpload"),
        TransferDirection.Download => Loc.Text("TransferDirectionDownload"),
        TransferDirection.Delete => Loc.Text("TransferDirectionDelete"),
        _ => Loc.Text("TransferDirectionDownload")
    };

    public string ProgressDisplay =>
        $"{UiConverters.FormatBytes(TransferredBytes)} / {UiConverters.FormatBytes(TotalBytes)} ({ProgressPercentage:0}%)";

    public string StatusDisplay => Status switch
    {
        TransferStatus.Queued => Loc.Text("TransferQueued"),
        TransferStatus.InProgress => Loc.Text("TransferInProgress"),
        TransferStatus.Completed => Loc.Text("TransferCompleted"),
        TransferStatus.Cancelled => Loc.Text("TransferCancelled"),
        TransferStatus.Failed => string.IsNullOrWhiteSpace(ErrorMessage)
            ? Loc.Text("TransferFailed")
            : Loc.Format("TransferFailedFmt", ErrorMessage),
        _ => string.Empty
    };

    public bool CanCancel => Status is TransferStatus.Queued or TransferStatus.InProgress;

    public IBrush TransferProgressBrush => Status switch
    {
        TransferStatus.Failed => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E"),
        TransferStatus.Cancelled => ThemeAccentService.ResolveThemeBrush("#8E9099", "#74777F"),
        _ => Direction switch
        {
            TransferDirection.Upload => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0"),
            TransferDirection.Download => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E"),
            TransferDirection.Delete => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E"),
            _ => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0")
        }
    };

    public IBrush AccentBorderBrush => Status switch
    {
        TransferStatus.InProgress => Direction switch
        {
            TransferDirection.Download => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E"),
            TransferDirection.Delete => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E"),
            _ => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0")
        },
        TransferStatus.Completed => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E"),
        TransferStatus.Failed => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E"),
        TransferStatus.Cancelled => ThemeAccentService.ResolveThemeBrush("#8E9099", "#74777F"),
        _ => ThemeAccentService.ResolveThemeBrush("#44474F", "#C4C6D0")
    };

    internal void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(TransferProgressBrush));
        OnPropertyChanged(nameof(AccentBorderBrush));
    }

    [RelayCommand]
    private void Cancel() => _parent.CancelTransfer(Id);

    [RelayCommand]
    private Task CopyNameAsync() => _parent.CopyTextAsync(FileName);

    [RelayCommand]
    private Task CopyPathAsync() => _parent.CopyTextAsync(HasLocalPath ? LocalPath : RelativePath);

    [RelayCommand]
    private Task CopyRemoteIdAsync() => HasRemoteFileId ? _parent.CopyTextAsync(RemoteFileId!) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenFolderAsync() => HasLocalPath ? _parent.OpenLocalFolderAsync(LocalPath) : Task.CompletedTask;
}

public sealed partial class TransferQueueViewModel : ViewModelBase
{
    private const int MaxUiRetainedItems = 2000;
    private readonly IDriveSyncService _syncService;
    private int _prunedUiCompletedCount;
    private int _prunedUiFailedCount;
    private int _prunedUiCancelledCount;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasFailedTransfers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastErrorBanner))]
    private string _lastErrorMessage = string.Empty;

    public bool HasLastErrorBanner => !string.IsNullOrWhiteSpace(LastErrorMessage);

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _hasNoItems = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelSelected))]
    private TransferItemViewModel? _selectedItem;

    public bool CanCancelSelected => SelectedItem is { CanCancel: true };

    public TransferDirection? ActiveDirection =>
        Items.FirstOrDefault(i => i.Status == TransferStatus.InProgress)?.Direction
        ?? Items.FirstOrDefault(i => i.Status == TransferStatus.Queued)?.Direction;

    public Func<string, Task>? CopyTextCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }
    public event EventHandler? ScrollToTopRequested;

    partial void OnSelectedItemChanged(TransferItemViewModel? oldValue, TransferItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    public ObservableCollection<TransferItemViewModel> Items { get; } = [];

    public TransferQueueViewModel(
        IDriveSyncService syncService,
        ILocalizationService localization)
    {
        _syncService = syncService;
        Loc = localization;

        Loc.LanguageChanged += (_, _) =>
        {
            foreach (var item in Items)
            {
                item.RefreshLocalizedProperties();
            }
            UpdateCounters();
        };

        ThemeAccentService.ThemeChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var item in Items)
                {
                    item.RefreshThemeBrushes();
                }
            });

        ReloadSnapshot();
    }

    public void ReloadSnapshot()
    {
        _prunedUiCompletedCount = 0;
        _prunedUiFailedCount = 0;
        _prunedUiCancelledCount = 0;
        Items.Clear();
        foreach (var item in _syncService.GetTransferQueueSnapshot())
        {
            Items.Add(new TransferItemViewModel(item, this, Loc));
        }

        var firstError = Items.FirstOrDefault(i => i.IsFailed);
        LastErrorMessage = firstError is not null ? firstError.ErrorHeadlineText : string.Empty;

        UpdateCounters();
    }

    public void UpsertTransfer(TransferQueueItem item)
    {
        var becameCompleted = false;
        var existing = Items.FirstOrDefault(x => x.Id == item.Id);
        if (existing is not null)
        {
            var wasCompleted = existing.Status == TransferStatus.Completed;
            existing.UpdateFrom(item);
            becameCompleted = !wasCompleted && item.Status == TransferStatus.Completed;
        }
        else
        {
            existing = new TransferItemViewModel(item, this, Loc);
            Items.Insert(0, existing);
            becameCompleted = item.Status == TransferStatus.Completed;
            while (Items.Count > MaxUiRetainedItems)
            {
                var removable = Items.LastOrDefault(x => !x.CanCancel);
                if (removable is null)
                {
                    break;
                }

                switch (removable.Status)
                {
                    case TransferStatus.Completed:
                        _prunedUiCompletedCount++;
                        break;
                    case TransferStatus.Failed:
                        _prunedUiFailedCount++;
                        break;
                    case TransferStatus.Cancelled:
                        _prunedUiCancelledCount++;
                        break;
                }
                Items.Remove(removable);
            }
        }

        if (becameCompleted)
        {
            var idx = Items.IndexOf(existing);
            if (idx > 0)
            {
                Items.Move(idx, 0);
            }

            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }

        if (item.Status == TransferStatus.Failed)
        {
            LastErrorMessage = existing.ErrorHeadlineText;
        }

        UpdateCounters();
    }

    [RelayCommand]
    private void DismissLastErrorBanner() => LastErrorMessage = string.Empty;

    public void CancelTransfer(string transferId)
    {
        _syncService.CancelTransfer(transferId);
    }

    public Task CopyTextAsync(string text)
    {
        if (CopyTextCallback is null || string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        return CopyTextCallback.Invoke(text);
    }

    public Task OpenLocalFolderAsync(string path)
    {
        if (OpenLocalFolderCallback is null || string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        return OpenLocalFolderCallback.Invoke(path);
    }

    [RelayCommand]
    private void CancelSelected()
    {
        if (SelectedItem is { CanCancel: true })
        {
            CancelTransfer(SelectedItem.Id);
        }
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        _prunedUiCompletedCount = 0;
        _prunedUiFailedCount = 0;
        _prunedUiCancelledCount = 0;
        LastErrorMessage = string.Empty;
        _syncService.ClearCompletedTransfers();
        ReloadSnapshot();
    }

    private void UpdateCounters()
    {
        var uiActive = Items.Count(i => i.Status is TransferStatus.Queued or TransferStatus.InProgress);
        var uiCompleted = Items.Count(i => i.Status == TransferStatus.Completed) + _prunedUiCompletedCount;
        var uiFailed = Items.Count(i => i.Status == TransferStatus.Failed) + _prunedUiFailedCount;
        var uiTotal = Items.Count + _prunedUiCompletedCount + _prunedUiFailedCount + _prunedUiCancelledCount;

        var serviceStats = _syncService.GetTransferQueueStats();
        if (serviceStats is not null && serviceStats.TotalCount > uiTotal)
        {
            ActiveCount = serviceStats.ActiveCount;
            CompletedCount = serviceStats.CompletedCount;
            FailedCount = serviceStats.FailedCount;
            TotalCount = serviceStats.TotalCount;
        }
        else
        {
            ActiveCount = uiActive;
            CompletedCount = uiCompleted;
            FailedCount = uiFailed;
            TotalCount = uiTotal;
        }

        HasFailedTransfers = FailedCount > 0;
        HasNoItems = TotalCount == 0 && Items.Count == 0;
        SummaryText = TotalCount == 0
            ? Loc.Text("TransferEmpty")
            : FailedCount > 0
                ? Loc.Format("TransferSummaryWithErrorsFmt", ActiveCount, CompletedCount, FailedCount, TotalCount)
                : Loc.Format("TransferSummaryFmt", ActiveCount, CompletedCount, TotalCount);
        OnPropertyChanged(nameof(CanCancelSelected));
    }
}
