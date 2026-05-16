using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Documents;
using EasySynQ.UI.Documents.ReturnToDraft;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.ReturnToDraft;

/// <summary>
/// Unit tests for <see cref="ReturnToDraftViewModel"/> (ADR 0008
/// C6b stop 5). Pure VM behavior — no Window construction, no DI
/// graph, no role-prompter integration (per the stop-5
/// plan-vs-service reconciliation: ReturnToDraft is not a signed
/// transition).
/// </summary>
public class ReturnToDraftViewModelTests
{
    private static readonly Guid RevisionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed record Sut(
        ReturnToDraftViewModel Vm,
        Mock<IDocumentLifecycleService> Lifecycle,
        List<bool> CloseCalls);

    private static Sut BuildSut(Exception? returnThrows = null)
    {
        var lifecycle = new Mock<IDocumentLifecycleService>();
        if (returnThrows is not null)
        {
            lifecycle.Setup(l => l.ReturnToDraftAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(returnThrows);
        }
        else
        {
            lifecycle.Setup(l => l.ReturnToDraftAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentRevision)null!);
        }

        var closes = new List<bool>();
        var vm = new ReturnToDraftViewModel(
            RevisionId, lifecycle.Object, ok => closes.Add(ok));

        return new Sut(vm, lifecycle, closes);
    }

    [Fact]
    public void Constructor_EmptyRevisionId_Throws()
    {
        Action act = () => new ReturnToDraftViewModel(
            Guid.Empty,
            Mock.Of<IDocumentLifecycleService>(),
            _ => { });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RevisionId*Guid.Empty*");
    }

    [Theory]
    [InlineData("lifecycle")]
    [InlineData("closeDialog")]
    public void Constructor_NullDependency_Throws(string paramName)
    {
        IDocumentLifecycleService? lc = Mock.Of<IDocumentLifecycleService>();
        Action<bool>? close = _ => { };
        switch (paramName)
        {
            case "lifecycle": lc = null; break;
            case "closeDialog": close = null; break;
        }

        Action act = () => new ReturnToDraftViewModel(RevisionId, lc!, close!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ReasonStartsEmpty_ReturnCannotExecute()
    {
        var sut = BuildSut();

        sut.Vm.Reason.Should().BeEmpty();
        sut.Vm.ReturnCommand.CanExecute(null).Should().BeFalse();
        sut.Vm.CancelCommand.Should().NotBeNull();
        sut.Vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ReturnCommand_CanExecute_FalseForEmptyOrWhitespaceReason(string reason)
    {
        var sut = BuildSut();

        sut.Vm.Reason = reason;

        sut.Vm.ReturnCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ReturnCommand_CanExecute_TrueForNonWhitespaceReason()
    {
        var sut = BuildSut();

        sut.Vm.Reason = "needs more detail in section 3";

        sut.Vm.ReturnCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ReturnAsync_CallsLifecycleWithRevisionIdAndReasonAsync()
    {
        var sut = BuildSut();
        sut.Vm.Reason = "needs section 3 fix";

        await sut.Vm.ReturnCommand.ExecuteAsync(null);

        sut.Lifecycle.Verify(l => l.ReturnToDraftAsync(
            RevisionId, "needs section 3 fix", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReturnAsync_OnSuccess_ClosesDialogWithTrueAsync()
    {
        var sut = BuildSut();
        sut.Vm.Reason = "reason";

        await sut.Vm.ReturnCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().ContainSingle().Which.Should().BeTrue();
        sut.Vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ReturnAsync_LifecycleThrows_PopulatesErrorMessageAndDialogStaysOpenAsync()
    {
        var sut = BuildSut(returnThrows: new InvalidOperationException("revision not in InReview"));
        sut.Vm.Reason = "reason";

        await sut.Vm.ReturnCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().BeEmpty();
        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("revision not in InReview");
    }

    [Fact]
    public void CancelCommand_ClosesDialogWithFalse()
    {
        var sut = BuildSut();

        sut.Vm.CancelCommand.Execute(null);

        sut.CloseCalls.Should().ContainSingle().Which.Should().BeFalse();
    }
}
