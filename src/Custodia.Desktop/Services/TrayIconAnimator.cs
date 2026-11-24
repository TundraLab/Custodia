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
            Interval = TimeSpan.FromMilliseconds(360)
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
            var frames = new WindowIcon[4];

            for (var i = 0; i < 4; i++)
            {
                frames[i] = RenderStateFrame(state, i);
            }

            return frames;
        }
        catch
        {
            return _idleIcon is not null ? [_idleIcon] : [];
        }
    }

    private static WindowIcon RenderStateFrame(TrayVisualState state, int step)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext(clear: true))
        {
            // Pulse radius strongly so active states visibly grow and contract on 22px Linux top bars.
            var pulseScale = step switch
            {
                0 => 0.56,
                1 => 0.78,
                2 => 1.00,
                _ => 0.78
            };

            const double center = 32.0;
            var centerPt = new Point(center, center);
            var radius = 29.0 * pulseScale;

            var badgeColor = state switch
            {
                TrayVisualState.Uploading => Color.Parse("#2563EB"),
                TrayVisualState.Downloading => Color.Parse("#16A34A"),
                TrayVisualState.Deleting => Color.Parse("#DC2626"),
                TrayVisualState.Error => step % 2 == 0 ? Color.Parse("#DC2626") : Color.Parse("#F59E0B"),
                _ => Color.Parse("#0284C7")
            };

            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#090D16")), null, centerPt, radius + 2.2, radius + 2.2);
            ctx.DrawEllipse(new SolidColorBrush(badgeColor), new Pen(Brushes.White, 2.0 * pulseScale), centerPt, radius, radius);

            var pen = new Pen(Brushes.White, Math.Max(4.2, 7.2 * pulseScale), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var unit = 12.0 * pulseScale;

            switch (state)
            {
                case TrayVisualState.Uploading:
                {
                    var dy = (1.5 - step * 1.1) * pulseScale;
                    ctx.DrawLine(pen, new Point(center, center + unit + dy), new Point(center, center - unit + dy));
                    ctx.DrawLine(pen, new Point(center - unit * 0.72, center - unit * 0.22 + dy), new Point(center, center - unit + dy));
                    ctx.DrawLine(pen, new Point(center + unit * 0.72, center - unit * 0.22 + dy), new Point(center, center - unit + dy));
                    break;
                }
                case TrayVisualState.Downloading:
                {
                    var dy = (-1.5 + step * 1.1) * pulseScale;
                    ctx.DrawLine(pen, new Point(center, center - unit + dy), new Point(center, center + unit + dy));
                    ctx.DrawLine(pen, new Point(center - unit * 0.72, center + unit * 0.22 + dy), new Point(center, center + unit + dy));
                    ctx.DrawLine(pen, new Point(center + unit * 0.72, center + unit * 0.22 + dy), new Point(center, center + unit + dy));
                    break;
                }
                case TrayVisualState.Deleting:
                {
                    var cross = unit * 0.78;
                    ctx.DrawLine(pen, new Point(center - cross, center - cross), new Point(center + cross, center + cross));
                    ctx.DrawLine(pen, new Point(center + cross, center - cross), new Point(center - cross, center + cross));
                    break;
                }
                case TrayVisualState.Error:
                {
                    ctx.DrawLine(pen, new Point(center, center - unit * 0.92), new Point(center, center + unit * 0.22));
                    ctx.DrawEllipse(Brushes.White, null, new Point(center, center + unit * 0.78), Math.Max(2.2, 3.4 * pulseScale), Math.Max(2.2, 3.4 * pulseScale));
                    break;
                }
                default:
                {
                    var arcRadius = unit * 0.95;
                    var startAngle = step * (Math.PI / 2.0);
                    DrawSyncArc(ctx, pen, center, arcRadius, startAngle);
                    DrawSyncArc(ctx, pen, center, arcRadius, startAngle + Math.PI);
                    break;
                }
            }
        }

        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    private static void DrawSyncArc(DrawingContext ctx, Pen pen, double center, double arcRadius, double startAngleRad)
    {
        var sweepRad = Math.PI * 0.65;
        var endAngleRad = startAngleRad + sweepRad;

        var startPt = new Point(center + Math.Cos(startAngleRad) * arcRadius, center + Math.Sin(startAngleRad) * arcRadius);
        var endPt = new Point(center + Math.Cos(endAngleRad) * arcRadius, center + Math.Sin(endAngleRad) * arcRadius);

        var arcGeometry = new StreamGeometry();
        using (var geoCtx = arcGeometry.Open())
        {
            geoCtx.BeginFigure(startPt, isFilled: false);
            geoCtx.ArcTo(
                endPt,
                new Size(arcRadius, arcRadius),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise);
        }

        ctx.DrawGeometry(null, pen, arcGeometry);

        var tangentAngle = endAngleRad + Math.PI / 2.0;
        var headLen = arcRadius * 0.45;
        var wing1 = new Point(
            endPt.X - Math.Cos(tangentAngle - 0.55) * headLen,
            endPt.Y - Math.Sin(tangentAngle - 0.55) * headLen);
        var wing2 = new Point(
            endPt.X - Math.Cos(tangentAngle + 0.55) * headLen,
            endPt.Y - Math.Sin(tangentAngle + 0.55) * headLen);
        ctx.DrawLine(pen, endPt, wing1);
        ctx.DrawLine(pen, endPt, wing2);
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
