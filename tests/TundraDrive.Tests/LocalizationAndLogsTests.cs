using System.Text.RegularExpressions;
using TundraDrive.Core.Models;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class LocalizationAndLogsTests
{
    [Fact]
    public void LocalizationDictionaries_HaveExactKeyParityBetweenPtBrAndEnUs()
    {
        var repoRoot = FindRepoRoot();
        var locFile = Path.Combine(repoRoot, "src", "TundraDrive.Desktop", "Localization", "LocalizationService.cs");
        var src = File.ReadAllText(locFile);

        var ptBlock = src.Split("PtBr")[1].Split("EnUs")[0];
        var enBlock = src.Split("EnUs")[1];
        var ptKeys = Regex.Matches(ptBlock, @"\[\""([^\""]+)\""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();
        var enKeys = Regex.Matches(enBlock, @"\[\""([^\""]+)\""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.NotEmpty(ptKeys);
        Assert.Equal(ptKeys, enKeys);
    }

    [Fact]
    public void FormatLocalizedMessage_TranslatesSyncCompletedAndTrashActionsToEnglish()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("en-US");

        var noChange = new SyncActivityLog { Action = "SyncCompleted", Message = "Sincronização concluída: Docs (sem alterações).", TargetPath = "Docs" };
        var withChanges = new SyncActivityLog { Action = "SyncCompleted", Message = "Sincronização concluída: Docs (3 alteração(ões)).", TargetPath = "Docs" };
        var restored = new SyncActivityLog { Action = "RemoteRestored", Message = "Restaurado da Lixeira do Google Drive: report.pdf", TargetPath = "report.pdf" };
        var deletedPerm = new SyncActivityLog { Action = "RemoteDeletedPermanent", Message = "Excluído permanentemente do Google Drive: old.zip", TargetPath = "old.zip" };
        var trashEmptied = new SyncActivityLog { Action = "TrashEmptied", Message = "Lixeira do Google Drive esvaziada." };
        var credJsonRevoked = new SyncActivityLog { Action = "CredentialsJsonRevoked", Message = "Arquivo credentials.json removido." };

        Assert.Equal("Sync completed: Docs (no changes).", ActivityLogItemViewModel.FormatLocalizedMessage(noChange, loc));
        Assert.Equal("Sync completed: Docs (3 change(s)).", ActivityLogItemViewModel.FormatLocalizedMessage(withChanges, loc));
        Assert.Contains("Restored from Google Drive Trash", ActivityLogItemViewModel.FormatLocalizedMessage(restored, loc));
        Assert.Contains("Permanently deleted from Google Drive", ActivityLogItemViewModel.FormatLocalizedMessage(deletedPerm, loc));
        Assert.Equal("Google Drive Trash emptied.", ActivityLogItemViewModel.FormatLocalizedMessage(trashEmptied, loc));
        Assert.Contains("credentials.json removed", ActivityLogItemViewModel.FormatLocalizedMessage(credJsonRevoked, loc));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TundraDrive.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("TundraDrive.sln not found.");
    }
}
