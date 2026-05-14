using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;

namespace EasySynQ.Services.Bootstrap;

/// <summary>
/// First-run setup service. Detects whether the system has any users and
/// creates the initial Administrator account (<see cref="User"/> +
/// <c>Administrator</c> <see cref="Role"/> + open-ended
/// <see cref="UserRole"/> assignment + the eleven Phase 1 system
/// <see cref="RolePermission"/> grants) on demand.
/// </summary>
/// <remarks>
/// <para>
/// <b>When the surface is reachable.</b> The application is in the
/// "bootstrap required" state exactly when the Users table is empty.
/// <see cref="IAuthenticationService.AuthenticateAsync"/> returns
/// <see cref="AuthenticationResult.FirstRunBootstrap"/> in that state;
/// the host should also call <see cref="IsBootstrapRequiredAsync"/>
/// at startup and route to the bootstrap UI instead of the sign-in UI
/// when it returns <see langword="true"/>.
/// </para>
/// <para>
/// <b>Transactional shape (ADR 0007).</b>
/// <see cref="CreateAdministratorAsync"/> writes the new
/// <see cref="Role"/>, <see cref="User"/>, <see cref="UserRole"/>, and
/// eleven <see cref="RolePermission"/> rows in a single
/// <see cref="IUnitOfWork.SaveChangesAsync"/> — either all of them
/// persist or none do. The Administrator role, the open-ended role
/// assignment, and the eleven system-permission grants are
/// non-negotiable parts of the bootstrap shape: a user without a role
/// assignment is not usable under permission-based authorization, and
/// an Administrator role without the system permissions cannot manage
/// the system.
/// </para>
/// <para>
/// <b>Idempotency.</b>
/// <see cref="CreateAdministratorAsync"/> re-asserts emptiness after
/// hashing the password and before inserting; throws
/// <see cref="InvalidOperationException"/> if a user appeared between
/// detection and creation. Vanishingly unlikely on a single-process
/// desktop app, but the contract is cheap and the failure mode is
/// unambiguous.
/// </para>
/// <para>
/// <b>Audit attribution.</b> All audit rows written during bootstrap
/// are attributed to <c>UserId = null</c> — the bootstrap path runs
/// before any identity exists, so
/// <see cref="ICurrentUserAccessor.UserId"/> is null and the audit
/// interceptor records that verbatim per <c>AuditLogEntry</c>'s
/// documented "system-generated entries" case. The rows share one
/// <c>CorrelationId</c> via the audit interceptor's per-save
/// fallback, so the writes can be reconstructed as one logical event.
/// </para>
/// </remarks>
public interface IBootstrapService
{
    /// <summary>
    /// Returns <see langword="true"/> when the system requires first-run
    /// bootstrap (no non-soft-deleted users exist).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when no users exist;
    /// <see langword="false"/> otherwise.</returns>
    Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates the first-run Administrator user along with the
    /// canonical <c>Administrator</c> role, an open-ended
    /// <see cref="UserRole"/> assignment that starts at the current
    /// clock instant, and the eleven Phase 1 system-permission grants
    /// (<see cref="EasySynQ.Domain.PermissionNames"/>.<c>All</c>) linked
    /// to the new Administrator role. Succeeds only when no users
    /// exist; throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="username">Desired Administrator username.
    /// Whitespace-trimmed at the boundary; must not be
    /// null/empty/whitespace.</param>
    /// <param name="password">Desired plaintext password. Must satisfy
    /// the policy's
    /// <see cref="IPasswordPolicy.MinimumLength"/>;
    /// <see cref="IPasswordHasher.Hash"/> throws
    /// <see cref="ArgumentException"/> otherwise and the exception
    /// propagates so the caller can translate it to a UI error.</param>
    /// <param name="displayName">Desired display name. Trimmed at the
    /// boundary; must not be null/empty/whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BootstrapResult"/> carrying the persisted
    /// <see cref="User"/> plus the role-and-permission snapshot the
    /// caller should pass to
    /// <see cref="IWritableCurrentUserAccessor.SetCurrentUser"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when any input is
    /// null/empty/whitespace, or when <paramref name="password"/> is
    /// shorter than the policy's minimum length.</exception>
    /// <exception cref="InvalidOperationException">Thrown when at least
    /// one user exists at the moment of the idempotency check — the
    /// bootstrap window has closed.</exception>
    Task<BootstrapResult> CreateAdministratorAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken);
}
