using AwesomeAssertions;

using EasySynQ.UI.Documents.EditMetadata;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.EditMetadata;

/// <summary>
/// Unit tests for <see cref="EditMetadataViewModel"/> (ADR 0008 C6a).
/// Pure VM behavior — no Window construction.
/// </summary>
public class EditMetadataViewModelTests
{
    private static EditMetadataViewModel NewVm(
        string number = "SOP-001",
        string title = "Initial Title",
        Action<bool>? close = null)
        => new(number, title, close ?? (_ => { }));

    [Fact]
    public void Constructor_PrePopulatesNumberAndTitle()
    {
        var vm = NewVm("SOP-A", "Title A");

        vm.Number.Should().Be("SOP-A");
        vm.Title.Should().Be("Title A");
    }

    [Fact]
    public void OkCanExecute_WhenBothNonEmpty()
    {
        var vm = NewVm();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("   ", "Title")]
    [InlineData("Number", "")]
    [InlineData("Number", "   ")]
    public void OkCannotExecute_WhenEitherEmpty(string number, string title)
    {
        var vm = NewVm();
        vm.Number = number;
        vm.Title = title;

        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CancelCommand_AlwaysCanExecute()
    {
        var vm = NewVm();
        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.Number = "";
        vm.CancelCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkCommand_InvokesCloseWithTrue()
    {
        bool? captured = null;
        var vm = NewVm(close: ok => captured = ok);

        vm.OkCommand.Execute(null);

        captured.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_InvokesCloseWithFalse()
    {
        bool? captured = null;
        var vm = NewVm(close: ok => captured = ok);

        vm.CancelCommand.Execute(null);

        captured.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("   ", "Title")]
    [InlineData("Number", "")]
    [InlineData("Number", "   ")]
    public void Constructor_RejectsEmptyOrWhitespaceArgs(string number, string title)
    {
        Action act = () => new EditMetadataViewModel(number, title, _ => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNullCloseCallback()
    {
        Action act = () => new EditMetadataViewModel("SOP-1", "Title", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
