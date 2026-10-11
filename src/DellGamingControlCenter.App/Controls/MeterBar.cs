namespace DellGamingControlCenter.App.Controls;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// Horizontal clay meter (rounded track + fill) used for sessions, storage,
/// memory and capability meters. Contract: <see cref="Value"/> is 0–100;
/// negative renders a neutral "no data" track (no fabricated fill).
/// </summary>
public sealed class MeterBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MeterBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x20, 0x26, 0x38)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
        nameof(BarHeight), typeof(double), typeof(MeterBar),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        return new Size(w, BarHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = BarHeight;
        if (w <= 0 || h <= 0) return;

        double radius = h / 2;
        var trackRect = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(TrackBrush, null, trackRect, radius, radius);

        bool unavailable = double.IsNaN(Value) || Value < 0;
        if (!unavailable)
        {
            double fillW = w * (Math.Clamp(Value, 0, 100) / 100.0);
            if (fillW >= h) // rounded fill needs at least a pill
            {
                dc.DrawRoundedRectangle(FillBrush, null, new Rect(0, 0, fillW, h), radius, radius);
            }
            else if (fillW > 1)
            {
                dc.DrawRoundedRectangle(FillBrush, null, new Rect(0, 0, fillW, h), fillW / 2, fillW / 2);
            }
        }
    }
}
