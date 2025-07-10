using System;
using System.Globalization;
using System.Windows.Data;

namespace PRNPusher
{
    public class GreaterThanZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
                return i > 0;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not used for TwoWay binding
            throw new NotImplementedException();
        }
    }
}
