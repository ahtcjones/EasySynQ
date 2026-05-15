using AwesomeAssertions;

using EasySynQ.UI.Signing;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Signing;

/// <summary>
/// Unit tests for <see cref="SignAsRoleViewModel"/> (ADR 0009 C4).
/// Pure VM behavior — no Window construction, no DI graph.
/// </summary>
public class SignAsRoleViewModelTests
{
    private static SignAsRoleViewModel NewVm(IReadOnlyList<string> roles, Action<bool>? close = null)
        => new(roles, close ?? (_ => { }));

    [Fact]
    public void Constructor_ExposesRolesInOrder()
    {
        var roles = new[] { "QualityManager", "AuditLead", "PlantManager" };

        var vm = NewVm(roles);

        vm.Roles.Should().Equal(roles);
    }

    [Fact]
    public void SelectedRole_StartsNull_OkCommandCannotExecute()
    {
        var vm = NewVm(["A", "B"]);

        vm.SelectedRole.Should().BeNull();
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectedRoleSet_OkCommandCanExecute()
    {
        var vm = NewVm(["A", "B"]);

        vm.SelectedRole = "A";

        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_AlwaysCanExecute()
    {
        var vm = NewVm(["A"]);

        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.SelectedRole = "A";
        vm.CancelCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkCommand_InvokesCloseWithTrue_WhenSelectionMade()
    {
        bool? captured = null;
        var vm = NewVm(["A"], ok => captured = ok);
        vm.SelectedRole = "A";

        vm.OkCommand.Execute(null);

        captured.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_InvokesCloseWithFalse()
    {
        bool? captured = null;
        var vm = NewVm(["A"], ok => captured = ok);

        vm.CancelCommand.Execute(null);

        captured.Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsEmptyRoles()
    {
        // Defensive — the prompter is responsible for the empty-
        // eligible-roles throw before constructing the VM.
        Action act = () => NewVm([]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Roles must not be empty*");
    }

    [Fact]
    public void Constructor_RejectsNullRoles()
    {
        Action act = () => new SignAsRoleViewModel(null!, _ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullCloseCallback()
    {
        Action act = () => new SignAsRoleViewModel(["A"], null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
