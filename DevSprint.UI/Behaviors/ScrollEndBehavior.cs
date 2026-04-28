using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DevSprint.UI.Behaviors;

public static class ScrollEndBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(ScrollEndBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand GetCommand(DependencyObject obj) => (ICommand)obj.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject obj, ICommand value) => obj.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        if (e.OldValue is not null)
            sv.ScrollChanged -= OnScrollChanged;

        if (e.NewValue is not null)
            sv.ScrollChanged += OnScrollChanged;
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.VerticalOffset < sv.ScrollableHeight - 50) return;

        var command = GetCommand(sv);
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }
}
