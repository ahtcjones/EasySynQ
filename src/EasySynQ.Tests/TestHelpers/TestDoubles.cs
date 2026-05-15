using EasySynQ.Domain.Common;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Events;
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
/// strings empty, collections empty per the ADR 0007 empty-state
/// contract). Tests set whichever fields they need before exercising
/// the service under test.
/// </summary>
public sealed class MutableCurrentUserAccessor : ICurrentUserAccessor
{
    /// <inheritdoc cref="ICurrentUserAccessor.UserId"/>
    public Guid? UserId { get; set; }

    /// <inheritdoc cref="ICurrentUserAccessor.Username"/>
    public string Username { get; set; } = string.Empty;

    /// <inheritdoc cref="ICurrentUserAccessor.DisplayName"/>
    public string DisplayName { get; set; } = string.Empty;

    /// <inheritdoc cref="ICurrentUserAccessor.Roles"/>
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    /// <inheritdoc cref="ICurrentUserAccessor.Permissions"/>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];
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

/// <summary>
/// Recording <see cref="IDomainEventDispatcher"/> wrapper for tests.
/// Captures every <see cref="Enqueue"/> call so tests can assert "was
/// event X published?" without needing to register a real handler.
/// Forwards Enqueue / DispatchPendingAsync / Clear / HasPending to an
/// inner <see cref="DomainEventDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a singleton in tests vs scoped in production.</b> Tests bind
/// the EF Core <c>DbContextOptions</c> against a prep container that
/// resolves interceptors at options-build time; the lifecycle service
/// resolves the dispatcher from the per-test runtime container. To
/// keep the captured-interceptor and the lifecycle-service references
/// to the SAME dispatcher instance, both containers register this
/// recording wrapper as a singleton with the same instance — see
/// <see cref="EasySynQ.Tests.Integration.Services.ServiceIntegrationTestBase"/>.
/// The queue is shared across scopes within one test, but each test
/// instance creates its own ServiceProvider so dispatchers do not leak
/// across tests. Within a single operation the queue is drained by
/// SaveChanges, mirroring production semantics.
/// </para>
/// </remarks>
public sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly DomainEventDispatcher _inner;

    /// <summary>Every event passed through <see cref="Enqueue"/>, in
    /// enqueue order. Tests inspect this list to assert publication.</summary>
    public List<IDomainEvent> Recorded { get; } = [];

    /// <summary>Construct the recording wrapper over an inner dispatcher
    /// resolving handlers from the supplied service provider.</summary>
    public RecordingDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _inner = new DomainEventDispatcher(serviceProvider);
    }

    /// <inheritdoc />
    public void Enqueue(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        Recorded.Add(domainEvent);
        _inner.Enqueue(domainEvent);
    }

    /// <inheritdoc />
    public bool HasPending => _inner.HasPending;

    /// <inheritdoc />
    public Task DispatchPendingAsync(CancellationToken cancellationToken)
        => _inner.DispatchPendingAsync(cancellationToken);

    /// <inheritdoc />
    public void Clear() => _inner.Clear();
}
