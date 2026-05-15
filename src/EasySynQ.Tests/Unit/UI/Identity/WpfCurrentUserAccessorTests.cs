using AwesomeAssertions;

using EasySynQ.UI.Identity;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Identity;

public class WpfCurrentUserAccessorTests
{
    private static readonly Guid SampleUserId = new("a1111111-1111-1111-1111-111111111111");

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyRolePerms =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

    private static Dictionary<string, IReadOnlyCollection<string>> Map(
        params (string Role, string[] Perms)[] entries)
    {
        var dict = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var (role, perms) in entries)
        {
            dict[role] = perms;
        }
        return dict;
    }

    [Fact]
    public void DefaultState_UserIdIsNull_StringsEmpty_CollectionsEmptyNotNull()
    {
        var sut = new WpfCurrentUserAccessor();

        sut.UserId.Should().BeNull();
        sut.Username.Should().BeEmpty(
            "ICurrentUserAccessor documents empty-string (not null) for the unauthenticated state");
        sut.DisplayName.Should().BeEmpty();
        sut.Roles.Should().NotBeNull();
        sut.Roles.Should().BeEmpty();
        sut.Permissions.Should().NotBeNull();
        sut.Permissions.Should().BeEmpty();
        // ADR 0009 — RolePermissions empty-state contract.
        sut.RolePermissions.Should().NotBeNull();
        sut.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_PopulatesAllProperties()
    {
        var sut = new WpfCurrentUserAccessor();
        var rolePerms = Map(("Quality Manager", ["Document.Approve", "AuditLog.Read"]));

        sut.SetCurrentUser(
            userId: SampleUserId,
            username: "mrodriguez",
            displayName: "M. Rodriguez",
            roles: ["Quality Manager"],
            permissions: ["Document.Approve", "AuditLog.Read"],
            rolePermissions: rolePerms);

        sut.UserId.Should().Be(SampleUserId);
        sut.Username.Should().Be("mrodriguez");
        sut.DisplayName.Should().Be("M. Rodriguez");
        sut.Roles.Should().BeEquivalentTo("Quality Manager");
        sut.Permissions.Should().BeEquivalentTo("Document.Approve", "AuditLog.Read");
        sut.RolePermissions.Should().BeSameAs(rolePerms);
    }

    [Fact]
    public void SetCurrentUser_AcceptsEmptyRolesAndPermissions()
    {
        // ADR 0007: a user with no roles or permissions is a legitimate
        // (if unproducible-in-Phase-1) state — the accessor must
        // round-trip empty collections without rejecting them.
        var sut = new WpfCurrentUserAccessor();

        sut.SetCurrentUser(
            userId: SampleUserId,
            username: "alice",
            displayName: "Alice",
            roles: [],
            permissions: [],
            rolePermissions: EmptyRolePerms);

        sut.UserId.Should().Be(SampleUserId);
        sut.Roles.Should().BeEmpty();
        sut.Permissions.Should().BeEmpty();
        sut.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_EmptyUserId_ThrowsArgumentException()
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: Guid.Empty,
            username: "alice",
            displayName: "Alice",
            roles: [],
            permissions: [],
            rolePermissions: EmptyRolePerms);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCurrentUser_NullOrWhitespaceUsername_ThrowsArgumentException(string? username)
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: SampleUserId,
            username: username!,
            displayName: "Alice",
            roles: [],
            permissions: [],
            rolePermissions: EmptyRolePerms);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(username));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCurrentUser_NullOrWhitespaceDisplayName_ThrowsArgumentException(string? displayName)
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: SampleUserId,
            username: "alice",
            displayName: displayName!,
            roles: [],
            permissions: [],
            rolePermissions: EmptyRolePerms);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(displayName));
    }

    [Fact]
    public void SetCurrentUser_NullRoles_ThrowsArgumentNullException()
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: SampleUserId,
            username: "alice",
            displayName: "Alice",
            roles: null!,
            permissions: [],
            rolePermissions: EmptyRolePerms);

        act.Should().Throw<ArgumentNullException>().WithParameterName("roles");
    }

    [Fact]
    public void SetCurrentUser_NullPermissions_ThrowsArgumentNullException()
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: SampleUserId,
            username: "alice",
            displayName: "Alice",
            roles: [],
            permissions: null!,
            rolePermissions: EmptyRolePerms);

        act.Should().Throw<ArgumentNullException>().WithParameterName("permissions");
    }

    [Fact]
    public void SetCurrentUser_NullRolePermissions_ThrowsArgumentNullException()
    {
        var sut = new WpfCurrentUserAccessor();
        var act = () => sut.SetCurrentUser(
            userId: SampleUserId,
            username: "alice",
            displayName: "Alice",
            roles: [],
            permissions: [],
            rolePermissions: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("rolePermissions");
    }

    [Fact]
    public void Clear_AfterSetCurrentUser_ResetsToDefault()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(SampleUserId, "alice", "Alice", ["Operator"], ["X.Y"], Map(("Operator", ["X.Y"])));

        sut.Clear();

        sut.UserId.Should().BeNull();
        sut.Username.Should().BeEmpty();
        sut.DisplayName.Should().BeEmpty();
        sut.Roles.Should().BeEmpty();
        sut.Permissions.Should().BeEmpty();
        sut.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_ReplacesPreviousSnapshot()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(Guid.NewGuid(), "alice", "Alice", ["Operator"], ["X.Y"], Map(("Operator", ["X.Y"])));

        var newId = Guid.NewGuid();
        var newRolePerms = Map(("Quality Manager", ["A.B", "C.D"]));
        sut.SetCurrentUser(newId, "bob", "Bob", ["Quality Manager"], ["A.B", "C.D"], newRolePerms);

        sut.UserId.Should().Be(newId);
        sut.Username.Should().Be("bob");
        sut.DisplayName.Should().Be("Bob");
        sut.Roles.Should().BeEquivalentTo("Quality Manager");
        sut.Permissions.Should().BeEquivalentTo("A.B", "C.D");
        sut.RolePermissions.Should().BeSameAs(newRolePerms);
    }
}
