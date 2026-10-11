namespace DellGamingControlCenter.App.Controls;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DellGamingControlCenter.Core.Models;

/// <summary>
/// Minimal rolling-history utilization graph (no charting framework).
/// Renders the bounded <see cref="TelemetryHistory"/> buffer as an area+line.
/// Null samples render as a gap — never as 0%. Redraws only when the history
/// ticks (2 s cadence from the authoritative sampler); allocates nothing per tick
/// beyond the render geometry for ≤30 points. Fully static when telemetry stops.
/// </summary>
public sealed class UsageGraph : FrameworkElement
{
    private readonly double?[] _samples = new double?[64];
    private TelemetryHistory? _history;

    public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
        nameof(History), typeof(TelemetryHistory), typeof(UsageGraph),
        new PropertyMetadata(null, OnHistoryChanged));

    public TelemetryHistory? History
    {
        get => (TelemetryHistory?)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(UsageGraph),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6))));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public UsageGraph()
    {
        Loaded += (_, _) => Attach(History);
        Unloaded += (_, _) => Attach(null);
    }

    private static void OnHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (UsageGraph)d;
        g.Attach(g.IsLoaded ? (TelemetryHistory?)e.NewValue : null);
        // Remember the pending history so Attach can pick it up when Loaded fires.
        if (!g.IsLoaded && e.NewValue is TelemetryHistory pending)
        {
            g._pending = pending;
        }
        g.InvalidateVisual();
    }

    private TelemetryHistory? _pending;

    private void Attach(TelemetryHistory? history)
    {
        if (_history != null)
        {
            _history.Updated -= OnUpdated;
        }
        _history = history;
        if (_history != null)
        {
            _history.Updated += OnUpdated;
        }
    }

    private void OnUpdated()
    {
        // Sampler thread → UI thread. BeginInvoke: no blocking, coalesced by the dispatcher.
        Dispatcher.BeginInvoke(new Action(InvalidateVisual),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Backdrop
        dc.DrawRectangle(BackdropBrush, null, new Rect(0, 0, w, h));

        // Grid lines at 25/50/75%
        var gridPen = GridPen;
        for (int i = 1; i <= 3; i++)
        {
            var y = h * i / 4.0;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        var history = History ?? _pending;
        if (history == null) return;

        int n = history.CopyTo(_samples);
        if (n < 2)
        {
            DrawStatus(dc, w, h, "Waiting for telemetry…");
            return;
        }

        bool anyValue = false;
        for (int i = 0; i < n; i++) { if (_samples[i].HasValue) { anyValue = true; break; } }
        if (!anyValue)
        {
            DrawStatus(dc, w, h, "Utilization unavailable");
            return;
        }

        double stepX = w / (n - 1);
        var stroke = Stroke;

        // Build the polyline; a null sample breaks the line into segments.
        var figureGeo = new StreamGeometry();
        bool lastWasGap = true;
        using (var ctx = figureGeo.Open())
        {
            for (int i = 0; i < n; i++)
            {
                var v = _samples[i];
                if (!v.HasValue) { lastWasGap = true; continue; }
                var p = new Point(i * stepX, h - (Math.Clamp(v.Value, 0, 100) / 100.0) * (h - 2) - 1);
                if (lastWasGap)
                {
                    ctx.BeginFigure(p, isFilled: false, isClosed: false);
                    lastWasGap = false;
                }
                else
                {
                    ctx.LineTo(p, isStroked: true, isSmoothJoin: false);
                }
            }
        }
        figureGeo.Freeze();
        dc.DrawGeometry(null, LinePen, figureGeo);
    }

    private void DrawStatus(DrawingContext dc, double w, double h, string text)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface, 10, StatusBrush, 1.25);
        dc.DrawText(ft, new Point(6, h / 2 - ft.Height / 2));
    }

    private static readonly Typeface Typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush BackdropBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x14, 0x17, 0x21));
    private static readonly Brush StatusBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xA2, 0xB8));
    private static readonly Pen GridPen = CreatePen(0x33, 0x26, 0x2C, 0x3E, 1);
    private static readonly Pen LinePen = CreatePen(0xFF, 0x8B, 0x5C, 0xF6, 1.6);

    private static Pen CreatePen(byte a, byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(a, r, g, b)), thickness);
        pen.Freeze();
        return pen;
    }
}
