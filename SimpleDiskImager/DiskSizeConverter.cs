using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace SimpleDiskImager
{
    class DiskSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Debug.Assert(targetType == typeof(string));
            double capacity = (double)(long)value;

            string units = "bytes";
            if (capacity > 1024)
            {
                capacity /= 1024;
                units = "KiB";
            }
            if (capacity > 1024)
            {
                capacity /= 1024;
                units = "MiB";
            }
            if (capacity > 1024)
            {
                capacity /= 1024;
                units = "GiB";
            }

            if (Math.Abs(capacity - Math.Round(capacity)) < 0.01)
            {
                return $"{capacity:0} {units}";
            }
            else
            {
                return $"{capacity:0.00} {units}";
            }

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
