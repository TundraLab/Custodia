using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed class ActivityLogItemViewModel
{
    private static readonly IBrush BrushSuccess = SolidColorBrush.Parse("#6DD58C");
    private static readonly IBrush BrushWarning = SolidColorBrush.Parse("#F5C26B");
    private static readonly IBrush BrushError = SolidColorBrush.Parse("#F28B82");
    private static readonly IBrush BrushInfo = SolidColorBrush.Parse("#A8C7FA");

    public long Id { get; init; }
    public ActivitySeverity Severity { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public bool HasTargetPath { get; init; }
    public string TimestampDisplay { get; init; } = string.Empty;
    public string SeverityLabel { get; init; } = string.Empty;
    public IBrush AccentBrush { get; init; } = SolidColorBrush.Parse("#44474F");
    public string CopyMessageLabel { get; init; } = string.Empty;
    public string CopyTargetLabel { get; init; } = string.Empty;
    public string OpenFolderLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? CopyMessageCommand { get; init; }
    public IAsyncRelayCommand? CopyTargetCommand { get; init; }
    public IAsyncRelayCommand? OpenTargetFolderCommand { get; init; }

    public static ActivityLogItemViewModel FromModel(
        SyncActivityLog model,
        ILocalizationService loc,
        ActivityLogsViewModel parent)
    {
        var hasTarget = !string.IsNullOrWhiteSpace(model.TargetPath);
        var localizedMessage = FormatLocalizedMessage(model, loc);
        return new()
        {
            Id = model.Id,
            Severity = model.Severity,
            Action = model.Action,
            Message = localizedMessage,
            TargetPath = model.TargetPath ?? "—",
            HasTargetPath = hasTarget,
            TimestampDisplay = model.CreatedAtUtc.ToLocalTime()
                .ToString("dd/MM HH:mm:ss", CultureInfo.GetCultureInfo(loc.CurrentLanguage)),
            SeverityLabel = model.Severity switch
            {
                ActivitySeverity.Success => loc.Text("SeveritySuccess"),
                ActivitySeverity.Warning => loc.Text("SeverityWarning"),
                ActivitySeverity.Error => loc.Text("SeverityError"),
                _ => loc.Text("SeverityInfo")
            },
            AccentBrush = model.Severity switch
            {
                ActivitySeverity.Success => BrushSuccess,
                ActivitySeverity.Warning => BrushWarning,
                ActivitySeverity.Error => BrushError,
                _ => BrushInfo
            },
            CopyMessageLabel = loc.Text("CopyMessageMenu"),
            CopyTargetLabel = loc.Text("CopyTargetPathMenu"),
            OpenFolderLabel = loc.Text("OpenLocalFolderMenu"),
            CopyMessageCommand = new AsyncRelayCommand(() => parent.CopyTextAsync(localizedMessage)),
            CopyTargetCommand = hasTarget
                ? new AsyncRelayCommand(() => parent.CopyTextAsync(model.TargetPath!))
                : null,
            OpenTargetFolderCommand = hasTarget
                ? new AsyncRelayCommand(() => parent.OpenLocalPathAsync(model.TargetPath!))
                : null
        };
    }

    public static string FormatLocalizedMessage(SyncActivityLog model, ILocalizationService loc)
    {
        if (!loc.IsEnglish)
        {
            return model.Message;
        }

        var msg = model.Message ?? string.Empty;
        var target = model.TargetPath;

        return model.Action switch
        {
            "AuthRestore" => $"Session restored for {ExtractTrailingValue(msg, "Sessão restaurada para ")}.",
            "AccountConnect" => $"Google account connected: {ExtractTrailingValue(msg, "Conta Google conectada: ")}.",
            "AccountSwitch" => $"Active account switched to {ExtractTrailingValue(msg, "Conta ativa alternada para ")}.",
            "AccountDisconnect" => "Google account disconnected and local tokens cleared.",
            "CredentialsRevoked" => "OAuth2 credentials.json and all account tokens/data were revoked.",
            "CredentialsJsonRevoked" => "OAuth2 credentials.json removed; connected account sessions preserved.",
            "ScheduledSync" => $"Scheduled automatic sync started at {ExtractTrailingValue(msg, "Sincronização agendada executada às ")}.",
            "FolderMapped" => TrySplitPair(ExtractTrailingValue(msg, "Pasta mapeada: "), " ↔ ", out var mapLeft, out var mapRight)
                ? $"Folder mapped: {mapLeft} ↔ {mapRight}."
                : $"Folder mapped: {target ?? msg}",
            "ModeChanged" => FormatModeChangedEn(msg, target),
            "FolderPaused" => $"Sync paused for {target ?? ExtractTrailingValue(msg, "Sincronização pausada para ")}.",
            "FolderResumed" => $"Sync resumed for {target ?? ExtractTrailingValue(msg, "Sincronização retomada para ")}.",
            "FolderUnmapped" => $"Folder mapping removed for {target ?? ExtractBetween(msg, "Mapeamento removido para ", " (arquivos")} (local files preserved).",
            "SyncSkipped" => "Sync skipped: no Google account connected.",
            "SyncStarted" => TrySplitPair(ExtractTrailingValue(msg, "Sincronização iniciada: "), " ↔ ", out var startLeft, out var startRight)
                ? $"Sync started: {startLeft} ↔ {startRight}."
                : $"Sync started: {target ?? msg}",
            "SyncCompleted" => FormatSyncCompletedEn(msg, target),
            "SyncError" => $"Folder sync error: {ExtractAfterColon(msg)}",
            "ApiUpload" => $"File uploaded via local API: {ExtractTrailingValue(msg, "Arquivo enviado via API local: ")}.",
            "ApiUploadFailed" => TryExtractParenthesizedError(msg, out var apiFile, out var apiErr)
                ? $"Local API upload failed ({apiFile}): {apiErr}"
                : $"Local API upload failed: {ExtractAfterColon(msg)}",
            "ManualDownload" => $"Downloaded to {target ?? ExtractTrailingValue(msg, "Baixado para ")}.",
            "ManualDownloadFailed" or "DownloadFailed" => TrySplitPair(ExtractTrailingValue(msg, "Falha ao baixar "), ": ", out var dlFile, out var dlErr)
                ? $"Failed to download {dlFile}: {dlErr}"
                : $"Download failed: {ExtractAfterColon(msg)}",
            "UploadFailed" => TrySplitPair(ExtractTrailingValue(msg, "Falha ao enviar "), ": ", out var ulFile, out var ulErr)
                ? $"Failed to upload {ulFile}: {ulErr}"
                : $"Upload failed: {ExtractAfterColon(msg)}",
            "InitialFolderSyncCompleted" => $"Initial sync completed for {target ?? ExtractAfterColon(msg)}.",
            "FolderCustomized" => $"Customization saved for {ExtractTrailingValue(msg, "Personalização salva para ")}.",
            "FolderCreated" => $"Folder created: {target ?? ExtractAfterColon(msg)}.",
            "GmailAlertSent" => $"Official TundraLab email sent to {target ?? ExtractTrailingValue(msg, "enviado para ")}.",
            "GmailAlertSkipped" => $"Automatic TundraLab email skipped for {target ?? "account"} (mailer webhook not configured or offline).",
            "LocalTrashed" => $"Moved local file to Trash after remote deletion: {target ?? ExtractTrailingValue(msg, "exclusão remota: ")}.",
            "RemoteTrashed" => $"Moved to Google Drive Trash: {target ?? ExtractInsideParens(msg)}.",
            "RemoteRestored" => $"Restored from Google Drive Trash: {target ?? ExtractAfterColon(msg)}.",
            "RemoteDeletedPermanent" => $"Permanently deleted from Google Drive: {target ?? ExtractAfterColon(msg)}.",
            "TrashEmptied" => "Google Drive Trash emptied.",
            "ConflictRenamed" => TryExtractConflictParts(msg, target, out var orig, out var copy)
                ? $"Conflict detected on {orig}; local copy preserved as {copy}."
                : msg,
            "Uploaded" => $"Uploaded to Drive ({ExtractInsideParens(msg)}): {target ?? ExtractAfterColon(msg)}.",
            "Downloaded" => $"Downloaded from Drive to {target ?? ExtractTrailingValue(msg, "Baixado do Drive para ")}.",
            _ => msg
        };
    }

    private static string FormatSyncCompletedEn(string msg, string? target)
    {
        if (msg.Contains("(sem alterações)", StringComparison.OrdinalIgnoreCase))
        {
            var folder = target ?? ExtractTrailingValue(msg, "Sincronização concluída: ")
                .Replace(" (sem alterações)", string.Empty, StringComparison.OrdinalIgnoreCase);
            return $"Sync completed: {folder} (no changes).";
        }

        var match = System.Text.RegularExpressions.Regex.Match(msg, @"\((\d+)\s+alteração\(ões\)\)");
        if (match.Success)
        {
            var rawFolder = ExtractTrailingValue(msg[..match.Index].TrimEnd(), "Sincronização concluída: ");
            var folder = string.IsNullOrWhiteSpace(target) ? rawFolder : target;
            return $"Sync completed: {folder} ({match.Groups[1].Value} change(s)).";
        }

        return $"Sync completed: {target ?? ExtractTrailingValue(msg, "Sincronização concluída: ")}.";
    }

    private static string ExtractTrailingValue(string msg, string prefix)
    {
        var value = msg.StartsWith(prefix, StringComparison.Ordinal) ? msg[prefix.Length..] : msg;
        return value.TrimEnd('.');
    }

    private static string ExtractAfterColon(string msg)
    {
        var idx = msg.IndexOf(": ", StringComparison.Ordinal);
        return idx >= 0 ? msg[(idx + 2)..] : msg;
    }

    private static string ExtractBetween(string msg, string start, string end)
    {
        var startIdx = msg.IndexOf(start, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            return msg;
        }

        startIdx += start.Length;
        var endIdx = msg.IndexOf(end, startIdx, StringComparison.Ordinal);
        return endIdx > startIdx ? msg[startIdx..endIdx] : msg[startIdx..];
    }

    private static string ExtractInsideParens(string msg)
    {
        var open = msg.LastIndexOf('(');
        var close = open >= 0 ? msg.IndexOf(')', open + 1) : -1;
        return open >= 0 && close > open ? msg[(open + 1)..close] : msg;
    }

    private static bool TrySplitPair(string input, string separator, out string left, out string right)
    {
        var idx = input.IndexOf(separator, StringComparison.Ordinal);
        if (idx >= 0)
        {
            left = input[..idx];
            right = input[(idx + separator.Length)..];
            return true;
        }

        left = input;
        right = string.Empty;
        return false;
    }

    private static bool TryExtractParenthesizedError(string msg, out string fileName, out string error)
    {
        var open = msg.IndexOf('(');
        var close = open >= 0 ? msg.IndexOf("): ", open + 1, StringComparison.Ordinal) : -1;
        if (open >= 0 && close > open)
        {
            fileName = msg[(open + 1)..close];
            error = msg[(close + 3)..];
            return true;
        }

        fileName = string.Empty;
        error = msg;
        return false;
    }

    private static bool TryExtractConflictParts(string msg, string? target, out string originalPath, out string copyName)
    {
        const string marker = "; cópia local preservada como ";
        var idx = msg.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            originalPath = target ?? ExtractTrailingValue(msg[..idx], "Conflito detectado em ");
            copyName = msg[(idx + marker.Length)..].TrimEnd('.');
            return true;
        }

        originalPath = target ?? string.Empty;
        copyName = string.Empty;
        return false;
    }

    private static string FormatModeChangedEn(string msg, string? target)
    {
        var rawMode = ExtractBetween(msg, "Modo alterado para ", " em ");
        var enMode = rawMode switch
        {
            "Sincronização Bidirecional" => "Two-Way Sync",
            "Backup Unidirecional" => "One-Way Backup",
            _ => rawMode
        };

        var path = target ?? ExtractTrailingValue(msg, $"Modo alterado para {rawMode} em ");
        return $"Mode changed to {enMode} for {path}.";
    }
}

public sealed partial class ActivityLogsViewModel : ViewModelBase
{
    private readonly ISyncStateRepository _repository;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private int _selectedSeverityFilterIndex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusSummary = string.Empty;

    [ObservableProperty]
    private bool _hasNoLogs = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLog))]
    [NotifyPropertyChangedFor(nameof(CanOpenSelectedTarget))]
    private ActivityLogItemViewModel? _selectedLog;

    public bool HasSelectedLog => SelectedLog is not null;
    public bool CanOpenSelectedTarget => SelectedLog is not null && !string.IsNullOrWhiteSpace(SelectedLog.TargetPath) && SelectedLog.TargetPath != "—";

    public Func<string, Task>? CopyTextCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }

    public ObservableCollection<ActivityLogItemViewModel> Logs { get; } = [];
    public ObservableCollection<string> SeverityFilterOptions { get; } = [];

    public ActivityLogsViewModel(
        ISyncStateRepository repository,
        ILocalizationService localization)
    {
        _repository = repository;
        Loc = localization;
        RebuildFilterOptions();

        Loc.LanguageChanged += (_, _) =>
        {
            RebuildFilterOptions();
            _ = RefreshLogsAsync();
        };
    }

    partial void OnSelectedSeverityFilterIndexChanged(int value)
    {
        if (value >= 0)
        {
            _ = RefreshLogsAsync();
        }
    }

    public void PrependLiveLog(SyncActivityLog log)
    {
        var minSeverity = GetMinSeverityFilter();
        if (minSeverity.HasValue && log.Severity < minSeverity.Value)
        {
            return;
        }

        var localizedMessage = ActivityLogItemViewModel.FormatLocalizedMessage(log, Loc);
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !localizedMessage.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
            !log.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
            !(log.TargetPath?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return;
        }

        Logs.Insert(0, ActivityLogItemViewModel.FromModel(log, Loc, this));
        if (Logs.Count > 300)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        StatusSummary = Loc.Format("LogsCountFmt", Logs.Count);
        HasNoLogs = Logs.Count == 0;
    }

    [RelayCommand]
    public async Task RefreshLogsAsync()
    {
        var minSeverity = GetMinSeverityFilter();
        var items = await _repository.GetActivityLogsAsync(minSeverity, SearchText, 250);
        Logs.Clear();
        foreach (var item in items)
        {
            Logs.Add(ActivityLogItemViewModel.FromModel(item, Loc, this));
        }

        HasNoLogs = Logs.Count == 0;
        StatusSummary = Loc.Format("LogsCountFmt", Logs.Count);
    }

    public Task CopyTextAsync(string text)
    {
        if (CopyTextCallback is null || string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        return CopyTextCallback.Invoke(text);
    }

    public Task OpenLocalPathAsync(string path)
    {
        if (OpenLocalFolderCallback is null || string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        return OpenLocalFolderCallback.Invoke(path);
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        await _repository.ClearActivityLogsAsync();
        Logs.Clear();
        HasNoLogs = true;
        StatusSummary = Loc.Format("LogsCountFmt", 0);
    }

    [RelayCommand]
    private async Task CopySelectedMessageAsync()
    {
        if (CopyTextCallback is not null && SelectedLog is not null && !string.IsNullOrWhiteSpace(SelectedLog.Message))
        {
            await CopyTextCallback.Invoke(SelectedLog.Message);
        }
    }

    [RelayCommand]
    private async Task CopySelectedTargetAsync()
    {
        if (CopyTextCallback is not null && SelectedLog is not null && !string.IsNullOrWhiteSpace(SelectedLog.TargetPath) && SelectedLog.TargetPath != "—")
        {
            await CopyTextCallback.Invoke(SelectedLog.TargetPath);
        }
    }

    [RelayCommand]
    private async Task OpenSelectedTargetFolderAsync()
    {
        if (OpenLocalFolderCallback is not null && SelectedLog is not null && !string.IsNullOrWhiteSpace(SelectedLog.TargetPath) && SelectedLog.TargetPath != "—")
        {
            await OpenLocalFolderCallback.Invoke(SelectedLog.TargetPath);
        }
    }

    private void RebuildFilterOptions()
    {
        var savedIndex = Math.Max(0, SelectedSeverityFilterIndex);
        SeverityFilterOptions.Clear();
        SeverityFilterOptions.Add(Loc.Text("SeverityFilterAll"));
        SeverityFilterOptions.Add(Loc.Text("SeverityFilterWarnErr"));
        SeverityFilterOptions.Add(Loc.Text("SeverityFilterErr"));
        SelectedSeverityFilterIndex = Math.Clamp(savedIndex, 0, SeverityFilterOptions.Count - 1);
    }

    private ActivitySeverity? GetMinSeverityFilter() => SelectedSeverityFilterIndex switch
    {
        1 => ActivitySeverity.Warning,
        2 => ActivitySeverity.Error,
        _ => null
    };
}
