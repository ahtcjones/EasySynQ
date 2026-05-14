using AwesomeAssertions;

using EasySynQ.UI.Identity;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Identity;

public class WpfCurrentUserAccessorTests
{
    private static readonly Guid SampleUserId = new("a1111111-1111-1111-1111-111111111111");

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
    }

    [Fact]
    public void SetCurrentUser_PopulatesAllProperties()
    {
        var sut = new WpfCurrentUserAccessor();

        sut.SetCurrentUser(
            userId: SampleUserId,
            username: "mrodriguez",
            displayName: "M. Rodriguez",
            roles: ["Quality Manager"],
            permissions: ["Document.Approve", "AuditLog.Read"]);

        sut.UserId.Should().Be(SampleUserId);
        sut.Username.Should().Be("mrodriguez");
        sut.DisplayName.Should().Be("M. Rodriguez");
        sut.Roles.Should().BeEquivalentTo("Quality Manager");
        sut.Permissions.Should().BeEquivalentTo("Document.Approve", "AuditLog.Read");
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
            permissions: []);

        sut.UserId.Should().Be(SampleUserId);
        sut.Roles.Should().BeEmpty();
        sut.Permissions.Should().BeEmpty();
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
            permissions: []);

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
            permissions: []);

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
            permissions: []);

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
            permissions: []);

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
            permissions: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("permissions");
    }

    [Fact]
    public void Clear_AfterSetCurrentUser_ResetsToDefault()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(SampleUserId, "alice", "Alice", ["Operator"], ["X.Y"]);

        sut.Clear();

        sut.UserId.Should().BeNull();
        sut.Username.Should().BeEmpty();
        sut.DisplayName.Should().BeEmpty();
        sut.Roles.Should().BeEmpty();
        sut.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_ReplacesPreviousSnapshot()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(Guid.NewGuid(), "alice", "Alice", ["Operator"], ["X.Y"]);

        var newId = Guid.NewGuid();
        sut.SetCurrentUser(newId, "bob", "Bob", ["Quality Manager"], ["A.B", "C.D"]);

        sut.UserId.Should().Be(newId);
        sut.Username.Should().Be("bob");
        sut.DisplayName.Should().Be("Bob");
        sut.Roles.Should().BeEquivalentTo("Quality Manager");
        sut.Permissions.Should().BeEquivalentTo("A.B", "C.D");
    }
}
