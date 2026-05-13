using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasySynQ.UI.Converters;

/// <summary>
/// Boolean → <see cref="Visibility"/> converter. <see langword="true"/>
/// maps to <see cref="Visibility.Visible"/>, anything else (including
/// <see langword="false"/> and <see langword="null"/>) maps to
/// <see cref="Visibility.Collapsed"/>.
/// </summary>
/// <remarks>
/// WPF ships its own <c>BooleanToVisibilityConverter</c>, but it sits in
/// <c>System.Windows.Controls</c> with no convenient key. Defining our
/// own here lets us register it in <c>App.xaml</c> under a predictable
/// <c>{StaticResource BoolToVisibility}</c> key and gives us a place to
/// document the round-trip semantics.
/// </remarks>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
