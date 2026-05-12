using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Time;

namespace EasySynQ.Tests.TestHelpers;

/// <summary>
/// Settable <see cref="IClock"/> for tests. Default value is
/// <see cref="DateTime.UtcNow"/> at construction; tests can mutate
/// <see cref="UtcNow"/> mid-test to simulate the passage of time.
/// </summary>
public sealed class FixedClock : IClock
{
    /// <inheritdoc cref="IClock.UtcNow"/>
    public DateTime UtcNow { get; set; }

    /// <summary>Construct a clock initialized to the supplied instant.</summary>
    public FixedClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }
}

/// <summary>
/// Mutable <see cref="ICurrentUserAccessor"/> for tests. Defaults to
/// unauthenticated (<see cref="UserId"/> = <see langword="null"/>,
/// display name empty).
/// </summary>
public sealed class MutableCurrentUserAccessor : ICurrentUserAccessor
{
    /// <inheritdoc cref="ICurrentUserAccessor.UserId"/>
    public Guid? UserId { get; set; }

    /// <inheritdoc cref="ICurrentUserAccessor.UserDisplayName"/>
    public string UserDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Mutable <see cref="IAuditCorrelationProvider"/> for tests. Defaults to
/// <see langword="null"/> (interceptor will generate a per-save
/// correlation id).
/// </summary>
public sealed class MutableAuditCorrelationProvider : IAuditCorrelationProvider
{
    /// <inheritdoc cref="IAuditCorrelationProvider.CurrentCorrelationId"/>
    public Guid? CurrentCorrelationId { get; set; }
}

/// <summary>
/// Mutable <see cref="ITemporalResolver"/> for tests. Lets a single
/// test instance change the "as of" instant mid-test and observe live
/// effect on query filters.
/// </summary>
public sealed class MutableTemporalResolver : ITemporalResolver
{
    /// <inheritdoc cref="ITemporalResolver.AsOfUtc"/>
    public DateTime AsOfUtc { get; set; }

    /// <summary>Construct a resolver initialized to the supplied instant.</summary>
    public MutableTemporalResolver(DateTime asOfUtc)
    {
        AsOfUtc = asOfUtc;
    }
}
