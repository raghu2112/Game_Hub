namespace DellGamingControlCenter.App.Controls;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// Radial clay gauge (270° arc) for dashboard/diagnostics/hardware meters.
/// Contract: <see cref="Value"/> is 0–100; any negative value renders the honest
/// "unavailable" state (empty track, "N/A" center) — never a fabricated arc.
/// Cheap: OnRender only, no timers, no effects beyond a single pen.
/// </summary>
public sealed class CircularGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CircularGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty CenterTextProperty = DependencyProperty.Register(
        nameof(CenterText), typeof(string), typeof(CircularGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Exact center text (e.g. "23%", "9.8 GB", "N/A"). Auto-derived when null.</summary>
    public string? CenterText
    {
        get => (string?)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CircularGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(CircularGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CircularGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x20, 0x26, 0x38)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    private static readonly Typeface ValueFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private const double StartAngle = 135;   // bottom-left
    private const double SweepAngle = 270;   // open at the bottom

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double thickness = Math.Clamp(Math.Min(w, h) * 0.11, 5, 12);
        double radius = (Math.Min(w, h) - thickness) / 2 - 1;
        var center = new Point(w / 2, h / 2 - (string.IsNullOrEmpty(Label) ? 0 : Math.Min(h * 0.06, 8)));

        bool unavailable = double.IsNaN(Value) || Value < 0;
        double clamped = unavailable ? 0 : Math.Clamp(Value, 0, 100);

        var track = new StreamGeometry();
        using (var ctx = track.Open())
        {
            var start = Polar(center, radius, StartAngle);
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(Polar(center, radius, StartAngle + SweepAngle), new Size(radius, radius), 0,
                SweepAngle > 180, SweepDirection.Clockwise, true, false);
        }
        track.Freeze();
        dc.DrawGeometry(null, new Pen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, track);

        if (clamped > 0.5)
        {
            double sweep = SweepAngle * (clamped / 100.0);
            var valueArc = new StreamGeometry();
            using (var ctx = valueArc.Open())
            {
                var start = Polar(center, radius, StartAngle);
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(Polar(center, radius, StartAngle + sweep), new Size(radius, radius), 0,
                    sweep > 180, SweepDirection.Clockwise, true, false);
            }
            valueArc.Freeze();
            var accent = new Pen(AccentBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, accent, valueArc);
        }

        string centerText = CenterText ?? (unavailable ? "N/A" : $"{Math.Round(clamped)}%");
        var centerBrush = unavailable ? DisallowedBrush : ValueBrush;
        var ft = new FormattedText(centerText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            ValueFace, Math.Clamp(Math.Min(w, h) * 0.24, 14, 34), centerBrush, 1.25);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));

        if (!string.IsNullOrEmpty(Label))
        {
            var lf = new FormattedText(Label!.ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelFace, 10, LabelBrush, 1.25);
            dc.DrawText(lf, new Point(center.X - lf.Width / 2, center.Y + ft.Height / 2 + 4));
        }
    }

    private static Point Polar(Point center, double radius, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private static readonly Brush ValueBrush = Frozen(Color.FromRgb(0xF2, 0xF3, 0xF8));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x9B, 0xA2, 0xB8));
    private static readonly Brush DisallowedBrush = Frozen(Color.FromRgb(0x9B, 0xA2, 0xB8));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
