using Google.Apis.Util.Store;

namespace TundraDrive.Core.Security;

public sealed class SecureFileDataStore : IDataStore
{
    private readonly string _directoryPath;
    private readonly FileDataStore _innerStore;

    public SecureFileDataStore(string directoryPath)
    {
        _directoryPath = directoryPath;
        PosixPermissionHelper.EnsurePrivateDirectory(_directoryPath);
        _innerStore = new FileDataStore(_directoryPath, fullPath: true);
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(_directoryPath);
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        PosixPermissionHelper.EnsurePrivateDirectory(_directoryPath);
        await _innerStore.StoreAsync(key, value);
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(_directoryPath);
    }

    public async Task DeleteAsync<T>(string key)
    {
        await _innerStore.DeleteAsync<T>(key);
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(_directoryPath);
    }

    public Task<T> GetAsync<T>(string key)
    {
        return _innerStore.GetAsync<T>(key);
    }

    public async Task ClearAsync()
    {
        await _innerStore.ClearAsync();
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(_directoryPath);
    }
}
