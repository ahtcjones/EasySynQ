namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Resolves the "as-of" instant used when evaluating effective-dated
/// queries (SPEC §3.7). The data layer's global query filter on every
/// <c>IEffectiveDated</c> entity reads from <see cref="AsOfUtc"/> to
/// determine which version of a configuration applies.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation (<c>CurrentTimeTemporalResolver</c>) returns
/// the current clock instant — used for "show me what's in effect now"
/// queries. For historical evaluation, callers can swap in a
/// fixed-instant resolver scoped to the historical event timestamp so
/// queries reflect the configuration that was in effect at that moment.
/// </para>
/// <para>
/// The contract is strict: <see cref="AsOfUtc"/> always returns a
/// <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.
/// </para>
/// </remarks>
public interface ITemporalResolver
{
    /// <summary>The UTC instant to evaluate effective-dating against.</summary>
    DateTime AsOfUtc { get; }
}
