using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MDraftingHeper.Core.Hooks;

namespace MDraftingHeper.UI.Views
{
    public sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }

    /// <summary>true → Visible, false → Collapsed.</summary>
    public sealed class VisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Readable labels for hook angles and hook positions in combo boxes.</summary>
    public sealed class LabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case HookAngle a: return (int)a + "°";
                case HookEnds e: return e == HookEnds.Both ? "Both ends" : e == HookEnds.EndA ? "Start A only" : "End B only";
                default: return value?.ToString() ?? "";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
