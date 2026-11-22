using System.Text.Json;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;

namespace TundraDrive.Core.Configuration;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);

            if (!File.Exists(AppPaths.SettingsFilePath))
            {
                Current = new AppSettings();
                await PersistInternalAsync(Current, cancellationToken);
                return Current;
            }

            await using var stream = File.OpenRead(AppPaths.SettingsFilePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            Current = loaded ?? new AppSettings();
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);
            Current = settings;
            await PersistInternalAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, settings);
    }

    private static async Task PersistInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await using (var stream = File.Create(AppPaths.SettingsFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        PosixPermissionHelper.EnsureFileMode0600(AppPaths.SettingsFilePath);
    }
}
