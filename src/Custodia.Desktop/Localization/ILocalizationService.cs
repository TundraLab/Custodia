using System.ComponentModel;

namespace TundraDrive.Desktop.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    bool IsEnglish { get; }
    event EventHandler<string>? LanguageChanged;

    void SetLanguage(string languageCode);
    string FormatRelativeTimestamp(DateTimeOffset? utcTimestamp);
    string Text(string key);
    string Format(string key, params object[] args);

    string WindowTitle { get; }
    string BrandSubtitle { get; }
    string NavAccounts { get; }
    string NavFileHub { get; }
    string NavSyncFolders { get; }
    string NavTransferQueue { get; }
    string NavActivityLogs { get; }
    string NavSettings { get; }
    string NavAbout { get; }

    string AccountsTitle { get; }
    string ConnectGoogleButton { get; }
    string CancelAuthButton { get; }
    string ImportCredentialsButton { get; }
    string RefreshQuotaButton { get; }
    string DisconnectButton { get; }
    string StorageLabel { get; }
    string BuyMoreStorageButton { get; }
    string BuyMoreShortButton { get; }
    string OpenInDriveButton { get; }
    string StorageDriveLegend { get; }
    string StorageOtherLegend { get; }
    string StorageTrashLegend { get; }
    string ImportJsonShortButton { get; }
    string ConnectShortButton { get; }
    string AddAccountShortButton { get; }
    string AddAccountMenu { get; }
    string AddAccountPromptTitle { get; }
    string AddAccountUseCurrentJson { get; }
    string AddAccountImportNewJson { get; }
    string SwitchAccountLabel { get; }
    string OnboardingTitle { get; }
    string OnboardingStep1Title { get; }
    string OnboardingStep1Desc { get; }
    string OpenEnableDriveApiButton { get; }
    string OpenCloudConsoleButton { get; }
    string OnboardingStep2Title { get; }
    string OnboardingStep2Desc { get; }
    string OnboardingStep3Title { get; }
    string OnboardingStep3Desc { get; }
    string SecuritySectionTitle { get; }
    string CredentialsFileLabel { get; }
    string TokensDirLabel { get; }
    string SafeDeletionNotice { get; }

    string SyncFoldersTitle { get; }
    string FileHubTitle { get; }
    string SearchFilesWatermark { get; }
    string UploadFileButton { get; }
    string LayoutListButton { get; }
    string LayoutGridButton { get; }
    string DownloadButton { get; }
    string PreviewButton { get; }
    string FileHubOpenLocal { get; }
    string BackButton { get; }
    string TrashTabButton { get; }
    string MoveToTrashButton { get; }
    string RestoreFromTrashButton { get; }
    string DeletePermanentlyButton { get; }
    string EmptyTrashButton { get; }
    string SelectAllButton { get; }
    string ClearSelectionButton { get; }
    string EmptyTrashConfirmTitle { get; }
    string ConfirmButton { get; }
    string CopyNameMenu { get; }
    string CopyDriveLinkMenu { get; }
    string OpenLocalFolderMenu { get; }
    string CopyMessageMenu { get; }
    string CopyTargetPathMenu { get; }
    string SyncThisFolderMenu { get; }
    string CopyEmailMenu { get; }
    string NoTransfersHint { get; }
    string NoLogsHint { get; }
    string PreviewTitle { get; }
    string PreviewPlayAudio { get; }
    string PreviewStopAudio { get; }
    string PreviewPlayVideo { get; }
    string PreviewPrevPage { get; }
    string PreviewNextPage { get; }
    string FileHubEmptyFolder { get; }
    string ColOrigin { get; }
    string FileHubFilterDrive { get; }
    string FileHubFilterLocal { get; }
    string OpenTrashTooltip { get; }
    string TrashBannerTitle { get; }
    string MappedFoldersButton { get; }
    string RemoteBrowserButton { get; }
    string MapLocalFolderButton { get; }
    string SyncAllButton { get; }
    string SyncNowButton { get; }
    string RemoveButton { get; }
    string EmptyFoldersHint { get; }
    string AllFoldersPausedHint { get; }
    string SyncOriginLocalLabel { get; }
    string SyncDestinationCloudLabel { get; }

    string MyDriveLabel { get; }
    string SearchRemoteWatermark { get; }
    string SearchButton { get; }
    string SyncSelectedFolderButton { get; }
    string RefreshButton { get; }
    string ColName { get; }
    string ColType { get; }
    string ColSize { get; }
    string ColModified { get; }
    string ColAction { get; }
    string OpenFolderButton { get; }

    string TransferQueueTitle { get; }
    string ClearCompletedButton { get; }
    string CancelButton { get; }
    string TransferDetailLocalPath { get; }
    string TransferDetailRelativePath { get; }
    string TransferDetailRemoteId { get; }
    string TransferDetailStarted { get; }
    string TransferDetailCompleted { get; }
    string TransferDetailDuration { get; }
    string TransferDetailAvgSpeed { get; }
    string TransferDetailError { get; }
    string TransferCopyPathButton { get; }
    string TransferCopyDriveIdButton { get; }

    string ActivityLogsTitle { get; }
    string ClearLogsButton { get; }
    string FilterLogsWatermark { get; }
    string ApplyFilterButton { get; }
    string ColTimestamp { get; }
    string ColLevel { get; }
    string ColEvent { get; }
    string ColDescription { get; }
    string ColTarget { get; }

    string SettingsTitle { get; }
    string ReloadButton { get; }
    string ReloadingButton { get; }
    string ReloadedButton { get; }
    string SaveChangesButton { get; }
    string SavingButton { get; }
    string SavedButton { get; }
    string GeneralSectionTitle { get; }
    string LanguageLabel { get; }
    string LanguageHint { get; }
    string UiScaleLabel { get; }
    string UiScaleHint { get; }
    string DownloadDirectoryLabel { get; }
    string DownloadDirectoryHint { get; }
    string BrowseFolderButton { get; }
    string StartOnBootCheckbox { get; }
    string StartOnBootHint { get; }
    string NotificationsEnabledCheckbox { get; }
    string MinimizeToTrayCheckbox { get; }
    string TestNotificationButton { get; }
    string AutoMemoryCleanupLabel { get; }
    string AutoMemoryCleanupHint { get; }
    string MemoryCleanupIntervalLabel { get; }
    string CleanMemoryNowButton { get; }
    string SyncEngineSectionTitle { get; }
    string SyncIntervalLabel { get; }
    string SyncIntervalHint { get; }
    string CredentialsAccountsSectionTitle { get; }
    string CredentialsAccountsSubtitle { get; }
    string RevokeJsonOnlyButton { get; }
    string RevokeAccountButton { get; }
    string SwitchToAccountButton { get; }
    string ConnectedAccountsEmptyHint { get; }
    string ConflictPolicyLabel { get; }
    string ConflictPolicyHint { get; }
    string WatchLocalChangesCheckbox { get; }
    string IgnorePatternsLabel { get; }
    string LocalApiSectionTitle { get; }
    string EnableLocalApiCheckbox { get; }
    string LocalApiPortLabel { get; }
    string LocalApiPortHint { get; }
    string CurrentApiStatusLabel { get; }
    string OpenApiDocsButton { get; }
    string OpenApiSpecButton { get; }
    string SystemPathsSectionTitle { get; }
    string AboutSectionTitle { get; }
    string AboutDescription { get; }
    string AboutCompanyLabel { get; }
    string AboutProductLabel { get; }
    string AboutDeveloperLabel { get; }
    string AboutVersionLabel { get; }
    string AboutTechStack { get; }

    string AutoCheckUpdatesCheckbox { get; }
    string AutoCheckUpdatesHint { get; }
    string UpdateFrequencyLabel { get; }
    string UpdatesSectionTitle { get; }
    string UpdateCheckButton { get; }
    string UpdateCheckingButton { get; }
    string UpdateInstallNowButton { get; }
    string UpdateOpenReleaseButton { get; }
    string UpdateBannerActionButton { get; }

    string SupportSectionTitle { get; }
    string SupportSectionSubtitle { get; }
    string ReportBugButton { get; }
    string SubmitSuggestionButton { get; }
    string BugTitleLabel { get; }
    string BugTitleWatermark { get; }
    string BugDescriptionLabel { get; }
    string BugDescriptionWatermark { get; }
    string BugIncludeDiagnosticsCheckbox { get; }
    string OpenGitHubIssueButton { get; }
    string CopyReportButton { get; }
    string SuggestionCategoryLabel { get; }
    string SuggestionTitleLabel { get; }
    string SuggestionTitleWatermark { get; }
    string SuggestionDescriptionLabel { get; }
    string SuggestionDescriptionWatermark { get; }

    string NotificationSoundCheckbox { get; }
    string NotificationSoundToneLabel { get; }
    string PreviewSoundButton { get; }
    string GmailAccountAlertsCheckbox { get; }
    string GmailAccountAlertsHint { get; }
    string OpenEnableGmailApiButton { get; }

    string NewFolderButton { get; }
    string SyncLocalIntoHereButton { get; }
    string NewFolderPlaceholder { get; }
    string CreateFolderConfirmButton { get; }

    string CustomizeFolderButton { get; }
    string CustomizeAliasLabel { get; }
    string CustomizeAliasWatermark { get; }
    string CustomizeColorLabel { get; }
    string CustomizeIconLabel { get; }
    string SaveCustomizationButton { get; }
    string ResetCustomizationButton { get; }

    string CredentialsDragDropHint { get; }
    string DetectedDownloadsTitle { get; }
    string UseDetectedJsonButton { get; }
    string OpenAccountsFolderButton { get; }

    string DeleteAccountFoldersCheckbox { get; }
    string ConfirmRemoveAccountButton { get; }
    string SidebarDisconnectPromptTitle { get; }
    string SidebarDeleteFoldersCheckbox { get; }
}
