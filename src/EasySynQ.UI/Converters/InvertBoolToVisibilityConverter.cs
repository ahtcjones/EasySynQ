using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasySynQ.UI.Converters;

/// <summary>
/// Inverse of <see cref="BoolToVisibilityConverter"/>:
/// <see langword="true"/> maps to <see cref="Visibility.Collapsed"/>;
/// anything else (including <see langword="false"/> and
/// <see langword="null"/>) maps to <see cref="Visibility.Visible"/>.
/// Useful when a single bool VM property drives two opposing visual
/// states (e.g., "show spinner while IsLoading; show content
/// otherwise") without forcing the VM to expose a paired Not-flag.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}
