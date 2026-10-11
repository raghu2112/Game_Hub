using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DellGamingControlCenter.App.Converters
{
    /// <summary>
    /// Converts a string to Visibility: null/empty/whitespace → Collapsed, otherwise Visible.
    /// Used by PerformanceView mode cards to show the warning line only when one exists.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
