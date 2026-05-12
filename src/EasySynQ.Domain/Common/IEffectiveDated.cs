using EasySynQ.Domain.ValueObjects;

namespace EasySynQ.Domain.Common;

/// <summary>
/// Marks a configuration-bearing entity (or entity revision) whose effective
/// period participates in compliance evaluation. Per SPEC §3.7, historical
/// records are evaluated against the effective version of a configuration at
/// the time of the event, not against the current version.
/// </summary>
/// <remarks>
/// Implementers must expose the <see cref="EffectivePeriod"/> property; the
/// default implementation of <see cref="IsInEffectAt(DateTime)"/> delegates
/// to <see cref="EffectiveDateRange.IsInEffectAt(DateTime)"/>. Override
/// <see cref="IsInEffectAt(DateTime)"/> only when a richer rule is required
/// (for example, an exclusion window inside the broader effective period).
/// </remarks>
public interface IEffectiveDated
{
    /// <summary>The effective period of this entity or entity revision.</summary>
    EffectiveDateRange EffectivePeriod { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this version is in effect at the
    /// supplied UTC instant.
    /// </summary>
    /// <param name="utc">The UTC instant to evaluate against.</param>
    /// <returns><see langword="true"/> if <paramref name="utc"/> falls within
    /// the effective period (inclusive on the start, exclusive on the end).</returns>
    bool IsInEffectAt(DateTime utc) => EffectivePeriod.IsInEffectAt(utc);
}
