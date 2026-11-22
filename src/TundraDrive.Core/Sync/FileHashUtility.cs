using System.Security.Cryptography;

namespace TundraDrive.Core.Sync;

public sealed record FileHashResult(string Md5Hex, string Sha256Hex);

public static class FileHashUtility
{
    public static async Task<FileHashResult> ComputeHashesAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 81920;
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize,
            useAsync: true);

        using var md5 = MD5.Create();
        using var sha256 = SHA256.Create();

        var buffer = new byte[bufferSize];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        md5.TransformFinalBlock([], 0, 0);
        sha256.TransformFinalBlock([], 0, 0);

        return new FileHashResult(
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant());
    }
}
