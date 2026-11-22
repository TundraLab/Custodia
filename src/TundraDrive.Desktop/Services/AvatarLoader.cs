using Avalonia.Media.Imaging;

namespace TundraDrive.Desktop.Services;

public static class AvatarLoader
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static string? _cachedUrl;
    private static Bitmap? _cachedBitmap;

    public static async Task<Bitmap?> LoadAsync(string? photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            return null;
        }

        if (string.Equals(_cachedUrl, photoUrl, StringComparison.Ordinal) && _cachedBitmap is not null)
        {
            return _cachedBitmap;
        }

        try
        {
            var requestUrl = photoUrl
                .Replace("=s96-c", "=s128-c", StringComparison.Ordinal)
                .Replace("=s64-c", "=s128-c", StringComparison.Ordinal);

            var bytes = await Client.GetByteArrayAsync(requestUrl);
            await using var ms = new MemoryStream(bytes);
            var bitmap = Bitmap.DecodeToWidth(ms, 128);
            _cachedUrl = photoUrl;
            _cachedBitmap = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
