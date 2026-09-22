using System;
using System.Windows.Controls;
using System.Windows.Data;

namespace RetroBar.Converters
{
    [ValueConversion(typeof(Orientation), typeof(Orientation))]
    public class EdgeOrientationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // value is the hosting taskbar's own Orientation (bound via AncestorType=Window).
            // Must not fall back to the primary Settings.Edge: with multiple taskbars, the
            // tray/task list can be hosted on a taskbar whose edge differs from the primary.
            if (value is Orientation orientation)
            {
                return orientation;
            }

            return Orientation.Horizontal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
