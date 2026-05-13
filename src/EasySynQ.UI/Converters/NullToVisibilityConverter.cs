using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasySynQ.UI.Converters;

/// <summary>
/// Object → <see cref="Visibility"/> converter. <see langword="null"/>
/// maps to <see cref="Visibility.Visible"/>; any non-null value maps to
/// <see cref="Visibility.Collapsed"/>.
/// </summary>
/// <remarks>
/// Inverse polarity to <see cref="NullOrEmptyToVisibilityConverter"/>,
/// which collapses on null. This variant is for "show the empty-state
/// placeholder when the bound object is missing" — the shell's content
/// area uses it to render a hint while no view is loaded.
/// </remarks>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException(
            $"{nameof(NullToVisibilityConverter)} is one-way; ConvertBack is unsupported.");
}
