using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PlotterControl
{
    /// <summary>
    /// Converts a boolean value to a SolidColorBrush.
    /// True returns Green, False returns Red.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(SolidColorBrush))]
    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Brushes.Green : Brushes.Red;
            }
            return Brushes.Red; // Default to Red if not a boolean
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
