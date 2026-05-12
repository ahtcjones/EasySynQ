namespace EasySynQ.Domain.ValueObjects;

/// <summary>
/// A half-open UTC time range marking when a configuration value or entity
/// revision is in effect. Per SPEC §3.7.
/// </summary>
/// <remarks>
/// The range is inclusive on <see cref="EffectiveFromUtc"/> and exclusive on
/// <see cref="EffectiveToUtc"/>. A <see langword="null"/>
/// <see cref="EffectiveToUtc"/> denotes a range that is still in effect
/// (no scheduled end).
/// <para>
/// <see cref="EffectiveFromUtc"/> is permitted to be in the future. Per
/// SPEC §3.7, pre-scheduled configuration changes are a deliberate
/// workflow — for example, pre-creating a tolerance change that takes
/// effect next Monday. The as-of resolver handles not-yet-active versions
/// naturally: <see cref="IsInEffectAt(DateTime)"/> returns
/// <see langword="false"/> for any instant before <see cref="EffectiveFromUtc"/>.
/// </para>
/// <para>
/// Construct via the public constructor, which validates UTC kind and
/// ordering. <c>with</c> expressions bypass the cross-field validation and
/// should not be used to mutate this type; callers wanting a derived range
/// must use the constructor.
/// </para>
/// </remarks>
public sealed record EffectiveDateRange
{
    /// <summary>UTC instant from which the range is in effect (inclusive).</summary>
    public DateTime EffectiveFromUtc { get; init; }

    /// <summary>
    /// UTC instant at which the range stops being in effect (exclusive),
    /// or <see langword="null"/> if the range is still open-ended.
    /// </summary>
    public DateTime? EffectiveToUtc { get; init; }

    /// <summary>
    /// Constructs a validated effective-date range.
    /// </summary>
    /// <param name="effectiveFromUtc">The UTC instant from which the range
    /// is in effect. Must be of <see cref="DateTimeKind.Utc"/>. May be in
    /// the future to support pre-scheduled configuration changes per
    /// SPEC §3.7.</param>
    /// <param name="effectiveToUtc">The UTC instant at which the range stops
    /// being in effect, or <see langword="null"/> for an open-ended range.
    /// When set, must be of <see cref="DateTimeKind.Utc"/> and strictly
    /// later than <paramref name="effectiveFromUtc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any UTC-kind or
    /// ordering constraint is violated.</exception>
    public EffectiveDateRange(DateTime effectiveFromUtc, DateTime? effectiveToUtc)
    {
        if (effectiveFromUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "EffectiveFromUtc must have DateTimeKind.Utc.",
                nameof(effectiveFromUtc));
        }

        if (effectiveToUtc.HasValue)
        {
            if (effectiveToUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "EffectiveToUtc must have DateTimeKind.Utc.",
                    nameof(effectiveToUtc));
            }

            if (effectiveToUtc.Value <= effectiveFromUtc)
            {
                throw new ArgumentException(
                    "EffectiveToUtc must be strictly after EffectiveFromUtc.",
                    nameof(effectiveToUtc));
            }
        }

        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    /// <summary>
    /// Returns <see langword="true"/> if this range is in effect at the
    /// supplied UTC instant. Inclusive on <see cref="EffectiveFromUtc"/>,
    /// exclusive on <see cref="EffectiveToUtc"/>. A <see langword="null"/>
    /// <see cref="EffectiveToUtc"/> is treated as still in effect.
    /// </summary>
    /// <param name="utc">The UTC instant to evaluate.</param>
    /// <returns><see langword="true"/> if <paramref name="utc"/> falls within
    /// the half-open range.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="utc"/>
    /// is not of <see cref="DateTimeKind.Utc"/>.</exception>
    public bool IsInEffectAt(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Instant must have DateTimeKind.Utc.",
                nameof(utc));
        }

        if (utc < EffectiveFromUtc)
        {
            return false;
        }

        if (EffectiveToUtc.HasValue && utc >= EffectiveToUtc.Value)
        {
            return false;
        }

        return true;
    }
}
