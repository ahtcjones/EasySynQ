using System.Globalization;
using System.Windows.Data;

namespace EasySynQ.UI.Converters;

/// <summary>
/// Reference-equality multi-value converter. Compares
/// <c>values[0]</c> against <c>values[1]</c> via
/// <see cref="object.ReferenceEquals(object?, object?)"/> and returns
/// the boolean result. Used to drive the navigation tree's
/// active-row treatment — bind <c>values[0]</c> to the item being
/// rendered and <c>values[1]</c> to the shell's <c>SelectedItem</c>;
/// the converter returns <see langword="true"/> for the active row.
/// </summary>
/// <remarks>
/// Reference equality (not <see cref="object.Equals(object?)"/>) is
/// deliberate. The catalog's singleton <c>NavigationItem</c> instances
/// are the only objects that should ever appear in the selection
/// state; an unexpected match by value-equality would mask a bug
/// rather than expose it.
/// </remarks>
public sealed class ReferenceEqualityMultiConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length >= 2 && ReferenceEquals(values[0], values[1]);

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException(
            $"{nameof(ReferenceEqualityMultiConverter)} is one-way; ConvertBack is unsupported.");
}
