using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DevSprint.UI.Models;

namespace DevSprint.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToSignalBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush(0x10, 0xA3, 0x10);
    private static readonly SolidColorBrush RedBrush = CreateFrozenBrush(0xE8, 0x1C, 0x23);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? GreenBrush : RedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class BoolToSignalIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "?" : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StateHistoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IList<Models.StateTransition> history || history.Count == 0)
            return string.Empty;

        // Group by ToStatus and sum days to show time per state
        var grouped = history
            .GroupBy(t => t.ToStatus)
            .Select(g => $"{g.Key}: {g.Sum(t => t.DaysInState)}d")
            .ToList();

        return string.Join("  ?  ", grouped);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
