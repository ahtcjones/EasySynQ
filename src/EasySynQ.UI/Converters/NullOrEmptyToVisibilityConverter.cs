using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasySynQ.UI.Converters;

/// <summary>
/// String → <see cref="Visibility"/> converter. A <see langword="null"/>
/// or empty string maps to <see cref="Visibility.Collapsed"/>; any
/// non-empty value maps to <see cref="Visibility.Visible"/>.
/// </summary>
/// <remarks>
/// Round-trip conversion is intentionally unsupported — visibility does
/// not encode enough information to reconstruct the original string.
/// </remarks>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException(
            $"{nameof(NullOrEmptyToVisibilityConverter)} is one-way; ConvertBack is unsupported.");
}
