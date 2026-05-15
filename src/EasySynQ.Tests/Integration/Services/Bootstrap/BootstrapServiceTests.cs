using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Bootstrap;
using EasySynQ.Services.Identity;
using EasySynQ.Tests.Integration.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Bootstrap;

public class BootstrapServiceTests : ServiceIntegrationTestBase
{
    [Fact]
    public async Task IsBootstrapRequiredAsync_OnEmptyUserTable_ReturnsTrueAsync()
    {
        await using var scope = NewScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();

        var required = await bootstrap.IsBootstrapRequiredAsync(Ct);

        required.Should().BeTrue();
    }

    [Fact]
    public async Task IsBootstrapRequiredAsync_WhenAnyUserExists_ReturnsFalseAsync()
    {
        await SeedUserAsync("alice", "password");

        await using var scope = NewScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();

        var required = await bootstrap.IsBootstrapRequiredAsync(Ct);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAdministratorAsync_OnEmptyState_CreatesRoleUserAndUserRole_AndAdminCanSubsequentlyAuthenticateAsync()
    {
        BootstrapResult result;
        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            result = await bootstrap.CreateAdministratorAsync(
                username: "admin",
                password: "bootstrap-pw",
                displayName: "Administrator",
                cancellationToken: Ct);
        }

        // Returned User reflects the persisted state.
        result.Administrator.Username.Should().Be("admin");
        result.Administrator.DisplayName.Should().Be("Administrator");
        result.Administrator.MustChangePassword.Should().BeFalse();
        result.Administrator.Id.Should().NotBe(Guid.Empty);

        // ADR 0007: the returned snapshots are deterministic by
        // construction — the Administrator role and the eleven seeded
        // Phase 1 system permissions.
        result.Roles.Should().BeEquivalentTo("Administrator");
        result.Permissions.Should().BeEquivalentTo(PermissionNames.All);

        await using (var ctx = NewContext())
        {
            var users = await ctx.Users.ToListAsync(Ct);
            users.Should().ContainSingle();
            users[0].Username.Should().Be("admin");
            users[0].DisplayName.Should().Be("Administrator");

            // Roles include the seeded Phase 2 QualityManager role
            // alongside the bootstrap-created Administrator role.
            // Scope the assertion to the Administrator role
            // specifically — the seeded QM role is pre-existing
            // catalog data, not something bootstrap creates.
            var adminRole = await ctx.Roles.SingleAsync(r => r.Name == "Administrator", Ct);

            var userRoles = await ctx.UserRoles.ToListAsync(Ct);
            userRoles.Should().ContainSingle();
            userRoles[0].UserId.Should().Be(users[0].Id);
            userRoles[0].RoleId.Should().Be(adminRole.Id);
            userRoles[0].EffectivePeriod.EffectiveFromUtc.Should().Be(Clock.UtcNow);
            userRoles[0].EffectivePeriod.EffectiveToUtc.Should().BeNull();

            // Eleven RolePermission rows linking the Administrator
            // role to every Phase 1 system permission. Scoped to the
            // Administrator role's id — the seeded QualityManager
            // role also has RolePermission rows (12 of them, one per
            // Phase 2 document permission except Document.AssignReviewers)
            // and would inflate an unfiltered count.
            var rolePermissions = await ctx.RolePermissions
                .Where(rp => rp.RoleId == adminRole.Id)
                .ToListAsync(Ct);
            rolePermissions.Should().HaveCount(PermissionNames.All.Count);
            rolePermissions.Should().OnlyContain(
                rp => rp.EffectivePeriod.EffectiveFromUtc == Clock.UtcNow
                   && rp.EffectivePeriod.EffectiveToUtc == null);
        }

        // Bridge assertion: the created admin authenticates successfully
        // against IAuthenticationService — proves the bootstrap writes
        // are read-compatible with the auth surface end-to-end.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var authResult = await auth.AuthenticateAsync("admin", "bootstrap-pw", Ct);

            var success = authResult.Should().BeOfType<AuthenticationResult.Success>().Subject;
            success.User.Username.Should().Be("admin");
            success.RequiresPasswordChange.Should().BeFalse();
            // ADR 0007: auth-time resolution returns the same shape as
            // bootstrap-time construction.
            success.Roles.Should().BeEquivalentTo("Administrator");
            success.Permissions.Should().BeEquivalentTo(PermissionNames.All);
        }
    }

    [Fact]
    public async Task CreateAdministratorAsync_ReturnedSnapshots_MatchPermissionRepositoryResolutionForNewUserAsync()
    {
        // Invariant pin: the BootstrapResult.Permissions returned at
        // construction must equal what
        // IPermissionRepository.GetEffectivePermissionNamesForUserAsync
        // would return for the just-created user. This protects the
        // "return PermissionNames.All directly" optimization from drift
        // away from the resolution algorithm — if the migration's seed
        // ever diverges from PermissionNames or the resolution query
        // ever changes shape, this pin fires.
        BootstrapResult result;
        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            result = await bootstrap.CreateAdministratorAsync(
                "admin", "bootstrap-pw", "Administrator", Ct);
        }

        await using (var scope = NewScope())
        {
            var userRoles = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
            var permissions = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();

            var resolvedRoles = await userRoles.GetEffectiveRoleNamesAsync(
                result.Administrator.Id, Clock.UtcNow, Ct);
            var resolvedPermissions = await permissions.GetEffectivePermissionNamesForUserAsync(
                result.Administrator.Id, Clock.UtcNow, Ct);

            resolvedRoles.Should().BeEquivalentTo(result.Roles);
            resolvedPermissions.Should().BeEquivalentTo(result.Permissions);
        }
    }

    [Fact]
    public async Task CreateAdministratorAsync_AfterUserAlreadyExists_ThrowsInvalidOperationExceptionAsync()
    {
        await SeedUserAsync("first", "password");

        await using var scope = NewScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();

        var act = async () => await bootstrap.CreateAdministratorAsync(
            "second", "password", "Second", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bootstrap is only available when no users exist*");
    }

    [Fact]
    public async Task CreateAdministratorAsync_PersistsHashThatVerifiesAgainstSuppliedPasswordAsync()
    {
        BootstrapResult result;
        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            result = await bootstrap.CreateAdministratorAsync(
                "admin", "bootstrap-pw", "Administrator", Ct);
        }

        // Test the contract end-to-end: the persisted hash material must
        // verify against the supplied plaintext under the same hasher.
        // Avoids coupling the test to PasswordHasher's internals (salt
        // generation, encoding) the way a literal-string assertion would.
        var hasher = ServiceProvider.GetRequiredService<IPasswordHasher>();
        var verification = hasher.Verify(
            "bootstrap-pw",
            result.Administrator.PasswordHash,
            result.Administrator.PasswordSalt,
            result.Administrator.PasswordIterationCount);

        verification.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public async Task CreateAdministratorAsync_UsesClockUtcNowForEffectivePeriodFromAsync()
    {
        // Advance the fixed clock to a distinctive instant so the
        // assertion pins on _clock.UtcNow specifically (rather than
        // DateTime.UtcNow leaking in from anywhere). Advance the
        // TemporalResolver.AsOfUtc too — UserRole and RolePermission
        // are both IEffectiveDated and the DbContext's query filter
        // hides assignments whose EffectiveFromUtc is in the future
        // relative to AsOfUtc. The fixture defaults AsOfUtc to its own
        // startInstant (in 2026); rows written with a 2027
        // EffectiveFromUtc would be filtered out of the readback
        // without this sync.
        var distinctive = new DateTime(2027, 3, 15, 9, 30, 0, DateTimeKind.Utc);
        Clock.UtcNow = distinctive;
        TemporalResolver.AsOfUtc = distinctive;

        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            await bootstrap.CreateAdministratorAsync("admin", "password", "Admin", Ct);
        }

        await using var ctx = NewContext();
        var userRole = await ctx.UserRoles.SingleAsync(Ct);
        userRole.EffectivePeriod.EffectiveFromUtc.Should().Be(distinctive);
        userRole.EffectivePeriod.EffectiveToUtc.Should().BeNull();

        // All eleven RolePermission rows for the Administrator role
        // share the same EffectiveFromUtc — the bootstrap transaction
        // has one clock instant of truth. Scoped to userRole.RoleId
        // (the bootstrap-created Administrator role) — the seeded
        // Phase 2 QualityManager role's 12 RolePermission rows are
        // pre-existing catalog data, not part of what bootstrap writes,
        // and they carry the migration's seed timestamp (2026-05-14),
        // not this test's distinctive 2027 clock.
        var rolePermissions = await ctx.RolePermissions
            .Where(rp => rp.RoleId == userRole.RoleId)
            .ToListAsync(Ct);
        rolePermissions.Should().HaveCount(PermissionNames.All.Count);
        rolePermissions.Should().OnlyContain(
            rp => rp.EffectivePeriod.EffectiveFromUtc == distinctive
               && rp.EffectivePeriod.EffectiveToUtc == null);
    }

    [Fact]
    public async Task CreateAdministratorAsync_WritesAdministratorAndPermissionGrantsAtomically_AllNullUserAttributed_SharingOneCorrelationIdAsync()
    {
        // CurrentUser is unauthenticated by default (the fixture's
        // MutableCurrentUserAccessor starts with UserId = null). This is
        // the production state at bootstrap time — no one has signed in
        // yet, and the user being created in this transaction is the
        // first one. The audit interceptor records UserId = null
        // verbatim per AuditLogEntry's "system-generated entries"
        // contract; this test pins that behavior.
        //
        // ADR 0007 grew the bootstrap transaction from 4 rows to a
        // catalog-dependent count. Bootstrap creates one
        // RolePermission row per name in PermissionNames.All (plus a
        // companion EffectiveDateRange owned-type audit row per
        // RolePermission), so the audit-row distribution is a function
        // of |PermissionNames.All|:
        //
        //   1 User
        //   1 Role (Administrator)
        //   1 UserRole
        //   1 EffectiveDateRange (UserRole.EffectivePeriod owned-type)
        //   N RolePermission (one per current system permission)
        //   N EffectiveDateRange (one per RolePermission.EffectivePeriod
        //                         owned-type)
        //
        // where N = PermissionNames.All.Count. Today (after Phase 2 C2
        // added Vault.PhysicalDelete) N = 12, total = 28; before C2
        // landed N = 11, total = 26. The test computes both from
        // PermissionNames.All.Count so a future system-permission
        // addition does not require mechanical literal updates here.
        // See the 2026-05-14 (Phase 2 C2) handoff note for the
        // narrowing-from-literals-to-catalog-derived shift.
        //
        // Phase 1 Follow-Up #11 tracks the open ADR on whether to
        // suppress + enrich vs. keep the current owned-type audit
        // shape; this test pins the status quo so that decision is
        // made deliberately rather than by drift.

        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            await bootstrap.CreateAdministratorAsync(
                "admin", "bootstrap-pw", "Administrator", Ct);
        }

        await using var ctx = NewContext();
        var auditRows = await ctx.AuditLogEntries.ToListAsync(Ct);

        var systemPermissionCount = PermissionNames.All.Count;
        var expectedRowCount =
            1                          // User
          + 1                          // Role
          + 1                          // UserRole
          + 1                          // UserRole's EffectivePeriod (owned)
          + systemPermissionCount      // RolePermission (one per current system permission)
          + systemPermissionCount;     // each RolePermission's EffectivePeriod (owned)
        auditRows.Should().HaveCount(expectedRowCount);

        // All rows are UserId-null (system attribution).
        auditRows.Should().OnlyContain(r => r.UserId == null);

        // All rows share ONE CorrelationId — verifies that the
        // IAuditCorrelationProvider null-fallback in
        // AuditSaveChangesInterceptor produces one correlation per
        // SaveChanges, not one per entity. Spanning many rows in one
        // transaction strengthens the per-save-grouping claim
        // considerably.
        auditRows.Select(r => r.CorrelationId).Distinct().Should().ContainSingle();

        // Per-type counts pin the exact distribution. EF Core 10's
        // owned-type tracking emits "EffectiveDateRange" entries for
        // BOTH UserRole.EffectivePeriod and RolePermission.EffectivePeriod
        // — same CLR type name; total is 1 (UserRole's) + N
        // (RolePermissions').
        var byType = auditRows
            .GroupBy(r => r.EntityTypeName)
            .ToDictionary(g => g.Key, g => g.Count());
        byType.Should().ContainKey("User").WhoseValue.Should().Be(1);
        byType.Should().ContainKey("Role").WhoseValue.Should().Be(1);
        byType.Should().ContainKey("UserRole").WhoseValue.Should().Be(1);
        byType.Should().ContainKey("RolePermission").WhoseValue.Should().Be(systemPermissionCount);
        byType.Should().ContainKey("EffectiveDateRange").WhoseValue.Should().Be(1 + systemPermissionCount);
    }
}
