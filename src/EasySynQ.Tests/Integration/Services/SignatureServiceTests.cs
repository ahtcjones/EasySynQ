using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Signatures;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services;

public class SignatureServiceTests : ServiceIntegrationTestBase
{
    [Fact]
    public async Task Sign_ProducesSignature_WithCurrentUserRoleTimestampAndPayloadHashAsync()
    {
        var userId = Guid.NewGuid();
        CurrentUser.UserId = userId;
        CurrentUser.DisplayName = "M. Rodriguez";
        CurrentUser.Roles = ["QualityManager"];

        const string payload = "doc:DR-2026-001:v3";
        var expectedHash = Sha256Lower(payload);

        Signature signature;
        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            signature = await sig.SignAsync("DocumentRevision", "DR-2026-001", payload, "QualityManager", Ct);
        }

        signature.UtcTimestamp.Should().Be(Clock.UtcNow);
        signature.RoleAtTimeOfSign.Should().Be("QualityManager");
        signature.SignedEntityType.Should().Be("DocumentRevision");
        signature.SignedEntityId.Should().Be("DR-2026-001");
        signature.PayloadHash.Should().Be(expectedHash);
        // The standard-fields interceptor stamps CreatedBy from the
        // current user — that is the signer's identity per ADR 0006 +
        // Signature entity remarks.
        signature.CreatedBy.Should().Be(userId.ToString());
    }

    [Fact]
    public async Task Verify_ReturnsTrue_ForOriginalPayload_AndFalse_ForModifiedAsync()
    {
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Roles = ["LabTech"];

        Signature signature;
        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            signature = await sig.SignAsync("Reading", "R-001", "value=36.4HRC", "LabTech", Ct);
        }

        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            (await sig.VerifyAsync(signature, "value=36.4HRC", Ct)).Should().BeTrue();
            (await sig.VerifyAsync(signature, "value=42.0HRC", Ct)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Sign_WithNoCurrentUser_ThrowsInvalidOperationAsync()
    {
        // CurrentUser default state — UserId is null.
        CurrentUser.Roles = ["QualityManager"];

        await using var scope = NewScope();
        var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();

        var act = async () => await sig.SignAsync("X", "Y", "payload", "QualityManager", Ct);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot sign anonymously*");
    }

    [Fact]
    public async Task Sign_WithRoleNotHeld_ThrowsInvalidOperationAsync()
    {
        // ADR 0009 — replaces the prior Roles.Single() throw. The
        // signing call site (UI prompter) must pass a role the user
        // actually holds; passing one they don't is a programming
        // error and surfaces with a clear message.
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Roles = ["QualityManager"];

        await using var scope = NewScope();
        var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();

        var act = async () => await sig.SignAsync("X", "Y", "payload", "Administrator", Ct);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not hold role 'Administrator'*");
    }

    [Fact]
    public async Task Sign_MultiRoleUser_WithValidSigningRole_SucceedsAsync()
    {
        // ADR 0009 — multi-role users now sign successfully when they
        // pass a role they actually hold. The role passed becomes the
        // RoleAtTimeOfSign verbatim.
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Roles = ["QualityManager", "Auditor"];

        Signature signature;
        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            signature = await sig.SignAsync("DocumentRevision", "DR-99", "p", "Auditor", Ct);
        }

        signature.RoleAtTimeOfSign.Should().Be("Auditor");
    }

    [Fact]
    public async Task Sign_WritesAuditLogEntry_AndInterceptorPipelineFiresAsync()
    {
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Roles = ["QualityManager"];

        Signature signature;
        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            signature = await sig.SignAsync("DocumentRevision", "DR-99", "payload-XYZ", "QualityManager", Ct);
        }

        await using var ctx = NewContext();
        var entries = await ctx.AuditLogEntries
            .Where(e => e.EntityTypeName == "Signature" && e.EntityId == signature.Id.ToString())
            .ToListAsync(Ct);

        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.Action.Should().Be(AuditAction.Insert);
        entry.Before.Should().BeNull();
        entry.After.Should().NotBeNullOrEmpty();
        entry.UserId.Should().Be(CurrentUser.UserId);
    }

    [Fact]
    public async Task Sign_RoleAtTimeOfSign_IsSnapshot_NotLiveAsync()
    {
        // Set the current role to "QualityManager" and sign.
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Roles = ["QualityManager"];

        Signature signature;
        await using (var scope = NewScope())
        {
            var sig = scope.ServiceProvider.GetRequiredService<ISignatureService>();
            signature = await sig.SignAsync("CoC", "J-2026-0847", "release-payload", "QualityManager", Ct);
        }

        // Mutate the current user's roles AFTER signing — simulates the
        // session-snapshot accessor being repopulated for a different
        // sign-in. The captured RoleAtTimeOfSign on the persisted row
        // must not move.
        CurrentUser.Roles = ["Auditor"];

        // Reload the signature from the DB and confirm the stored
        // role is the role-at-time-of-sign, NOT the current value.
        await using var ctx = NewContext();
        var loaded = await ctx.Signatures.SingleAsync(s => s.Id == signature.Id, Ct);
        loaded.RoleAtTimeOfSign.Should().Be("QualityManager");
    }

    private static string Sha256Lower(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
