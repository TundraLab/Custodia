using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TundraDrive.Desktop.Services;

public enum TrayVisualState
{
    Idle = 0,
    Syncing = 1,
    Uploading = 2,
    Downloading = 3,
    Deleting = 4,
    Error = 5
}

public sealed class TrayIconAnimator : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<TrayVisualState, WindowIcon[]> _framesByState = new();
    private WindowIcon? _idleIcon;
    private TrayVisualState _currentState = TrayVisualState.Idle;
    private int _frameIndex;
    private bool _disposed;

    public TrayIconAnimator(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _timer.Tick += OnTick;
        InitializeBaseFrames();
    }

    public void SetState(TrayVisualState state)
    {
        if (_disposed || _currentState == state)
        {
            return;
        }

        _currentState = state;
        _frameIndex = 0;

        if (state == TrayVisualState.Idle)
        {
            _timer.Stop();
            ApplyIconSafely(_idleIcon);
            return;
        }

        if (!_framesByState.ContainsKey(state))
        {
            _framesByState[state] = BuildFramesForState(state);
        }

        ApplyCurrentFrame();
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _currentState == TrayVisualState.Idle)
        {
            _timer.Stop();
            return;
        }

        if (_framesByState.TryGetValue(_currentState, out var frames) && frames.Length > 0)
        {
            _frameIndex = (_frameIndex + 1) % frames.Length;
            ApplyCurrentFrame();
        }
    }

    private void ApplyCurrentFrame()
    {
        if (_framesByState.TryGetValue(_currentState, out var frames) && frames.Length > 0)
        {
            ApplyIconSafely(frames[_frameIndex % frames.Length]);
        }
    }

    private void ApplyIconSafely(WindowIcon? icon)
    {
        if (_disposed || icon is null)
        {
            return;
        }

        try
        {
            _trayIcon.Icon = icon;
        }
        catch
        {
        }
    }

    private void InitializeBaseFrames()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Custodia/Assets/custodia-tray.png"));
            _idleIcon = new WindowIcon(stream);
        }
        catch
        {
        }
    }

    private WindowIcon[] BuildFramesForState(TrayVisualState state)
    {
        try
        {
            using var baseStream = AssetLoader.Open(new Uri("avares://Custodia/Assets/custodia-tray.png"));
            using var baseBitmap = new Bitmap(baseStream);
            var frames = new WindowIcon[4];

            for (var i = 0; i < 4; i++)
            {
                frames[i] = RenderOverlayFrame(baseBitmap, state, i);
            }

            return frames;
        }
        catch
        {
            return _idleIcon is not null ? [_idleIcon] : [];
        }
    }

    private static WindowIcon RenderOverlayFrame(Bitmap baseBitmap, TrayVisualState state, int step)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext(clear: true))
        {
            ctx.DrawImage(baseBitmap, new Rect(0, 0, baseBitmap.PixelSize.Width, baseBitmap.PixelSize.Height), new Rect(0, 0, 64, 64));

            var badgeColor = state switch
            {
                TrayVisualState.Uploading => Color.Parse("#3B82F6"),
                TrayVisualState.Downloading => Color.Parse("#2EA043"),
                TrayVisualState.Deleting => Color.Parse("#EF4444"),
                TrayVisualState.Error => step % 2 == 0 ? Color.Parse("#EF4444") : Color.Parse("#F59E0B"),
                _ => Color.Parse("#0EA5E9")
            };

            var center = new Point(47, 47);
            const double radius = 14.0;
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#0B111E")), null, center, radius + 2.0, radius + 2.0);
            ctx.DrawEllipse(new SolidColorBrush(badgeColor), null, center, radius, radius);

            var pen = new Pen(Brushes.White, 2.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            switch (state)
            {
                case TrayVisualState.Uploading:
                {
                    var dy = 2.0 - step * 1.4;
                    ctx.DrawLine(pen, new Point(47, 54 + dy), new Point(47, 40 + dy));
                    ctx.DrawLine(pen, new Point(42, 45 + dy), new Point(47, 40 + dy));
                    ctx.DrawLine(pen, new Point(52, 45 + dy), new Point(47, 40 + dy));
                    break;
                }
                case TrayVisualState.Downloading:
                {
                    var dy = -2.0 + step * 1.4;
                    ctx.DrawLine(pen, new Point(47, 40 + dy), new Point(47, 54 + dy));
                    ctx.DrawLine(pen, new Point(42, 49 + dy), new Point(47, 54 + dy));
                    ctx.DrawLine(pen, new Point(52, 49 + dy), new Point(47, 54 + dy));
                    break;
                }
                case TrayVisualState.Deleting:
                {
                    var scale = step % 2 == 0 ? 4.5 : 3.5;
                    ctx.DrawLine(pen, new Point(47 - scale, 47 - scale), new Point(47 + scale, 47 + scale));
                    ctx.DrawLine(pen, new Point(47 + scale, 47 - scale), new Point(47 - scale, 47 + scale));
                    break;
                }
                case TrayVisualState.Error:
                {
                    ctx.DrawLine(pen, new Point(47, 40), new Point(47, 49));
                    ctx.DrawEllipse(Brushes.White, null, new Point(47, 53.5), 1.6, 1.6);
                    break;
                }
                default:
                {
                    var angleRad = step * (Math.PI / 2.0);
                    var dx = Math.Cos(angleRad) * 6.0;
                    var dy = Math.Sin(angleRad) * 6.0;
                    ctx.DrawLine(pen, new Point(47 - dx, 47 - dy), new Point(47 + dx, 47 + dy));
                    ctx.DrawLine(pen, new Point(47 + dy * 0.7, 47 - dx * 0.7), new Point(47 + dx, 47 + dy));
                    break;
                }
            }
        }

        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
