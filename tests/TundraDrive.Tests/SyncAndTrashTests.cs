using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;

namespace TundraDrive.Tests;

public sealed class SyncAndTrashTests
{
    [Fact]
    public void AuthCredentialManager_ResolvesPerAccountCredentialsPathWhenPresent()
    {
        var mgr = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        var defaultResolved = mgr.ResolveCredentialsFilePath("acc_nonexistent_999");
        Assert.Equal(mgr.CredentialsFilePath, defaultResolved);

        var perAccountPath = mgr.GetAccountCredentialsFilePath("acc_test_isolated");
        Directory.CreateDirectory(Path.GetDirectoryName(perAccountPath)!);
        try
        {
            File.WriteAllText(perAccountPath, "{}");
            Assert.Equal(perAccountPath, mgr.ResolveCredentialsFilePath("acc_test_isolated"));
            mgr.DeleteAccountCredentialsFile("acc_test_isolated");
            Assert.False(File.Exists(perAccountPath));
        }
        finally
        {
            if (File.Exists(perAccountPath))
            {
                File.Delete(perAccountPath);
            }
        }
    }
    [Theory]
    [InlineData("/home/user/Docs/arquivo com espaço.pdf", "/home/user/Docs/arquivo%20com%20espa%C3%A7o.pdf")]
    [InlineData("/home/user/file#1.txt", "/home/user/file%231.txt")]
    public void EncodeFreeDesktopTrashPath_PreservesSlashesForDolphinAndNautilus(string input, string expected)
    {
        var encoded = DriveSyncService.EncodeFreeDesktopTrashPath(input);
        Assert.Equal(expected, encoded);
        Assert.StartsWith("/", encoded);
        Assert.DoesNotContain("%2F", encoded, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cache/.tundratmp.1234", true)]
    [InlineData("node_modules/pkg/index.js", true)]
    [InlineData("build/tmp.swp", true)]
    [InlineData("documentos/relatorio.pdf", false)]
    public void SyncIgnoreMatcher_RespectsTempAndConfiguredPatterns(string relativePath, bool expectedIgnored)
    {
        var patterns = new[] { "node_modules/*", "*.swp", ".git/*" };
        Assert.Equal(expectedIgnored, SyncIgnoreMatcher.IsIgnored(relativePath, patterns));
    }

    [Theory]
    [InlineData("foto.png", "image/png")]
    [InlineData("documento.pdf", "application/pdf")]
    [InlineData("video.mp4", "video/mp4")]
    public void MimeTypeHelper_ResolvesExpectedTypes(string fileName, string expectedMime)
    {
        Assert.Equal(expectedMime, MimeTypeHelper.GetMimeTypeFromFilePath(fileName));
    }
}
