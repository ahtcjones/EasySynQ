using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Bootstrap;
using EasySynQ.Services.Identity;
using EasySynQ.Tests.Integration.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Bootstrap;

public class BootstrapServiceTests : ServiceIntegrationTestBase
{
    private static readonly string[] ExpectedAuditEntityTypeNames =
        ["EffectiveDateRange", "Role", "User", "UserRole"];

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
        User createdUser;
        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            createdUser = await bootstrap.CreateAdministratorAsync(
                username: "admin",
                password: "bootstrap-pw",
                displayName: "Administrator",
                cancellationToken: Ct);
        }

        // Returned User reflects the persisted state.
        createdUser.Username.Should().Be("admin");
        createdUser.DisplayName.Should().Be("Administrator");
        createdUser.MustChangePassword.Should().BeFalse();
        createdUser.Id.Should().NotBe(Guid.Empty);

        await using (var ctx = NewContext())
        {
            var users = await ctx.Users.ToListAsync(Ct);
            users.Should().ContainSingle();
            users[0].Username.Should().Be("admin");
            users[0].DisplayName.Should().Be("Administrator");

            var roles = await ctx.Roles.ToListAsync(Ct);
            roles.Should().ContainSingle();
            roles[0].Name.Should().Be("Administrator");

            var userRoles = await ctx.UserRoles.ToListAsync(Ct);
            userRoles.Should().ContainSingle();
            userRoles[0].UserId.Should().Be(users[0].Id);
            userRoles[0].RoleId.Should().Be(roles[0].Id);
            userRoles[0].EffectivePeriod.EffectiveFromUtc.Should().Be(Clock.UtcNow);
            userRoles[0].EffectivePeriod.EffectiveToUtc.Should().BeNull();
        }

        // Bridge assertion: the created admin authenticates successfully
        // against IAuthenticationService — proves the bootstrap writes
        // are read-compatible with the auth surface end-to-end.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var result = await auth.AuthenticateAsync("admin", "bootstrap-pw", Ct);

            var success = result.Should().BeOfType<AuthenticationResult.Success>().Subject;
            success.User.Username.Should().Be("admin");
            success.RequiresPasswordChange.Should().BeFalse();
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
        User created;
        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            created = await bootstrap.CreateAdministratorAsync(
                "admin", "bootstrap-pw", "Administrator", Ct);
        }

        // Test the contract end-to-end: the persisted hash material must
        // verify against the supplied plaintext under the same hasher.
        // Avoids coupling the test to PasswordHasher's internals (salt
        // generation, encoding) the way a literal-string assertion would.
        var hasher = ServiceProvider.GetRequiredService<IPasswordHasher>();
        var verification = hasher.Verify(
            "bootstrap-pw",
            created.PasswordHash,
            created.PasswordSalt,
            created.PasswordIterationCount);

        verification.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public async Task CreateAdministratorAsync_UsesClockUtcNowForEffectivePeriodFromAsync()
    {
        // Advance the fixed clock to a distinctive instant so the
        // assertion pins on _clock.UtcNow specifically (rather than
        // DateTime.UtcNow leaking in from anywhere). Advance the
        // TemporalResolver.AsOfUtc too — UserRole is IEffectiveDated
        // and the DbContext's query filter hides assignments whose
        // EffectiveFromUtc is in the future relative to AsOfUtc. The
        // fixture defaults AsOfUtc to its own startInstant (in 2026);
        // a UserRole written with a 2027 EffectiveFromUtc would be
        // filtered out of the readback without this sync.
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
    }

    [Fact]
    public async Task CreateAdministratorAsync_WritesThreeAuditRows_AllNullUserAttributed_SharingOneCorrelationIdAsync()
    {
        // CurrentUser is unauthenticated by default (the fixture's
        // MutableCurrentUserAccessor starts with UserId = null). This is
        // the production state at bootstrap time — no one has signed in
        // yet, and the user being created in this transaction is the
        // first one. The audit interceptor records UserId = null
        // verbatim per AuditLogEntry's "system-generated entries"
        // contract; this test pins that behavior.

        await using (var scope = NewScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            await bootstrap.CreateAdministratorAsync(
                "admin", "bootstrap-pw", "Administrator", Ct);
        }

        await using var ctx = NewContext();
        var auditRows = await ctx.AuditLogEntries.ToListAsync(Ct);

        // (1) Exactly four audit rows — Role, User, UserRole, AND the
        //     EffectiveDateRange owned-type row. EF Core 10 tracks
        //     owned types as their own EntityEntry, so the audit
        //     interceptor walks them alongside their owners and emits
        //     a row per entry. The owned-type row is the only audit
        //     surface for the effective_* values — UserRole's own
        //     After snapshot does not enumerate the flattened columns.
        //     Phase 1 Follow-Up tracks the open ADR question on
        //     whether to suppress + enrich vs. keep the current shape.
        auditRows.Should().HaveCount(4);

        // (2) All four rows are UserId-null (system attribution).
        auditRows.Should().OnlyContain(r => r.UserId == null);

        // (3) All four rows share ONE CorrelationId — verifies that
        //     the IAuditCorrelationProvider null-fallback in
        //     AuditSaveChangesInterceptor produces one correlation per
        //     SaveChanges, not one per entity. Spanning four rows
        //     instead of three strengthens the per-save-grouping claim.
        auditRows.Select(r => r.CorrelationId).Distinct().Should().ContainSingle();

        // (4) The four rows cover exactly the set
        //     {"EffectiveDateRange", "Role", "User", "UserRole"} —
        //     order is not guaranteed (EF's change tracker yields in
        //     arbitrary order).
        auditRows.Select(r => r.EntityTypeName)
            .Should().BeEquivalentTo(ExpectedAuditEntityTypeNames);
    }
}
