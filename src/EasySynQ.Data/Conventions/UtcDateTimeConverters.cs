using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EasySynQ.Data.Conventions;

/// <summary>
/// Forces <see cref="DateTime"/> values read from SQLite to carry
/// <see cref="DateTimeKind.Utc"/>. SQLite stores no timezone information; EF
/// Core's SQLite provider returns DateTime values with
/// <see cref="DateTimeKind.Unspecified"/>. Without this conversion the
/// domain's UTC-kind invariants — for example,
/// <see cref="EasySynQ.Domain.ValueObjects.EffectiveDateRange"/>'s constructor
/// and every <c>UtcTimestamp</c> validation — would throw during EF
/// materialization.
/// </summary>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>
/// Nullable counterpart to <see cref="UtcDateTimeConverter"/>.
/// </summary>
internal sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter() : base(
        v => v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
