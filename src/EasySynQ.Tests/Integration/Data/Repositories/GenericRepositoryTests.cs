using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Tests the generic <see cref="Repository{TEntity, TId}"/> against
/// <see cref="Role"/> — an entity that has neither user-specific lookup
/// methods nor the audit-log-specific overrides, so it validates the
/// raw generic surface and the interceptor pipeline that fires through
/// the repository's SaveChanges path.
/// </summary>
public class GenericRepositoryTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task GetByIdAsync_ReturnsEntityAsync()
    {
        var role = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var loaded = await repo.GetByIdAsync(role.Id, Ct);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("QM");
        }
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNonexistentIdAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository<Role, Guid>(ctx);
        var loaded = await repo.GetByIdAsync(Guid.NewGuid(), Ct);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForSoftDeletedEntityAsync()
    {
        var role = new Role(Guid.NewGuid(), "Gone", "Soft-deleted role.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(role).Property(nameof(Role.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var loaded = await repo.GetByIdAsync(role.Id, Ct);
            loaded.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetByIdIncludingDeletedAsync_ReturnsSoftDeletedEntityAsync()
    {
        var role = new Role(Guid.NewGuid(), "Recoverable", "Soft-deleted.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(role).Property(nameof(Role.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var loaded = await repo.GetByIdIncludingDeletedAsync(role.Id, Ct);
            loaded.Should().NotBeNull();
            loaded!.IsDeleted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Query_IsComposable_WhereOrderBySelectAsync()
    {
        var apple = new Role(Guid.NewGuid(), "Apple", "Fruit role.");
        var banana = new Role(Guid.NewGuid(), "Banana", "Fruit role.");
        var cherry = new Role(Guid.NewGuid(), "Cherry", "Fruit role.");

        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(apple);
            ctx.Roles.Add(banana);
            ctx.Roles.Add(cherry);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var names = await repo.Query()
                .Where(r => r.Name == "Banana" || r.Name == "Cherry")
                .OrderBy(r => r.Name)
                .Select(r => r.Name)
                .ToListAsync(Ct);

            names.Should().Equal("Banana", "Cherry");
        }
    }

    [Fact]
    public async Task SoftDelete_SetsIsDeletedAndWritesAuditEntryAsync()
    {
        var role = new Role(Guid.NewGuid(), "SoftKill", "About to be soft-deleted.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var loaded = await repo.GetByIdAsync(role.Id, Ct);
            loaded.Should().NotBeNull();

            repo.SoftDelete(loaded!);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var queryCtx = NewContext())
        {
            // Default query path excludes soft-deleted row.
            var defaultQuery = await queryCtx.Roles
                .SingleOrDefaultAsync(r => r.Id == role.Id, Ct);
            defaultQuery.Should().BeNull();

            // The interceptor pipeline still fired through the repository
            // SaveChanges path — there's an audit entry with action=Delete.
            var deleteEntry = await queryCtx.AuditLogEntries
                .SingleAsync(
                    a => a.EntityTypeName == "Role"
                         && a.EntityId == role.Id.ToString()
                         && a.Action == AuditAction.Delete,
                    Ct);
            deleteEntry.Before.Should().NotBeNullOrEmpty();
            deleteEntry.After.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task HardDelete_RemovesRowAndWritesAuditEntryAsync()
    {
        var role = new Role(Guid.NewGuid(), "HardKill", "About to be hard-deleted.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            var loaded = await repo.GetByIdAsync(role.Id, Ct);
            loaded.Should().NotBeNull();

            repo.HardDelete(loaded!);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var queryCtx = NewContext())
        {
            // Row is physically gone — even IgnoreQueryFilters cannot find it.
            var stillExists = await queryCtx.Roles
                .IgnoreQueryFilters()
                .AnyAsync(r => r.Id == role.Id, Ct);
            stillExists.Should().BeFalse();

            // Audit entry with HardDelete action and After=null per ADR 0002.
            var hardDeleteEntry = await queryCtx.AuditLogEntries
                .SingleAsync(
                    a => a.EntityTypeName == "Role"
                         && a.EntityId == role.Id.ToString()
                         && a.Action == AuditAction.HardDelete,
                    Ct);
            hardDeleteEntry.Before.Should().NotBeNullOrEmpty();
            hardDeleteEntry.After.Should().BeNull();
        }
    }

    [Fact]
    public async Task AddAsync_PlusSaveChangesAsync_InsertsAndWritesAuditEntryAsync()
    {
        var role = new Role(Guid.NewGuid(), "FreshlyAdded", "Inserted via repository.");

        await using (var ctx = NewContext())
        {
            var repo = new Repository<Role, Guid>(ctx);
            await repo.AddAsync(role, Ct);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var queryCtx = NewContext())
        {
            var loaded = await queryCtx.Roles.SingleAsync(r => r.Id == role.Id, Ct);
            loaded.Name.Should().Be("FreshlyAdded");

            var insertEntry = await queryCtx.AuditLogEntries
                .SingleAsync(
                    a => a.EntityTypeName == "Role"
                         && a.EntityId == role.Id.ToString()
                         && a.Action == AuditAction.Insert,
                    Ct);
            insertEntry.Before.Should().BeNull();
            insertEntry.After.Should().NotBeNullOrEmpty();
        }
    }
}
