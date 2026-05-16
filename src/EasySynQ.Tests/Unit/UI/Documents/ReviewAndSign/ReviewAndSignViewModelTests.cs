using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.UI.Documents.ReviewAndSign;
using EasySynQ.UI.Signing;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.ReviewAndSign;

/// <summary>
/// Unit tests for <see cref="ReviewAndSignViewModel"/> (ADR 0008
/// C6b stop 4). Pure VM behavior — no Window construction, no DI
/// graph. Covers role-resolution paths (single-role auto-pick,
/// multi-role pick simulated by a returning prompter, cancel
/// closes the dialog), the sign-call path, and post-sign state
/// surface (last-signer Approved vs not-last-signer InReview).
/// </summary>
public class ReviewAndSignViewModelTests
{
    private static readonly Guid RevisionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string DocumentTitle = "Test SOP";
    private const string RevisionLabel = "Rev A";

    private static DocumentRevision MakeRevision(DocumentLifecycle lifecycle)
    {
        var rev = new DocumentRevision(
            RevisionId, Guid.NewGuid(), RevisionLabel, Guid.NewGuid());
        // Drive the revision to the requested lifecycle.
        if (lifecycle == DocumentLifecycle.Draft)
        {
            return rev;
        }
        rev.Submit(Guid.NewGuid(), new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));
        if (lifecycle == DocumentLifecycle.InReview)
        {
            return rev;
        }
        rev.Approve(new DateTime(2026, 5, 16, 12, 1, 0, DateTimeKind.Utc));
        return rev;
    }

    private sealed record Sut(
        ReviewAndSignViewModel Vm,
        Mock<ISignatureRolePrompter> RolePrompter,
        Mock<IDocumentLifecycleService> Lifecycle,
        Mock<IDocumentRevisionRepository> Revisions,
        List<bool> CloseCalls);

    private static Sut BuildSut(
        string? rolePromptReturn = "Reviewer",
        Exception? rolePromptThrows = null,
        Exception? signThrows = null,
        DocumentRevision? postSignRevision = null)
    {
        var rolePrompter = new Mock<ISignatureRolePrompter>();
        if (rolePromptThrows is not null)
        {
            rolePrompter.Setup(p => p.ResolveSigningRoleAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(rolePromptThrows);
        }
        else
        {
            rolePrompter.Setup(p => p.ResolveSigningRoleAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rolePromptReturn!);
        }

        var lifecycle = new Mock<IDocumentLifecycleService>();
        if (signThrows is not null)
        {
            lifecycle.Setup(l => l.SignAsReviewerAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(signThrows);
        }
        else
        {
            lifecycle.Setup(l => l.SignAsReviewerAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentReviewAssignment)null!);
        }

        var revisions = new Mock<IDocumentRevisionRepository>();
        revisions.Setup(r => r.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(postSignRevision);

        var closes = new List<bool>();
        var vm = new ReviewAndSignViewModel(
            RevisionId,
            DocumentTitle,
            RevisionLabel,
            rolePrompter.Object,
            lifecycle.Object,
            revisions.Object,
            ok => closes.Add(ok));

        return new Sut(vm, rolePrompter, lifecycle, revisions, closes);
    }

    [Fact]
    public void Constructor_EmptyRevisionId_Throws()
    {
        Action act = () => new ReviewAndSignViewModel(
            Guid.Empty,
            DocumentTitle,
            RevisionLabel,
            Mock.Of<ISignatureRolePrompter>(),
            Mock.Of<IDocumentLifecycleService>(),
            Mock.Of<IDocumentRevisionRepository>(),
            _ => { });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RevisionId*Guid.Empty*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceDocumentTitle_Throws(string? title)
    {
        Action act = () => new ReviewAndSignViewModel(
            RevisionId,
            title!,
            RevisionLabel,
            Mock.Of<ISignatureRolePrompter>(),
            Mock.Of<IDocumentLifecycleService>(),
            Mock.Of<IDocumentRevisionRepository>(),
            _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceRevisionLabel_Throws(string? label)
    {
        Action act = () => new ReviewAndSignViewModel(
            RevisionId,
            DocumentTitle,
            label!,
            Mock.Of<ISignatureRolePrompter>(),
            Mock.Of<IDocumentLifecycleService>(),
            Mock.Of<IDocumentRevisionRepository>(),
            _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ResolvedRoleStartsNull_SignCannotExecute()
    {
        var sut = BuildSut();

        sut.Vm.ResolvedRole.Should().BeNull();
        sut.Vm.IsRoleResolved.Should().BeFalse();
        sut.Vm.ConfirmationMessage.Should().BeEmpty();
        sut.Vm.SignCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRoleAsync_CallsPrompterWithDocumentReviewPermissionAsync()
    {
        var sut = BuildSut();

        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        sut.RolePrompter.Verify(p => p.ResolveSigningRoleAsync(
            PermissionNames.DocumentReview,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveRoleAsync_SingleRoleAutoPickPath_RoleResolvedAndConfirmationRenderedAsync()
    {
        // The prompter's contract: single eligible role → returns
        // it instantly without showing a picker dialog. We
        // simulate that with a returning mock; the VM cannot
        // distinguish auto-pick from a fast picker.
        var sut = BuildSut(rolePromptReturn: "QualityReviewer");

        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        sut.Vm.ResolvedRole.Should().Be("QualityReviewer");
        sut.Vm.IsRoleResolved.Should().BeTrue();
        sut.Vm.ConfirmationMessage.Should().Be(
            $"Sign as reviewer of {DocumentTitle} {RevisionLabel}? Signing as QualityReviewer.");
        sut.Vm.SignCommand.CanExecute(null).Should().BeTrue();
        sut.CloseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveRoleAsync_MultiRolePickPath_RoleResolvedToUsersPickAsync()
    {
        // Multi-role: the prompter shows the picker and returns
        // whatever the user picked. From the VM's perspective
        // it's the same call; we just supply a different return
        // value to confirm the VM forwards it verbatim.
        var sut = BuildSut(rolePromptReturn: "AuditLead");

        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        sut.Vm.ResolvedRole.Should().Be("AuditLead");
        sut.Vm.ConfirmationMessage.Should().Contain("Signing as AuditLead.");
    }

    [Fact]
    public async Task ResolveRoleAsync_RolePickerCancelled_AutoClosesDialogAsync()
    {
        var sut = BuildSut(
            rolePromptThrows: new OperationCanceledException("picker cancelled"));

        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        sut.Vm.ResolvedRole.Should().BeNull();
        sut.CloseCalls.Should().ContainSingle().Which.Should().BeFalse();
        sut.Vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ResolveRoleAsync_PrompterThrowsOther_PopulatesErrorMessageAsync()
    {
        var sut = BuildSut(
            rolePromptThrows: new InvalidOperationException("no eligible roles"));

        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        sut.Vm.ResolvedRole.Should().BeNull();
        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("no eligible roles");
        sut.CloseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task SignAsync_CallsLifecycleWithRevisionIdAndResolvedRoleAsync()
    {
        var sut = BuildSut(rolePromptReturn: "Reviewer");
        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        await sut.Vm.SignCommand.ExecuteAsync(null);

        sut.Lifecycle.Verify(l => l.SignAsReviewerAsync(
            RevisionId, "Reviewer", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignAsync_LastSignerPath_PostSignRevisionIsApprovedAsync()
    {
        var approvedRevision = MakeRevision(DocumentLifecycle.Approved);
        var sut = BuildSut(postSignRevision: approvedRevision);
        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        await sut.Vm.SignCommand.ExecuteAsync(null);

        sut.Vm.PostSignRevision.Should().NotBeNull();
        sut.Vm.PostSignRevision!.Lifecycle.Should().Be(DocumentLifecycle.Approved);
        sut.CloseCalls.Should().ContainSingle().Which.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_NotLastSignerPath_PostSignRevisionStaysInReviewAsync()
    {
        var inReviewRevision = MakeRevision(DocumentLifecycle.InReview);
        var sut = BuildSut(postSignRevision: inReviewRevision);
        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        await sut.Vm.SignCommand.ExecuteAsync(null);

        sut.Vm.PostSignRevision.Should().NotBeNull();
        sut.Vm.PostSignRevision!.Lifecycle.Should().Be(DocumentLifecycle.InReview);
        sut.CloseCalls.Should().ContainSingle().Which.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_LifecycleThrows_PopulatesErrorMessageAndDialogStaysOpenAsync()
    {
        var sut = BuildSut(
            signThrows: new InvalidOperationException("already signed"));
        await sut.Vm.ResolveRoleCommand.ExecuteAsync(null);

        await sut.Vm.SignCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().BeEmpty();
        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("already signed");
        sut.Vm.PostSignRevision.Should().BeNull();
    }

    [Fact]
    public void CancelCommand_ClosesDialogWithFalse()
    {
        var sut = BuildSut();

        sut.Vm.CancelCommand.Execute(null);

        sut.CloseCalls.Should().ContainSingle().Which.Should().BeFalse();
    }
}
