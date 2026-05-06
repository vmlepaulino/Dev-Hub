using System.Windows;
using System.Windows.Controls;

namespace DevSprint.UI.Behaviors;

/// <summary>
/// Attached properties that enable two-way binding to <see cref="PasswordBox.Password"/>,
/// which Microsoft deliberately did not expose as a DependencyProperty.
/// </summary>
/// <remarks>
/// <para>
/// Usage in XAML (note both attached properties must be set):
/// <code>
/// &lt;PasswordBox b:PasswordBoxHelper.BindPassword="True"
///              b:PasswordBoxHelper.BoundPassword="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/&gt;
/// </code>
/// </para>
/// <para>
/// We use a small "updating" flag to prevent feedback loops between the
/// PasswordBox.PasswordChanged event and the bound property's setter.
/// </para>
/// </remarks>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d) =>
        (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) =>
        d.SetValue(BoundPasswordProperty, value);

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static bool GetBindPassword(DependencyObject d) =>
        (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool value) =>
        d.SetValue(BindPasswordProperty, value);

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    private static bool GetIsUpdating(DependencyObject d) => (bool)d.GetValue(IsUpdatingProperty);
    private static void SetIsUpdating(DependencyObject d, bool value) => d.SetValue(IsUpdatingProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;
        if (GetIsUpdating(pb)) return; // ignore VM→UI when UI is the source

        pb.Password = e.NewValue as string ?? string.Empty;
    }

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;

        var enabled = (bool)e.NewValue;
        if (enabled)
        {
            pb.PasswordChanged += OnPasswordChanged;
            // Push any initial bound value into the box.
            pb.Password = GetBoundPassword(pb) ?? string.Empty;
        }
        else
        {
            pb.PasswordChanged -= OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;

        SetIsUpdating(pb, true);
        SetBoundPassword(pb, pb.Password);
        SetIsUpdating(pb, false);
    }
}
