namespace DellGamingControlCenter.App.Converters;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var inverted = !b;

            // Visibility="{Binding X, Converter={StaticResource InverseBooleanConverter}}"
            // passes typeof(Visibility) as the target type. Returning a bool there makes
            // the binding fail silently and the element keeps its default Visibility
            // (Visible), so an inverse condition could never hide anything: the hardware
            // capabilities rendered both the check and the cross, and the editor showed
            // its "missing executable" warning for files that were selected.
            if (targetType == typeof(Visibility))
                return inverted ? Visibility.Visible : Visibility.Collapsed;

            return inverted;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}
