using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

/// <summary>
/// Pins the known limitation called out in ADR 0002's Required Tests
/// section: <c>ExecuteSqlRaw</c> / <c>ExecuteSqlInterpolated</c> bypass
/// EF Core's <c>SaveChangesInterceptor</c> pipeline and therefore bypass
/// the audit-log writer. The Phase 1 mandate is that services and
/// repositories <b>must not</b> use raw SQL for compliance-critical
/// writes; this test fixes the limitation in code so we never
/// accidentally claim immunity we don't have.
/// </summary>
public class AuditLogBypassResistanceTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task RawSqlUpdate_BypassesInterceptor_ProducesNoAuditEntry_DocumentedLimitationAsync()
    {
        // Set up: insert a User through the normal path. Produces 1 audit row.
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            (await ctx.AuditLogEntries.CountAsync(Ct))
                .Should().Be(1, "the insert through ctx.Add + SaveChanges goes through the interceptor");
        }

        // Now bypass the change tracker entirely with a raw SQL UPDATE.
        await using (var ctx = NewContext())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Users SET DisplayName = 'Tampered' WHERE Id = {user.Id}",
                Ct);
        }

        await using (var ctx = NewContext())
        {
            // The data WAS modified — bypass succeeded at the DB level.
            var loaded = await ctx.Users.SingleAsync(u => u.Id == user.Id, Ct);
            loaded.DisplayName.Should().Be("Tampered");

            // And yet — no audit-log entry for the tamper. This is the
            // limitation. Repository surface must reject raw-SQL writes
            // for compliance-critical tables (Phase 1 data-layer scope
            // per ADR 0002).
            (await ctx.AuditLogEntries.CountAsync(Ct))
                .Should().Be(1, "raw-SQL UPDATE bypasses the SaveChangesInterceptor pipeline; this is the known limitation pinned by ADR 0002");
        }
    }

    [Fact]
    public async Task SanctionedWritePath_IsAlwaysAuditedAsync()
    {
        // The complement of the above: every change that DOES go through
        // ctx.Add / Update / Remove + SaveChanges is captured.
        var user = new User(Guid.NewGuid(), "bob", "Bob", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        var role = new Role(Guid.NewGuid(), "Operator", "Production operator.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entries = await ctx.AuditLogEntries.ToListAsync(Ct);
            entries.Should().HaveCount(2);
            entries.Should().Contain(e => e.EntityTypeName == "User" && e.Action == AuditAction.Insert);
            entries.Should().Contain(e => e.EntityTypeName == "Role" && e.Action == AuditAction.Insert);
        }
    }
}
