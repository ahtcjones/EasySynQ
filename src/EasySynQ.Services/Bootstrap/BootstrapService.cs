using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;

using Microsoft.Extensions.Logging;

namespace EasySynQ.Services.Bootstrap;

/// <summary>
/// Production <see cref="IBootstrapService"/>. Creates the first-run
/// Administrator user along with the canonical Administrator role and
/// an open-ended role assignment, in a single transactional save.
/// </summary>
/// <remarks>
/// <para>
/// The three inserts (Role, User, UserRole) participate in one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>; either all three persist
/// or none do. The audit interceptor records three Insert rows
/// attributed to <c>UserId = null</c> because no identity has been
/// established yet — the AuditLogEntry XML documents this as the
/// "system-generated entries" case. All three rows share one
/// <c>CorrelationId</c> via the interceptor's per-save fallback,
/// grouping them as a single logical event.
/// </para>
/// </remarks>
public sealed partial class BootstrapService : IBootstrapService
{
    private const string AdministratorRoleName = "Administrator";

    private const string AdministratorRoleDescription =
        "Built-in administrator role with full system access. " +
        "Created during first-run bootstrap.";

    private readonly IUserRepository _users;
    private readonly IRepository<Role, Guid> _roles;
    private readonly IRepository<UserRole, Guid> _userRoles;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BootstrapService> _logger;

    /// <summary>Constructs the service over its dependencies.</summary>
    public BootstrapService(
        IUserRepository users,
        IRepository<Role, Guid> roles,
        IRepository<UserRole, Guid> userRoles,
        IPasswordHasher hasher,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<BootstrapService> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(userRoles);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);
        _users = users;
        _roles = roles;
        _userRoles = userRoles;
        _hasher = hasher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken)
    {
        var anyUser = await _users.AnyAsync(cancellationToken);
        return !anyUser;
    }

    /// <inheritdoc />
    public async Task<User> CreateAdministratorAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // Hash first so password-policy failures (length below the
        // minimum) surface before any DB read. PasswordHasher throws
        // ArgumentException; let it propagate so the caller (the
        // bootstrap UI surface) can translate to a UI-visible error.
        var hashed = _hasher.Hash(password);

        // Re-check emptiness after hashing — preserves the idempotency
        // contract carried over from the previous
        // CreateBootstrapAdministratorAsync. Honors the soft-delete
        // filter (a soft-deleted Administrator does not re-open
        // bootstrap; recovery is a separate admin/forensic concern).
        var anyUser = await _users.AnyAsync(cancellationToken);
        if (anyUser)
        {
            throw new InvalidOperationException(
                "Bootstrap is only available when no users exist; at least one user is already present.");
        }

        var nowUtc = _clock.UtcNow;

        var role = new Role(
            id: Guid.NewGuid(),
            name: AdministratorRoleName,
            description: AdministratorRoleDescription);

        var user = new User(
            id: Guid.NewGuid(),
            username: username.Trim(),
            displayName: displayName.Trim(),
            passwordHash: hashed.Hash,
            passwordSalt: hashed.Salt,
            passwordIterationCount: hashed.IterationCount,
            mustChangePassword: false);

        var userRole = new UserRole(
            id: Guid.NewGuid(),
            userId: user.Id,
            roleId: role.Id,
            effectivePeriod: new EffectiveDateRange(nowUtc, null));

        await _roles.AddAsync(role, cancellationToken);
        await _users.AddAsync(user, cancellationToken);
        await _userRoles.AddAsync(userRole, cancellationToken);

        // One SaveChanges drives the three inserts under a single
        // transaction; the audit interceptor groups them via a single
        // per-save CorrelationId. The interceptor actually emits FOUR
        // audit rows here — Role, User, UserRole, and a separate row
        // for the UserRole.EffectivePeriod owned type (EF Core tracks
        // owned-one types as their own EntityEntry). The owned-type
        // row is the only audit surface for the effective_* values;
        // see BootstrapServiceTests's audit-row test for the pinned
        // shape and the Phase 1 Follow-Up on the open audit-shape ADR.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogBootstrapCompleted(_logger, user.Username);
        return user;
    }

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Information,
        Message = "Bootstrap completed: administrator user {Username} created.")]
    private static partial void LogBootstrapCompleted(
        ILogger<BootstrapService> logger,
        string username);
}
