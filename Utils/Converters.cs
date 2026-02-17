using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SyncWave.Utils
{
    /// <summary>
    /// Converts a non-empty string to Visible, empty/null to Collapsed.
    /// Used for showing/hiding error message panels.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts an audio level (0.0–1.0) to a pixel width (0–80) for a level meter bar.
    /// </summary>
    public class LevelToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double level)
                return Math.Clamp(level * 80.0, 0, 80);
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
