using System.Diagnostics;
using System.Text;

namespace TundraDrive.Desktop.Services;

public sealed record NotificationSoundPreset(string Id, string LabelKey);

public sealed class NotificationSoundService
{
    private const int SampleRate = 44100;

    public static IReadOnlyList<NotificationSoundPreset> Presets { get; } =
    [
        new("chime", "SoundToneChime"),
        new("pop", "SoundTonePop"),
        new("bell", "SoundToneBell"),
        new("drop", "SoundToneDrop"),
        new("pulse", "SoundTonePulse"),
        new("minimal", "SoundToneMinimal")
    ];

    private readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache",
        "custodia",
        "sounds");

    private long _lastPlayedTickMs;
    private static readonly string[] Players = ["paplay", "pw-play", "aplay"];

    public static int GetPresetIndex(string? toneId)
    {
        if (string.IsNullOrWhiteSpace(toneId))
        {
            return 0;
        }

        for (var i = 0; i < Presets.Count; i++)
        {
            if (string.Equals(Presets[i].Id, toneId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    public static string GetPresetIdByIndex(int index) =>
        index >= 0 && index < Presets.Count ? Presets[index].Id : Presets[0].Id;

    public Task PlayToneAsync(
        string? toneId,
        bool isError = false,
        bool bypassDebounce = false,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Task.CompletedTask;
        }

        var now = Environment.TickCount64;
        var prev = Volatile.Read(ref _lastPlayedTickMs);
        if (!bypassDebounce && now - prev < 600)
        {
            return Task.CompletedTask;
        }
        Volatile.Write(ref _lastPlayedTickMs, now);

        var key = isError ? "error" : NormalizeToneId(toneId);
        return Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var wavPath = Path.Combine(_cacheDir, $"{key}-v1.wav");
                if (!File.Exists(wavPath))
                {
                    var wavBytes = BuildWavBytes(key);
                    await File.WriteAllBytesAsync(wavPath, wavBytes, cancellationToken);
                }

                foreach (var player in Players)
                {
                    if (await TryRunPlayerAsync(player, wavPath, cancellationToken))
                    {
                        return;
                    }
                }
            }
            catch
            {
            }
        }, CancellationToken.None);
    }

    public static string NormalizeToneId(string? toneId)
    {
        if (string.IsNullOrWhiteSpace(toneId))
        {
            return "chime";
        }

        var trimmed = toneId.Trim().ToLowerInvariant();
        if (trimmed == "error" || Presets.Any(p => p.Id == trimmed))
        {
            return trimmed;
        }

        return "chime";
    }

    public static byte[] BuildWavBytes(string? toneId)
    {
        var key = NormalizeToneId(toneId);
        var samples = SynthesizeTone(key);
        var dataByteLength = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataByteLength);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataByteLength);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataByteLength);

        foreach (var sample in samples)
        {
            bw.Write(sample);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static short[] SynthesizeTone(string key) => key switch
    {
        "pop" => RenderSequence([(540.0, 0.0, 0.055, 0.35), (860.0, 0.035, 0.075, 0.32)]),
        "bell" => RenderBell(880.0, 0.38, 0.30),
        "drop" => RenderSweep(1050.0, 520.0, 0.14, 0.32),
        "pulse" => RenderSequence([(660.0, 0.0, 0.065, 0.28), (660.0, 0.11, 0.08, 0.28)]),
        "minimal" => RenderSequence([(740.0, 0.0, 0.075, 0.24)]),
        "error" => RenderSequence([(340.0, 0.0, 0.11, 0.34), (255.0, 0.13, 0.18, 0.36)]),
        _ => RenderSequence([(587.33, 0.0, 0.14, 0.26), (880.0, 0.08, 0.20, 0.28)])
    };

    private static short[] RenderSequence((double Freq, double StartSec, double DurSec, double Amp)[] notes)
    {
        var totalSec = notes.Max(n => n.StartSec + n.DurSec) + 0.02;
        var len = (int)(totalSec * SampleRate);
        var buf = new double[len];

        foreach (var note in notes)
        {
            var startSample = (int)(note.StartSec * SampleRate);
            var noteSamples = Math.Max(1, (int)(note.DurSec * SampleRate));
            for (var i = 0; i < noteSamples && startSample + i < len; i++)
            {
                var t = (double)i / SampleRate;
                var env = Math.Min(1.0, i / (0.008 * SampleRate)) * Math.Pow(1.0 - (double)i / noteSamples, 1.4);
                var val = (Math.Sin(2 * Math.PI * note.Freq * t) + 0.22 * Math.Sin(4 * Math.PI * note.Freq * t)) * note.Amp * env;
                buf[startSample + i] += val;
            }
        }

        return ToPcm16(buf);
    }

    private static short[] RenderBell(double freq, double durSec, double amp)
    {
        var len = (int)(durSec * SampleRate);
        var buf = new double[len];
        for (var i = 0; i < len; i++)
        {
            var t = (double)i / SampleRate;
            var attack = Math.Min(1.0, i / (0.004 * SampleRate));
            var decay = Math.Exp(-7.5 * t);
            var wave = Math.Sin(2 * Math.PI * freq * t) + 0.38 * Math.Sin(2 * Math.PI * (freq * 2.0) * t);
            buf[i] = wave * amp * attack * decay;
        }

        return ToPcm16(buf);
    }

    private static short[] RenderSweep(double startFreq, double endFreq, double durSec, double amp)
    {
        var len = (int)(durSec * SampleRate);
        var buf = new double[len];
        var phase = 0.0;
        for (var i = 0; i < len; i++)
        {
            var progress = (double)i / len;
            var freq = startFreq + (endFreq - startFreq) * progress;
            phase += 2 * Math.PI * freq / SampleRate;
            var attack = Math.Min(1.0, i / (0.006 * SampleRate));
            var release = 1.0 - progress;
            buf[i] = Math.Sin(phase) * amp * attack * release;
        }

        return ToPcm16(buf);
    }

    private static short[] ToPcm16(double[] buf)
    {
        var pcm = new short[buf.Length];
        for (var i = 0; i < buf.Length; i++)
        {
            var clamped = Math.Clamp(buf[i], -0.98, 0.98);
            pcm[i] = (short)(clamped * short.MaxValue);
        }

        return pcm;
    }

    private static async Task<bool> TryRunPlayerAsync(string player, string wavPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(player)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(wavPath);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
