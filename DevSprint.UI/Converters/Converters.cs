using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x0F, 0x7B, 0x0F));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xD1, 0x34, 0x38));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? GreenBrush : RedBrush;

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

public sealed class SprintBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0xD1, 0xD1, 0xD1));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SprintShadowConverter : IValueConverter
{
    private static readonly DropShadowEffect ActiveShadow = new()
    {
        Color = Color.FromRgb(0x00, 0x78, 0xD4),
        BlurRadius = 12,
        ShadowDepth = 0,
        Opacity = 0.3
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ActiveShadow : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
