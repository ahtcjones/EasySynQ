using EasySynQ.Services.Time;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Default <see cref="ITemporalResolver"/> implementation that returns the
/// current clock instant. Wires the temporal axis to "now," which is the
/// right answer for the vast majority of queries.
/// </summary>
/// <remarks>
/// Historical-evaluation paths (e.g., "did this job pass under the rules
/// in effect when it was processed?") swap in a fixed-instant resolver
/// scoped to the event timestamp instead of using this default.
/// </remarks>
public sealed class CurrentTimeTemporalResolver : ITemporalResolver
{
    private readonly IClock _clock;

    /// <summary>
    /// Constructs a resolver that reads from the supplied clock.
    /// </summary>
    /// <param name="clock">The clock to read "now" from.</param>
    /// <exception cref="ArgumentNullException">When
    /// <paramref name="clock"/> is <see langword="null"/>.</exception>
    public CurrentTimeTemporalResolver(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <inheritdoc />
    public DateTime AsOfUtc => _clock.UtcNow;
}
