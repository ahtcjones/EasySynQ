using AwesomeAssertions;

using EasySynQ.Services.Authorization;
using EasySynQ.Tests.TestHelpers;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.Authorization;

/// <summary>
/// Unit tests for <see cref="RoleResolutionService.GetEligibleRolesForPermission"/>
/// (ADR 0009 C4). Pure in-memory; no DI graph beyond the
/// <see cref="MutableCurrentUserAccessor"/> test double.
/// </summary>
public class RoleResolutionServiceTests
{
    private static RoleResolutionService NewService(MutableCurrentUserAccessor accessor)
        => new(accessor);

    [Fact]
    public void OneRole_HoldingThePermission_ReturnsThatRole()
    {
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = ["QualityManager"],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["QualityManager"] = ["Document.Review"],
            },
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Document.Review");

        result.Should().Equal("QualityManager");
    }

    [Fact]
    public void OneRole_NotHoldingThePermission_ReturnsEmpty()
    {
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = ["QualityManager"],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["QualityManager"] = ["Document.Review"],
            },
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Document.Approve");

        result.Should().BeEmpty();
    }

    [Fact]
    public void TwoRoles_OnlyOneHoldsThePermission_ReturnsTheOne()
    {
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = ["QualityManager", "MaintenanceManager"],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["QualityManager"] = ["Document.Review"],
                ["MaintenanceManager"] = ["Asset.Calibrate"],
            },
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Document.Review");

        result.Should().Equal("QualityManager");
    }

    [Fact]
    public void TwoRoles_BothHoldThePermission_ReturnsBothInOrdinalOrder()
    {
        // Build the dict with deliberately reversed insertion order so
        // the test would fail if the service relied on dictionary
        // iteration order rather than sorting ordinal.
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = ["QualityManager", "AuditLead"],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["QualityManager"] = ["Document.Review"],
                ["AuditLead"] = ["Document.Review"],
            },
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Document.Review");

        // Ordinal sort: "AuditLead" < "QualityManager".
        result.Should().Equal("AuditLead", "QualityManager");
    }

    [Fact]
    public void ZeroRoles_ReturnsEmpty()
    {
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = [],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(),
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Document.Review");

        result.Should().BeEmpty();
    }

    [Fact]
    public void PermissionNameNotInAnyRolesValueCollection_ReturnsEmpty_NoThrow()
    {
        // Per ADR Required Tests: "Permission name that doesn't exist
        // in catalog returns empty (not throw — the user simply has
        // no eligible role)." Service does not consult a catalog; the
        // contract is "if no role contains it, return empty."
        var accessor = new MutableCurrentUserAccessor
        {
            Roles = ["QualityManager"],
            RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["QualityManager"] = ["Document.Review"],
            },
        };
        var sut = NewService(accessor);

        var result = sut.GetEligibleRolesForPermission("Permission.That.Does.Not.Exist.In.Catalog");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetEligibleRolesForPermission_RejectsNullOrWhitespacePermissionName()
    {
        var accessor = new MutableCurrentUserAccessor();
        var sut = NewService(accessor);

        Action actNull = () => sut.GetEligibleRolesForPermission(null!);
        Action actEmpty = () => sut.GetEligibleRolesForPermission(string.Empty);
        Action actSpace = () => sut.GetEligibleRolesForPermission("   ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actSpace.Should().Throw<ArgumentException>();
    }
}
