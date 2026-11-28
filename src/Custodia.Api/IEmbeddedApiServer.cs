namespace TundraDrive.Api;

public interface IEmbeddedApiServer : IAsyncDisposable
{
    bool IsRunning { get; }
    string BoundAddress { get; }
    string? LastError { get; }
    string ApiTokenFilePath { get; }
    string GetOrCreateApiToken();
    string RegenerateApiToken();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
