using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.UI.Identity;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Identity;

public class WpfCurrentUserAccessorTests
{
    private static User NewUser(string username = "alice", string displayName = "Alice Example") =>
        new(
            id: Guid.NewGuid(),
            username: username,
            displayName: displayName,
            passwordHash: "hash",
            passwordSalt: "salt",
            passwordIterationCount: 10_000,
            mustChangePassword: false);

    [Fact]
    public void DefaultState_UserIdIsNullAndNamesAreEmpty()
    {
        var sut = new WpfCurrentUserAccessor();

        sut.UserId.Should().BeNull();
        sut.UserDisplayName.Should().BeEmpty(
            "ICurrentUserAccessor documents empty-string (not null) for the unauthenticated state");
        sut.CurrentRoleName.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_PopulatesAllProperties()
    {
        var sut = new WpfCurrentUserAccessor();
        var user = NewUser(displayName: "M. Rodriguez");

        sut.SetCurrentUser(user, "Quality Manager");

        sut.UserId.Should().Be(user.Id);
        sut.UserDisplayName.Should().Be("M. Rodriguez");
        sut.CurrentRoleName.Should().Be("Quality Manager");
    }

    [Fact]
    public void SetCurrentUser_NullUser_ThrowsArgumentNullException()
    {
        var sut = new WpfCurrentUserAccessor();

        var act = () => sut.SetCurrentUser(null!, "Quality Manager");
        act.Should().Throw<ArgumentNullException>().WithParameterName("user");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCurrentUser_NullOrWhitespaceRoleName_ThrowsArgumentException(string? roleName)
    {
        var sut = new WpfCurrentUserAccessor();
        var user = NewUser();

        var act = () => sut.SetCurrentUser(user, roleName!);
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(roleName));
    }

    [Fact]
    public void Clear_AfterSetCurrentUser_ResetsToDefault()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(NewUser(), "Quality Manager");

        sut.Clear();

        sut.UserId.Should().BeNull();
        sut.UserDisplayName.Should().BeEmpty();
        sut.CurrentRoleName.Should().BeEmpty();
    }

    [Fact]
    public void SetCurrentUser_ReplacesPreviousUser()
    {
        var sut = new WpfCurrentUserAccessor();
        sut.SetCurrentUser(NewUser(username: "alice", displayName: "Alice"), "Operator");

        var second = NewUser(username: "bob", displayName: "Bob");
        sut.SetCurrentUser(second, "Quality Manager");

        sut.UserId.Should().Be(second.Id);
        sut.UserDisplayName.Should().Be("Bob");
        sut.CurrentRoleName.Should().Be("Quality Manager");
    }
}
