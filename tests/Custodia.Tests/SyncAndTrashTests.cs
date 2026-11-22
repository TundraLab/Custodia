using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Api;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;

namespace TundraDrive.Tests;

public sealed class SyncAndTrashTests
{
    [Theory]
    [InlineData("../../evil.txt", ".._.._evil.txt")]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("a/b\\c\0d.pdf", "a_b_c_d.pdf")]
    public void SanitizeRemoteSegment_PreventsTraversalCharacters(string rawName, string expected)
    {
        Assert.Equal(expected, DriveSyncService.SanitizeRemoteSegment(rawName));
    }

    [Fact]
    public void ResolveSafeLocalPath_BlocksParentDirectoryEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "tundra_root_test");
        Assert.Throws<UnauthorizedAccessException>(() => DriveSyncService.ResolveSafeLocalPath(root, "../../etc/passwd"));

        var safe = DriveSyncService.ResolveSafeLocalPath(root, "sub/doc.pdf");
        Assert.StartsWith(Path.GetFullPath(root), safe);
    }

    [Theory]
    [InlineData("127.0.0.1:5080", null, null, null, true)]
    [InlineData("localhost:5080", "http://127.0.0.1:3000", "http://127.0.0.1:3000/page", "same-origin", true)]
    [InlineData("127.0.0.1:5080", "https://evil.example", null, null, false)]
    [InlineData("127.0.0.1:5080", null, "https://evil.example/csrf.html", null, false)]
    [InlineData("127.0.0.1:5080", null, null, "cross-site", false)]
    [InlineData("evil.example:5080", null, null, null, false)]
    public void EmbeddedApiServer_BlocksCrossOriginCsrfAndDnsRebinding(
        string? host,
        string? origin,
        string? referer,
        string? secFetchSite,
        bool expected)
    {
        Assert.Equal(expected, EmbeddedApiServer.IsAllowedLoopbackRequest(host, origin, referer, secFetchSite));
    }

    [Fact]
    public void EmbeddedApiServer_BlocksProtectedConfigAndShellStartupPaths()
    {
        Assert.False(EmbeddedApiServer.IsSafeLocalApiFilePath(TundraDrive.Core.Configuration.AppPaths.CredentialsFilePath, out _));
        Assert.False(EmbeddedApiServer.IsSafeLocalApiFilePath(TundraDrive.Core.Configuration.AppPaths.DatabaseFilePath, out _));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(EmbeddedApiServer.IsSafeLocalApiFilePath(Path.Combine(home, ".ssh", "authorized_keys"), out _));
        Assert.False(EmbeddedApiServer.IsSafeLocalApiFilePath(Path.Combine(home, ".bashrc"), out _));

        var safeDoc = Path.Combine(Path.GetTempPath(), "relatorio.pdf");
        Assert.True(EmbeddedApiServer.IsSafeLocalApiFilePath(safeDoc, out _));
    }

    [Fact]
    public async Task AuthCredentialManager_RejectsWebTypeOAuthCredentialsJson()
    {
        var mgr = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        var tempJson = Path.Combine(Path.GetTempPath(), $"web-creds-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempJson, """
                {
                  "web": {
                    "client_id": "123.apps.googleusercontent.com",
                    "client_secret": "secret"
                  }
                }
                """);

            await Assert.ThrowsAsync<InvalidDataException>(() => mgr.ImportCredentialsFileAsync(tempJson, "acc_web_reject"));
        }
        finally
        {
            if (File.Exists(tempJson))
            {
                File.Delete(tempJson);
            }
        }
    }
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
