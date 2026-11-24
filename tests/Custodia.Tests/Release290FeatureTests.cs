using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Notifications;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class Release290FeatureTests
{
    [Fact]
    public void TransferQueue_RetainsMoreThan200Items_AndExposesErrorBanner()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var queueVm = new TransferQueueViewModel(syncProxy, loc);

        for (var i = 0; i < 260; i++)
        {
            queueVm.UpsertTransfer(new TransferQueueItem
            {
                Id = $"item-{i}",
                FileName = $"file-{i}.txt",
                RelativePath = $"file-{i}.txt",
                Direction = TransferDirection.Upload,
                Status = TransferStatus.Completed,
                TotalBytes = 128,
                TransferredBytes = 128,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
        }

        Assert.Equal(260, queueVm.Items.Count);
        Assert.Equal(260, queueVm.TotalCount);
        Assert.Equal(260, queueVm.CompletedCount);

        var failedUpload = new TransferQueueItem
        {
            Id = "err-up",
            FileName = "relatorio.pdf",
            RelativePath = "relatorio.pdf",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Failed,
            TotalBytes = 500,
            TransferredBytes = 120,
            ErrorMessage = "Quota exceeded",
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        queueVm.UpsertTransfer(failedUpload);

        Assert.True(queueVm.HasFailedTransfers);
        Assert.True(queueVm.HasLastErrorBanner);
        Assert.Contains("relatorio.pdf", queueVm.LastErrorMessage);
        Assert.Contains("Quota exceeded", queueVm.LastErrorMessage);
        Assert.True(queueVm.Items[0].IsFailed);
        Assert.True(queueVm.Items[0].IsFailedActive);
        Assert.Contains("relatorio.pdf", queueVm.Items[0].ErrorHeadlineText);

        queueVm.DismissLastErrorBannerCommand.Execute(null);
        Assert.False(queueVm.HasLastErrorBanner);
    }

    [Fact]
    public async Task DriveSyncService_TriggerFolderSync_StopsSyncStateBeforeFinalEvent_AndSyncsEmptySubfolders()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-sync-test-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "Work");
        var emptySubdir = Path.Combine(localFolder, "EmptySubdir");
        var dbPath = Path.Combine(tempRoot, "test.db");
        Directory.CreateDirectory(emptySubdir);

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, MockSyncDriveClientProxy>();
            var clientState = (MockSyncDriveClientProxy)(object)mockClient;
            var settings = new TempAppSettingsService();
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance);

            var runningStatesOnChange = new List<bool>();
            var loggedActions = new List<string>();
            syncService.SyncFoldersChanged += (_, _) => runningStatesOnChange.Add(syncService.IsSyncRunning);
            syncService.ActivityLogged += (_, log) => loggedActions.Add(log.Action);

            var added = await syncService.AddSyncFolderAsync(
                localFolder,
                "remote-root-folder",
                "Meus Arquivos/Work",
                SyncMode.TwoWay);

            // Wait briefly for fire-and-forget initial sync from AddSyncFolderAsync to settle if triggered
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((syncService.IsSyncRunning || !loggedActions.Contains("InitialFolderSyncCompleted"))
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Contains("EmptySubdir", clientState.CreatedFolderNames);
            Assert.Contains("InitialFolderSyncCompleted", loggedActions);
            Assert.False(syncService.IsSyncRunning);
            Assert.NotEmpty(runningStatesOnChange);
            Assert.False(runningStatesOnChange[^1]);

            loggedActions.Clear();
            runningStatesOnChange.Clear();

            await syncService.TriggerFolderSyncAsync(added.Id);
            Assert.False(syncService.IsSyncRunning);
            Assert.NotEmpty(runningStatesOnChange);
            Assert.False(runningStatesOnChange[^1]);
            Assert.DoesNotContain("InitialFolderSyncCompleted", loggedActions);
            Assert.Contains("SyncCompleted", loggedActions);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AboutViewModel_OfflineUpdateCheck_StopsAnimationAndShowsScheduleSummary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-about-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var failingHandler = new FailingHttpMessageHandler();
            using var updateService = new GitHubAppUpdateService(
                NullLogger<GitHubAppUpdateService>.Instance,
                failingHandler,
                tempDir,
                "2.9.0");

            var settings = new TempAppSettingsService();
            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");

            var aboutVm = new AboutViewModel(loc, updateService, settings)
            {
                MinimumCheckAnimationDuration = TimeSpan.FromMilliseconds(150)
            };

            Assert.Contains("Agendamento:", aboutVm.UpdateScheduleSummary);

            settings.Current.AutoCheckUpdates = false;
            await settings.SaveAsync(settings.Current);
            aboutVm.RefreshUpdateScheduleSummary();
            Assert.Contains("desativada", aboutVm.UpdateScheduleSummary, StringComparison.OrdinalIgnoreCase);

            await aboutVm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.False(aboutVm.IsCheckingUpdates);
            Assert.True(aboutVm.IsUpdateError);
            Assert.Equal(0, aboutVm.UpdateCheckProgress);
            Assert.False(string.IsNullOrWhiteSpace(aboutVm.UpdateStatusText));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteSyncStateRepository_PersistsFolderCustomization()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-db-cust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "custodia.db");

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var folder = new LocalSyncFolder
            {
                Id = "folder-custom-1",
                AccountId = "default",
                LocalPath = Path.Combine(tempDir, "Docs"),
                RemoteFolderId = "rem-1",
                RemoteFolderName = "Meus Arquivos/Docs",
                Mode = SyncMode.TwoWay,
                CustomAlias = "Projetos Ativos",
                CustomColorHex = "#10B981",
                CustomIconKey = "code"
            };

            await repo.UpsertSyncFolderAsync(folder);

            var loaded = await repo.GetSyncFolderByIdAsync("folder-custom-1");
            Assert.NotNull(loaded);
            Assert.Equal("Projetos Ativos", loaded.CustomAlias);
            Assert.Equal("#10B981", loaded.CustomColorHex);
            Assert.Equal("code", loaded.CustomIconKey);
            Assert.Equal("Projetos Ativos", loaded.EffectiveDisplayName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void NotificationSoundService_BuildWavBytes_ProducesValidRiffWaveForAllPresets()
    {
        foreach (var preset in NotificationSoundService.Presets)
        {
            var wav = NotificationSoundService.BuildWavBytes(preset.Id);
            AssertValidWave(wav);
        }

        var errorWav = NotificationSoundService.BuildWavBytes("error");
        AssertValidWave(errorWav);
    }

    private static void AssertValidWave(byte[] wav)
    {
        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Contains(wav.Skip(44), b => b != 0);
    }

    [Fact]
    public async Task AuthCredentialManager_OrganizesAccountFolderByEmail_AndInspectsMetadata()
    {
        var mgr = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        var accountKey = $"acc_test_{Guid.NewGuid():N}"[..16];
        var email = $"custodia-test-{Guid.NewGuid():N}@example.com";
        var sourceJson = Path.Combine(Path.GetTempPath(), $"client_secret_test_{Guid.NewGuid():N}.json");
        var expectedFolder = mgr.GetAccountFolderPath(accountKey, email);

        try
        {
            await File.WriteAllTextAsync(sourceJson, """
                {
                  "installed": {
                    "client_id": "123456789012-abcdefghijklmnopqrstuvwxyz.apps.googleusercontent.com",
                    "project_id": "custodia-unit-test",
                    "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                    "token_uri": "https://oauth2.googleapis.com/token",
                    "client_secret": "GOCSPX-testsecret"
                  }
                }
                """);

            await mgr.ImportCredentialsFileAsync(sourceJson, accountKey);
            await mgr.OrganizeAccountDirectoryAsync(accountKey, email, "Unit Tester");

            Assert.True(Directory.Exists(expectedFolder));
            Assert.EndsWith(email, expectedFolder, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(expectedFolder, "credentials.json")));
            Assert.True(File.Exists(Path.Combine(expectedFolder, "conta.json")));

            var resolved = mgr.ResolveCredentialsFilePath(accountKey);
            Assert.Equal(Path.Combine(expectedFolder, "credentials.json"), resolved);

            var meta = mgr.InspectCredentialsFile(accountKey);
            Assert.NotNull(meta);
            Assert.True(meta.IsPerAccount);
            Assert.Equal("custodia-unit-test", meta.ProjectId);
            Assert.Contains("apps.googleusercontent.com", meta.MaskedClientId);
        }
        finally
        {
            mgr.DeleteAccountCredentialsFile(accountKey, email);
            if (Directory.Exists(expectedFolder))
            {
                Directory.Delete(expectedFolder, recursive: true);
            }

            var fallbackDir = mgr.GetAccountFolderPath(accountKey);
            if (Directory.Exists(fallbackDir))
            {
                Directory.Delete(fallbackDir, recursive: true);
            }

            if (File.Exists(sourceJson))
            {
                File.Delete(sourceJson);
            }
        }
    }

    [Fact]
    public async Task AuthCredentialManager_FindCredentialsCandidatesInDirectory_FiltersDesktopJson()
    {
        var mgr = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-dl-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "client_secret_web.json"), """
                { "web": { "client_id": "bad-web.apps.googleusercontent.com" } }
                """);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "other_file.json"), """
                { "installed": { "client_id": "ignored-prefix.apps.googleusercontent.com" } }
                """);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "client_secret_ok.json"), """
                { "installed": { "client_id": "999999999-valid.apps.googleusercontent.com", "project_id": "dl-proj" } }
                """);

            var found = mgr.FindCredentialsCandidatesInDirectory(tempDir);
            Assert.Single(found);
            Assert.Equal("client_secret_ok.json", found[0].FileName);
            Assert.Equal("dl-proj", found[0].ProjectId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsViewModel_ImportCredentialsFromPath_SurfacesValidationErrorForWebJson()
    {
        var settings = new TempAppSettingsService();
        var apiProxy = DispatchProxy.Create<IEmbeddedApiServer, StubBasicProxy>();
        var autostartProxy = DispatchProxy.Create<IAutostartManager, StubBasicProxy>();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");
        var notify = new RecordingNotificationService();
        var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        using var cleaner = new MemoryCleanerService(settings, NullLogger<MemoryCleanerService>.Instance);

        var vm = new SettingsViewModel(settings, apiProxy, autostartProxy, loc, notify, auth, syncProxy, cleaner);

        var webJson = Path.Combine(Path.GetTempPath(), $"web-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(webJson, """
                { "web": { "client_id": "123.apps.googleusercontent.com", "client_secret": "sec" } }
                """);

            await vm.ImportCredentialsFromPathAsync(webJson);
            Assert.True(vm.HasCredentialsFeedback);
            Assert.True(vm.IsCredentialsFeedbackError);
            Assert.Contains("installed", vm.CredentialsFeedbackMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(webJson))
            {
                File.Delete(webJson);
            }
        }
    }

    private class StubBasicProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_IsRunning" => false,
                "get_IsSupported" => false,
                "IsEnabled" => false,
                "get_BoundAddress" => "http://127.0.0.1:5080",
                "get_DesktopEntryPath" => "/tmp/custodia.desktop",
                _ => null
            };
    }

    [Fact]
    public async Task DriveSyncService_PreservesFolderConfigurationAcrossAccountRemovalAndReAdd_UnlessDeleted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-reattach-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "DocsSaved");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        var email = $"reattach-{Guid.NewGuid():N}@example.com";
        Directory.CreateDirectory(localFolder);

        var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        string? accountDir = null;

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, MockAccountFlowDriveClientProxy>();
            var clientState = (MockAccountFlowDriveClientProxy)(object)mockClient;
            clientState.Email = email;

            var settings = new TempAppSettingsService();
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance);

            var connected1 = await syncService.ConnectAccountAsync(addNewAccount: false);
            accountDir = auth.GetAccountFolderPath(connected1.Id, email);

            var added = await syncService.AddSyncFolderAsync(localFolder, "drv-docs-1", "Meus Arquivos/DocsSaved", SyncMode.OneWayBackup);
            await syncService.UpdateFolderCustomizationAsync(added.Id, "Trabalho", "#10B981", "work");

            // Disconnect without deleting saved folder configuration
            await syncService.DisconnectAccountAsync(connected1.Id, deleteFolderConfiguration: false);
            Assert.Empty(await syncService.GetSyncFoldersAsync());

            // Simulate re-adding the same email with a brand new internal account ID
            clientState.ForcedNextAccountId = "acc_new_readded_99";
            var connected2 = await syncService.ConnectAccountAsync(addNewAccount: true);
            var restoredFolders = await syncService.GetSyncFoldersAsync();

            Assert.Single(restoredFolders);
            Assert.Equal(Path.GetFullPath(localFolder), restoredFolders[0].LocalPath);
            Assert.Equal("Trabalho", restoredFolders[0].CustomAlias);
            Assert.Equal("#10B981", restoredFolders[0].CustomColorHex);
            Assert.Equal(SyncMode.OneWayBackup, restoredFolders[0].Mode);

            // Disconnect WITH deleting folder configuration
            await syncService.DisconnectAccountAsync(connected2.Id, deleteFolderConfiguration: true);
            var remainingAfterDelete = await repo.GetSyncFoldersAsync();
            Assert.DoesNotContain(remainingAfterDelete, f => f.LocalPath == Path.GetFullPath(localFolder));
            Assert.False(File.Exists(Path.Combine(accountDir, "pastas-sincronizadas.json")));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(accountDir) && Directory.Exists(accountDir))
            {
                Directory.Delete(accountDir, recursive: true);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppsScriptMailerClient_SendsConnectAndRevokePayloadsFromOfficialSender()
    {
        var settings = new TempAppSettingsService();
        settings.Current.MailerWebhookUrl = "https://script.google.com/macros/s/demo-script/exec";
        var captureHandler = new CapturingHttpMessageHandler();
        using var mailer = new AppsScriptMailerClient(settings, NullLogger<AppsScriptMailerClient>.Instance, captureHandler);

        var okConnect = await mailer.TrySendLifecycleEmailAsync("cliente@gmail.com", "Cliente Teste", isConnected: true, language: "pt-BR");
        var okRevoke = await mailer.TrySendLifecycleEmailAsync("cliente@gmail.com", "Cliente Teste", isConnected: false, language: "pt-BR");

        Assert.True(okConnect);
        Assert.True(okRevoke);
        Assert.Equal(2, captureHandler.Requests.Count);
        Assert.Contains("\"eventType\":\"connect\"", captureHandler.Requests[0]);
        Assert.Contains($"\"senderEmail\":\"{AppsScriptMailerClient.OfficialSenderEmail}\"", captureHandler.Requests[0]);
        Assert.Contains("\"recipientEmail\":\"cliente@gmail.com\"", captureHandler.Requests[0]);
        Assert.Contains("\"eventType\":\"revoke\"", captureHandler.Requests[1]);
        Assert.Contains($"\"senderEmail\":\"{AppsScriptMailerClient.OfficialSenderEmail}\"", captureHandler.Requests[1]);
    }

    [Fact]
    public async Task DriveSyncService_ConnectReconnectAndDisconnect_SendLifecycleEmailsViaTundraMailer()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-mailer-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        var email1 = $"mail1-{Guid.NewGuid():N}@example.com";
        var email2 = $"mail2-{Guid.NewGuid():N}@example.com";
        Directory.CreateDirectory(tempRoot);

        var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        string? dir1 = null;
        string? dir2 = null;

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, MockAccountFlowDriveClientProxy>();
            var clientState = (MockAccountFlowDriveClientProxy)(object)mockClient;
            var mailerSpy = new RecordingTundraMailerClient();

            var settings = new TempAppSettingsService();
            settings.Current.GmailAccountAlertsEnabled = true;

            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                mailerSpy);

            clientState.Email = email1;
            var acc1 = await syncService.ConnectAccountAsync(addNewAccount: false);
            dir1 = auth.GetAccountFolderPath(acc1.Id, email1);

            await WaitForMailerCountAsync(mailerSpy, 1);
            Assert.Equal((email1, true), (mailerSpy.Calls[0].Email, mailerSpy.Calls[0].IsConnected));

            await syncService.ReconnectAccountAsync(acc1.Id);
            await WaitForMailerCountAsync(mailerSpy, 2);
            Assert.Equal((email1, true), (mailerSpy.Calls[1].Email, mailerSpy.Calls[1].IsConnected));

            clientState.Email = email2;
            clientState.ForcedNextAccountId = "acc_second_mailer";
            var acc2 = await syncService.ConnectAccountAsync(addNewAccount: true);
            dir2 = auth.GetAccountFolderPath(acc2.Id, email2);

            await WaitForMailerCountAsync(mailerSpy, 3);
            Assert.Equal((email2, true), (mailerSpy.Calls[2].Email, mailerSpy.Calls[2].IsConnected));

            await syncService.SwitchAccountAsync(acc1.Id);
            await syncService.DisconnectAccountAsync(acc2.Id);
            await WaitForMailerCountAsync(mailerSpy, 4);
            Assert.Equal((email2, false), (mailerSpy.Calls[3].Email, mailerSpy.Calls[3].IsConnected));

            await syncService.DisconnectAccountAsync(acc1.Id);
            await WaitForMailerCountAsync(mailerSpy, 5);
            Assert.Equal((email1, false), (mailerSpy.Calls[4].Email, mailerSpy.Calls[4].IsConnected));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(dir1) && Directory.Exists(dir1))
            {
                Directory.Delete(dir1, recursive: true);
            }

            if (!string.IsNullOrWhiteSpace(dir2) && Directory.Exists(dir2))
            {
                Directory.Delete(dir2, recursive: true);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AboutViewModel_SupportSelector_SanitizesOptionalDiagnostics_AndEnforcesUrlLimit()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-support-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            for (var i = 0; i < 35; i++)
            {
                await repo.AddActivityLogAsync(new SyncActivityLog
                {
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
                    Severity = i % 2 == 0 ? ActivitySeverity.Error : ActivitySeverity.Warning,
                    Action = "UploadFailed",
                    TargetPath = $"/home/gustavo/SecretDocs/item-{i}.pdf",
                    Message = $"Falha #{i:D2} para privateuser{i}@gmail.com em /home/gustavo/Documentos/segredo-{i}.txt com detalhe extenso para testar corte seguro da URL"
                });
            }

            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");
            var aboutVm = new AboutViewModel(loc, repository: repo);

            Uri? openedUri = null;
            string? copiedText = null;
            aboutVm.OpenExternalUriCallback = uri =>
            {
                openedUri = uri;
                return Task.CompletedTask;
            };
            aboutVm.CopyTextCallback = text =>
            {
                copiedText = text;
                return Task.CompletedTask;
            };

            // Diagnostics are opt-in by default and bug labels are used
            Assert.False(aboutVm.IncludeDiagnostics);
            Assert.False(aboutVm.IsSuggestionMode);
            Assert.Equal(loc.BugDescriptionLabel, aboutVm.FeedbackDescriptionLabel);

            aboutVm.FeedbackTitle = "Pasta travada";
            aboutVm.FeedbackDescription = "Teste reproduzível";
            await aboutVm.SubmitFeedbackGitHubCommand.ExecuteAsync(null);
            Assert.NotNull(openedUri);
            Assert.Contains("labels=bug", openedUri!.Query);
            Assert.DoesNotContain("Diagn%C3%B3stico", openedUri.Query);

            // Switch to Suggestion mode updates label, hides preview, and never sends diagnostics
            aboutVm.IncludeDiagnostics = true;
            await aboutVm.ToggleDiagnosticsPreviewCommand.ExecuteAsync(null);
            Assert.True(aboutVm.IsDiagnosticsPreviewOpen);

            aboutVm.SelectSuggestionModeCommand.Execute(null);
            Assert.True(aboutVm.IsSuggestionMode);
            Assert.False(aboutVm.IsDiagnosticsPreviewOpen);
            Assert.Equal(loc.SuggestionDescriptionLabel, aboutVm.FeedbackDescriptionLabel);
            await aboutVm.SubmitFeedbackGitHubCommand.ExecuteAsync(null);
            Assert.Contains("labels=enhancement", openedUri.Query);
            Assert.DoesNotContain("Diagn%C3%B3stico", openedUri.Query);

            // Switch back to Bug, open preview and opt into diagnostics
            aboutVm.SelectBugModeCommand.Execute(null);
            aboutVm.IncludeDiagnostics = true;
            await aboutVm.ToggleDiagnosticsPreviewCommand.ExecuteAsync(null);
            Assert.True(aboutVm.IsDiagnosticsPreviewOpen);
            Assert.Contains("[email]", aboutVm.DiagnosticsPreviewText);
            Assert.Contains("/home/[user]", aboutVm.DiagnosticsPreviewText);
            Assert.DoesNotContain("privateuser0@gmail.com", aboutVm.DiagnosticsPreviewText);
            Assert.DoesNotContain("/home/gustavo", aboutVm.DiagnosticsPreviewText);

            // URL enforces safe length while Copy keeps full log details
            aboutVm.FeedbackDescription = new string('A', 4500);
            await aboutVm.SubmitFeedbackGitHubCommand.ExecuteAsync(null);
            Assert.True(openedUri.AbsoluteUri.Length <= AboutViewModel.MaxGitHubIssueUrlLength);

            aboutVm.FeedbackDescription = "Descrição curta";
            await aboutVm.CopyFeedbackCommand.ExecuteAsync(null);
            Assert.NotNull(copiedText);
            Assert.Contains("Falha #34", copiedText);
            Assert.DoesNotContain("privateuser34@gmail.com", copiedText);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task WaitForMailerCountAsync(RecordingTundraMailerClient spy, int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && spy.Calls.Count < expectedCount)
        {
            await Task.Delay(25);
        }

        Assert.True(spy.Calls.Count >= expectedCount, $"Expected at least {expectedCount} mailer calls, got {spy.Calls.Count}.");
    }

    private sealed class RecordingTundraMailerClient : ITundraMailerClient
    {
        public List<(string Email, string? Name, bool IsConnected, string Language)> Calls { get; } = [];

        public Task<bool> TrySendLifecycleEmailAsync(
            string recipientEmail,
            string? recipientName,
            bool isConnected,
            string language = "pt-BR",
            CancellationToken cancellationToken = default)
        {
            Calls.Add((recipientEmail, recipientName, isConnected, language));
            return Task.FromResult(true);
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            Requests.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private class MockAccountFlowDriveClientProxy : DispatchProxy
    {
        private bool _connected;
        private string _activeKey = "default";
        private readonly Dictionary<string, string> _emailsByKey = new(StringComparer.Ordinal);
        public string Email { get; set; } = "user@example.com";
        public string? ForcedNextAccountId { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return _connected;
                case "get_ActiveAccountKey":
                    return _activeKey;
                case nameof(IGoogleDriveClient.ConnectInteractiveAsync):
                {
                    _connected = true;
                    _activeKey = !string.IsNullOrWhiteSpace(ForcedNextAccountId)
                        ? ForcedNextAccountId!
                        : (string)args![0]!;
                    _emailsByKey[_activeKey] = Email;
                    return Task.FromResult(new DriveAccount
                    {
                        Id = _activeKey,
                        DisplayName = "User",
                        EmailAddress = Email,
                        IsConnected = true
                    });
                }
                case nameof(IGoogleDriveClient.SwitchSessionAsync):
                {
                    _connected = true;
                    _activeKey = (string)args![0]!;
                    var email = _emailsByKey.TryGetValue(_activeKey, out var known) ? known : Email;
                    return Task.FromResult<DriveAccount?>(new DriveAccount
                    {
                        Id = _activeKey,
                        DisplayName = "User",
                        EmailAddress = email,
                        IsConnected = true
                    });
                }
                case nameof(IGoogleDriveClient.DisconnectAsync):
                    _connected = false;
                    return Task.CompletedTask;
                case nameof(IGoogleDriveClient.ListChildrenAsync):
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(Array.Empty<RemoteDriveNode>());
                case nameof(IGoogleDriveClient.GetAccountInfoAsync):
                {
                    var email = _emailsByKey.TryGetValue(_activeKey, out var known) ? known : Email;
                    return Task.FromResult(new DriveAccount
                    {
                        Id = _activeKey,
                        DisplayName = "User",
                        EmailAddress = email,
                        IsConnected = true
                    });
                }
                default:
                    return null;
            }
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network unavailable");
    }

    private sealed class TempAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task SaveSilentlyAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private class MockSyncDriveClientProxy : DispatchProxy
    {
        public List<string> CreatedFolderNames { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return true;
                case "get_ActiveAccountKey":
                    return "default";
                case nameof(IGoogleDriveClient.ListChildrenAsync):
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(Array.Empty<RemoteDriveNode>());
                case nameof(IGoogleDriveClient.CreateFolderAsync):
                case nameof(IGoogleDriveClient.FindOrCreateFolderAsync):
                {
                    var name = (string)args![0]!;
                    CreatedFolderNames.Add(name);
                    return Task.FromResult(new RemoteDriveNode
                    {
                        Id = $"drv-{Guid.NewGuid():N}",
                        Name = name,
                        IsFolder = true
                    });
                }
                case nameof(IGoogleDriveClient.GetAccountInfoAsync):
                    return Task.FromResult(new DriveAccount
                    {
                        Id = "default",
                        IsConnected = true,
                        EmailAddress = "test@example.com"
                    });
                default:
                    return null;
            }
        }
    }

    [Fact]
    public async Task FileHubViewModel_CustomizesFolderColorAndSymbolViaContextAction()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");
        var settings = new TempAppSettingsService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();

        var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc, settings);
        var folderItem = new FileHubItemViewModel
        {
            Name = "Projetos",
            OriginalFolderName = "Projetos",
            IsFolder = true,
            RemoteFileId = "drv-proj-9",
            AppearanceKey = "drive:drv-proj-9"
        };

        hub.UpdateSelectedItems([folderItem]);
        hub.SelectedItem = folderItem;
        Assert.True(hub.CanCustomizeSelectedFolder);

        hub.OpenCustomizeFolderCommand.Execute(null);
        Assert.True(hub.IsCustomizeFolderOpen);

        hub.CustomizeEditAlias = "Projetos Ativos";
        hub.CustomizeColorChoices.First(c => c.Hex == "#10B981").SelectCommand.Execute(null);
        hub.CustomizeIconChoices.First(i => i.Key == "star").SelectCommand.Execute(null);

        await hub.SaveFolderCustomizationCommand.ExecuteAsync(null);
        Assert.False(hub.IsCustomizeFolderOpen);
        Assert.True(settings.Current.FolderAppearances.TryGetValue("drive:drv-proj-9", out var saved));
        Assert.Equal("Projetos Ativos", saved!.Alias);
        Assert.Equal("#10B981", saved.ColorHex);
        Assert.Equal("star", saved.IconKey);
    }
}
