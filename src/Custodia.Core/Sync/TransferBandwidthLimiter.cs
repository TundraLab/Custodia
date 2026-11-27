using System.Diagnostics;

namespace TundraDrive.Core.Sync;

/// <summary>
/// Token-bucket rate limiter shared across concurrent transfers.
/// </summary>
public sealed class TransferBandwidthLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private double _availableTokens;
    private long _lastTimestamp = Stopwatch.GetTimestamp();

    public async Task ThrottleAsync(
        int byteCount,
        long bytesPerSecond,
        CancellationToken cancellationToken = default)
    {
        if (byteCount <= 0 || bytesPerSecond <= 0)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double waitSeconds;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedSeconds = Math.Max(0.0, (double)(now - _lastTimestamp) / Stopwatch.Frequency);
                _lastTimestamp = now;

                // Allow a short burst window of 0.5s so chunked HTTP I/O stays smooth.
                var maxBucket = Math.Max((double)bytesPerSecond * 0.5, byteCount);
                _availableTokens = Math.Min(maxBucket, _availableTokens + (elapsedSeconds * bytesPerSecond));

                if (_availableTokens >= byteCount)
                {
                    _availableTokens -= byteCount;
                    return;
                }

                var deficit = byteCount - _availableTokens;
                waitSeconds = deficit / bytesPerSecond;
            }
            finally
            {
                _gate.Release();
            }

            var delayMs = Math.Clamp((int)Math.Ceiling(waitSeconds * 1000.0), 10, 5000);
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
