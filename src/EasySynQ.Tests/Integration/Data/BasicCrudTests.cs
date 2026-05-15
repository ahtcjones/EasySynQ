using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Entities.Snapshots;
using EasySynQ.Domain.Enums;
using EasySynQ.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class BasicCrudTests : IntegrationTestBase
{
    private const string ValidHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly DateTime Now = new(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task User_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var user = new User(id, "alice", "Alice", "hashed", "salted", 600_000, mustChangePassword: true);

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Users.SingleAsync(u => u.Id == id, Ct);
            loaded.Username.Should().Be("alice");
            loaded.DisplayName.Should().Be("Alice");
            loaded.PasswordIterationCount.Should().Be(600_000);
            loaded.MustChangePassword.Should().BeTrue();
            loaded.IsDisabled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Role_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        // Name avoids "QualityManager" — the Phase 2 migration seeds a
        // role with that exact name (ADR 0008), and Roles.Name has a
        // unique index.
        var role = new Role(id, "QualityManagerRoundTrip", "Approves NCRs and signs CoCs.");

        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Roles.SingleAsync(r => r.Id == id, Ct);
            loaded.Name.Should().Be("QualityManagerRoundTrip");
            loaded.Description.Should().StartWith("Approves");
        }
    }

    [Fact]
    public async Task UserRole_RoundTrips_WithFlattenedEffectivePeriodAsync()
    {
        // FK constraints (per ADR 0004) require the referenced User and Role
        // rows to exist before the UserRole is inserted.
        var user = new User(Guid.NewGuid(), "carol", "Carol", "hashed", "salted", 600_000, false);
        var role = new Role(Guid.NewGuid(), "Operator", "Production operator role.");

        var id = Guid.NewGuid();
        var period = new EffectiveDateRange(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        var ur = new UserRole(id, user.Id, role.Id, period);

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(ur);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            // The test period (2024-01-01 → 2024-12-31) is entirely in the
            // past; the effective-dating filter (introduced in Chunk B with
            // a "now"-anchored resolver in IntegrationTestBase) would hide
            // it. IgnoreQueryFilters opts out — this test is about column
            // round-trip, not filter behavior.
            var loaded = await ctx.UserRoles.IgnoreQueryFilters().SingleAsync(u => u.Id == id, Ct);
            loaded.UserId.Should().Be(user.Id);
            loaded.RoleId.Should().Be(role.Id);
            loaded.EffectivePeriod.EffectiveFromUtc.Should().Be(period.EffectiveFromUtc);
            loaded.EffectivePeriod.EffectiveToUtc.Should().Be(period.EffectiveToUtc);
            // UTC-kind convention must round-trip — domain constructors assert this.
            loaded.EffectivePeriod.EffectiveFromUtc.Kind.Should().Be(DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task AuditLogEntry_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var corrId = Guid.NewGuid();
        var entry = new AuditLogEntry(
            id, Now, userId: null,
            "User", "user-1", AuditAction.Insert,
            before: null, after: "{\"username\":\"alice\"}",
            correlationId: corrId);

        await using (var ctx = NewContext())
        {
            ctx.AuditLogEntries.Add(entry);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.AuditLogEntries.SingleAsync(a => a.Id == id, Ct);
            loaded.EntityTypeName.Should().Be("User");
            loaded.EntityId.Should().Be("user-1");
            loaded.Action.Should().Be(AuditAction.Insert);
            loaded.Before.Should().BeNull();
            loaded.After.Should().Be("{\"username\":\"alice\"}");
            loaded.CorrelationId.Should().Be(corrId);
            loaded.UtcTimestamp.Kind.Should().Be(DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task Signature_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var sig = new Signature(
            id, Now, "QualityManager", "Job", "J-2026-0847", ValidHash);

        await using (var ctx = NewContext())
        {
            ctx.Signatures.Add(sig);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Signatures.SingleAsync(s => s.Id == id, Ct);
            loaded.RoleAtTimeOfSign.Should().Be("QualityManager");
            loaded.SignedEntityType.Should().Be("Job");
            loaded.SignedEntityId.Should().Be("J-2026-0847");
            loaded.PayloadHash.Should().Be(ValidHash);
        }
    }

    [Fact]
    public async Task LockReason_RoundTrips_SingleLinkAsync()
    {
        var id = Guid.NewGuid();
        var chain = new[]
        {
            new LockReasonLink("Job", "J-1", "detail", null, null, null, isTerminal: true),
        };
        var reason = new LockReason(id, "Job", "J-1", chain);

        await using (var ctx = NewContext())
        {
            ctx.LockReasons.Add(reason);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.LockReasons.SingleAsync(lr => lr.Id == id, Ct);
            loaded.LockedEntityType.Should().Be("Job");
            loaded.LockedEntityId.Should().Be("J-1");
            loaded.Chain.Should().ContainSingle();
            loaded.Chain[0].IsTerminal.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Snapshot_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var snap = new Snapshot(
            id, SnapshotTier.Weekly,
            new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            "weekly/2026-05-10.zip", ValidHash,
            byteSize: 1024L,
            includedDatabaseSize: 512L,
            includedVaultSize: 256L);

        await using (var ctx = NewContext())
        {
            ctx.Snapshots.Add(snap);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Snapshots.SingleAsync(s => s.Id == id, Ct);
            loaded.Tier.Should().Be(SnapshotTier.Weekly);
            loaded.FilePath.Should().Be("weekly/2026-05-10.zip");
            loaded.ZipSha256.Should().Be(ValidHash);
            loaded.ByteSize.Should().Be(1024L);
            loaded.IntegrityVerified.Should().BeFalse();
            loaded.IntegrityVerifiedUtc.Should().BeNull();
        }
    }
}
