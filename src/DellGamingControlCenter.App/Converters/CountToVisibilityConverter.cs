using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DellGamingControlCenter.App.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Default (no parameter): visible only while the count is 0 — empty-state messaging.
    /// ConverterParameter="NonEmpty" flips it for content that must only render once
    /// there is something to show (e.g. the benchmark history table, whose light
    /// default chrome used to show as a bare white bar over the dark theme).
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool? isEmpty = value switch
        {
            int count => count == 0,
            ICollection collection => collection.Count == 0,
            _ => null
        };

        if (isEmpty is null) return Visibility.Collapsed;

        var showWhenEmpty = !string.Equals(parameter as string, "NonEmpty", StringComparison.OrdinalIgnoreCase);
        return (isEmpty.Value == showWhenEmpty) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
