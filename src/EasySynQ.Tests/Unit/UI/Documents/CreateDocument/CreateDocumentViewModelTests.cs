using AwesomeAssertions;

using EasySynQ.UI.Documents;
using EasySynQ.UI.Documents.CreateDocument;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.CreateDocument;

/// <summary>
/// Unit tests for <see cref="CreateDocumentViewModel"/> (ADR 0008
/// C6a). Pure VM behavior — no Window construction.
/// </summary>
public class CreateDocumentViewModelTests
{
    private static CreateDocumentViewModel NewVm(
        IFilePicker? picker = null,
        Action<bool>? close = null)
        => new(
            picker ?? Mock.Of<IFilePicker>(),
            close ?? (_ => { }));

    [Fact]
    public void NumberAndTitle_StartEmpty_OkCannotExecute()
    {
        var vm = NewVm();

        vm.Number.Should().Be(string.Empty);
        vm.Title.Should().Be(string.Empty);
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("   ", "Title")]
    [InlineData("Number", "")]
    [InlineData("Number", "   ")]
    public void OkCannotExecute_WhenEitherEmptyOrWhitespace(string number, string title)
    {
        var vm = NewVm();
        vm.Number = number;
        vm.Title = title;

        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OkCanExecute_WhenBothNonEmpty()
    {
        var vm = NewVm();
        vm.Number = "SOP-001";
        vm.Title = "Procedure";

        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_AlwaysCanExecute()
    {
        var vm = NewVm();
        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.Number = "SOP";
        vm.Title = "Title";
        vm.CancelCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkCommand_InvokesCloseWithTrue()
    {
        bool? captured = null;
        var vm = NewVm(close: ok => captured = ok);
        vm.Number = "SOP-1";
        vm.Title = "Test";

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

    [Fact]
    public void PickPdfCommand_InvokesFilePicker_UpdatesSelectedPathOnPick()
    {
        var picker = new Mock<IFilePicker>();
        picker.Setup(p => p.PickFile(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(@"C:\docs\test.pdf");
        var vm = NewVm(picker: picker.Object);

        vm.PickPdfCommand.Execute(null);

        vm.SelectedPdfPath.Should().Be(@"C:\docs\test.pdf");
        picker.Verify(
            p => p.PickFile(
                It.Is<string>(s => !string.IsNullOrEmpty(s)),
                It.Is<string>(f => f.Contains("*.pdf", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void PickPdfCommand_PickerReturnsNull_LeavesSelectedPathUnchanged()
    {
        var picker = new Mock<IFilePicker>();
        picker.Setup(p => p.PickFile(It.IsAny<string>(), It.IsAny<string>()))
              .Returns((string?)null);
        var vm = NewVm(picker: picker.Object);
        vm.SelectedPdfPath = "pre-existing-path.pdf";

        vm.PickPdfCommand.Execute(null);

        vm.SelectedPdfPath.Should().Be("pre-existing-path.pdf",
            "cancel from the file picker must not clear a previously-picked path");
    }

    [Fact]
    public void Constructor_RejectsNullFilePicker()
    {
        Action act = () => new CreateDocumentViewModel(null!, _ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullCloseCallback()
    {
        Action act = () => new CreateDocumentViewModel(Mock.Of<IFilePicker>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
