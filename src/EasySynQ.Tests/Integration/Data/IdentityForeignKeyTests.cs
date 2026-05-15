using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class IdentityForeignKeyTests : IntegrationTestBase
{
    private static readonly DateTime PastFrom = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task InsertingUserRole_WithNonexistentUserId_FailsAsync()
    {
        // Set up a valid Role so the RoleId FK is satisfied; the failure
        // must come from the UserId FK specifically. Name avoids
        // "QualityManager" — the Phase 2 migration seeds a role with
        // that exact name (ADR 0008), and Roles.Name has a unique index.
        var role = new Role(Guid.NewGuid(), "QualityManagerFkTest", "Quality Manager role.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        var orphanUserId = Guid.NewGuid();
        var ur = new UserRole(
            Guid.NewGuid(),
            userId: orphanUserId,
            roleId: role.Id,
            new EffectiveDateRange(PastFrom, null));

        await using var ctx2 = NewContext();
        ctx2.UserRoles.Add(ur);
        var act = async () => await ctx2.SaveChangesAsync(Ct);

        // SQLite raises a FOREIGN KEY constraint violation; EF Core wraps
        // it in DbUpdateException.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InsertingUserRole_WithNonexistentRoleId_FailsAsync()
    {
        // Set up a valid User so the UserId FK is satisfied; the failure
        // must come from the RoleId FK specifically.
        var user = new User(Guid.NewGuid(), "alice", "Alice", "hashed", "salted", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        var orphanRoleId = Guid.NewGuid();
        var ur = new UserRole(
            Guid.NewGuid(),
            userId: user.Id,
            roleId: orphanRoleId,
            new EffectiveDateRange(PastFrom, null));

        await using var ctx2 = NewContext();
        ctx2.UserRoles.Add(ur);
        var act = async () => await ctx2.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SoftDeletingUser_DoesNotCascadeToUserRoles_AndAllowsNewAssignmentAsync()
    {
        // Set up a User, a Role, and an active UserRole linking them.
        var user = new User(Guid.NewGuid(), "bob", "Bob", "hashed", "salted", 600_000, false);
        var role = new Role(Guid.NewGuid(), "LabTech", "Laboratory Technician role.");
        var existingUr = new UserRole(
            Guid.NewGuid(),
            user.Id,
            role.Id,
            new EffectiveDateRange(PastFrom, null));

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(existingUr);
            await ctx.SaveChangesAsync(Ct);

            // Soft-delete the user. OnDelete.Restrict would reject a hard
            // delete; soft delete just flips the flag, so the row remains
            // and FKs continue to point at a valid Users row.
            ctx.Entry(user).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        // The existing UserRole row is intact — no cascade fired.
        await using (var ctx = NewContext())
        {
            var stillThere = await ctx.UserRoles
                .SingleOrDefaultAsync(u => u.Id == existingUr.Id, Ct);
            stillThere.Should().NotBeNull();
        }

        // And a brand-new UserRole assigning the (soft-deleted) user to
        // the same role succeeds at the schema level. The FK validates
        // against table presence, not soft-delete state. Refusing
        // assignment to a disabled user is a service-layer concern, not
        // a schema concern.
        await using (var ctx = NewContext())
        {
            var newUr = new UserRole(
                Guid.NewGuid(),
                user.Id,
                role.Id,
                new EffectiveDateRange(PastFrom, null));
            ctx.UserRoles.Add(newUr);

            // Should not throw — the row in Users with user.Id still
            // exists; FK passes regardless of IsDeleted.
            await ctx.SaveChangesAsync(Ct);

            var saved = await ctx.UserRoles
                .SingleOrDefaultAsync(u => u.Id == newUr.Id, Ct);
            saved.Should().NotBeNull();
        }
    }
}
