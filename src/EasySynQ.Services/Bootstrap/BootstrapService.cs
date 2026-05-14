using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;

using Microsoft.Extensions.Logging;

namespace EasySynQ.Services.Bootstrap;

/// <summary>
/// Production <see cref="IBootstrapService"/>. Creates the first-run
/// Administrator user along with the canonical Administrator role, an
/// open-ended role assignment, and the eleven Phase 1 system-permission
/// grants (ADR 0007), all in a single transactional save.
/// </summary>
/// <remarks>
/// <para>
/// The inserts (Role, User, UserRole, and eleven RolePermission rows)
/// participate in one <see cref="IUnitOfWork.SaveChangesAsync"/>; either
/// all of them persist or none do. The audit interceptor records Insert
/// rows attributed to <c>UserId = null</c> because no identity has been
/// established yet — the AuditLogEntry XML documents this as the
/// "system-generated entries" case. All audit rows share one
/// <c>CorrelationId</c> via the interceptor's per-save fallback,
/// grouping them as a single logical event.
/// </para>
/// <para>
/// The eleven system permissions are fetched by name from the catalog
/// seeded by the <c>AddPermissionsAndLinkTables</c> migration. The
/// catalog is the source of truth; <see cref="PermissionNames"/>.
/// <c>All</c> mirrors it exactly (the seed-test pins this invariant).
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
    private readonly IRepository<RolePermission, Guid> _rolePermissions;
    private readonly IPermissionRepository _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BootstrapService> _logger;

    /// <summary>Constructs the service over its dependencies.</summary>
    public BootstrapService(
        IUserRepository users,
        IRepository<Role, Guid> roles,
        IRepository<UserRole, Guid> userRoles,
        IRepository<RolePermission, Guid> rolePermissions,
        IPermissionRepository permissions,
        IPasswordHasher hasher,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<BootstrapService> logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(userRoles);
        ArgumentNullException.ThrowIfNull(rolePermissions);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);
        _users = users;
        _roles = roles;
        _userRoles = userRoles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
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
    public async Task<BootstrapResult> CreateAdministratorAsync(
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
        // contract. Honors the soft-delete filter (a soft-deleted
        // Administrator does not re-open bootstrap; recovery is a
        // separate admin/forensic concern).
        var anyUser = await _users.AnyAsync(cancellationToken);
        if (anyUser)
        {
            throw new InvalidOperationException(
                "Bootstrap is only available when no users exist; at least one user is already present.");
        }

        // Fetch the eleven seeded Phase 1 system permissions by name
        // BEFORE building the link rows. Single round trip via the
        // IN clause in IPermissionRepository.GetByNamesAsync.
        var systemPermissions = await _permissions.GetByNamesAsync(PermissionNames.All, cancellationToken);
        if (systemPermissions.Count != PermissionNames.All.Count)
        {
            // The migration's seeded catalog has drifted from the
            // PermissionNames constants. Fail loudly with the names of
            // what was actually returned so the operator can diagnose.
            var found = string.Join(", ", systemPermissions.Select(p => p.Name).OrderBy(n => n));
            var expected = string.Join(", ", PermissionNames.All.OrderBy(n => n));
            throw new InvalidOperationException(
                $"Permission catalog is missing one or more Phase 1 system permissions. " +
                $"Expected: [{expected}]. Found: [{found}]. " +
                $"This indicates the AddPermissionsAndLinkTables migration did not apply, " +
                $"or the catalog has been tampered with.");
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

        // Link the Administrator role to every Phase 1 system permission
        // — eleven RolePermission rows, each open-ended from `nowUtc`.
        // A FRESH EffectiveDateRange instance is constructed per row:
        // EF Core 10 treats each owned-type instance as a distinct
        // EntityEntry, and sharing one instance across multiple owners
        // confuses change tracking (the second owner ends up trying to
        // INSERT a row whose owned-type columns were already flattened
        // for the first owner, producing a NULL on EffectiveFromUtc).
        // The same `nowUtc` instant populates each fresh range so the
        // bootstrap transaction still has one clock instant of truth.
        foreach (var permission in systemPermissions)
        {
            var rolePermission = new RolePermission(
                id: Guid.NewGuid(),
                roleId: role.Id,
                permissionId: permission.Id,
                effectivePeriod: new EffectiveDateRange(nowUtc, null));
            await _rolePermissions.AddAsync(rolePermission, cancellationToken);
        }

        // One SaveChanges drives all the inserts under a single
        // transaction; the audit interceptor groups them via a single
        // per-save CorrelationId. The interceptor emits one audit row
        // per EntityEntry; with the eleven RolePermissions, the
        // RolePermission's owned-type EffectivePeriod, the UserRole's
        // owned EffectivePeriod, plus Role/User/UserRole themselves,
        // the bootstrap transaction now writes ~26 audit rows (1 User +
        // 1 Role + 1 UserRole + 1 UserRole.EffectivePeriod +
        // 11 RolePermission + 11 RolePermission.EffectivePeriod).
        // Phase 1 Follow-Up #11 tracks the audit-row shape for
        // owned types pending its own ADR.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogBootstrapCompleted(_logger, user.Username);

        // Return the role and permission snapshots directly. By
        // construction they are equivalent to what
        // IPermissionRepository.GetEffectivePermissionNamesForUserAsync
        // would return for the new user at `nowUtc`; the integration
        // test pins that invariant. Returning the canonical
        // PermissionNames.All avoids an avoidable EF read for a value
        // we deterministically know.
        return new BootstrapResult(
            Administrator: user,
            Roles: [AdministratorRoleName],
            Permissions: PermissionNames.All);
    }

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Information,
        Message = "Bootstrap completed: administrator user {Username} created.")]
    private static partial void LogBootstrapCompleted(
        ILogger<BootstrapService> logger,
        string username);
}
