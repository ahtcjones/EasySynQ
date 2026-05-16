using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Tests.TestHelpers;
using EasySynQ.UI.Documents.Reviewers;
using EasySynQ.UI.Documents.SubmitForReview;
using EasySynQ.UI.Signing;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.SubmitForReview;

/// <summary>
/// Unit tests for <see cref="SubmitForReviewViewModel"/> (ADR 0008
/// C6b stop 3). Pure VM behavior — no Window construction, no DI
/// graph, no WPF surface. Covers candidate-load shape, OK-can-
/// execute gating, the role-prompter integration paths
/// (single-role auto-pick, multi-role pick, cancel), and the
/// service-call path.
/// </summary>
public class SubmitForReviewViewModelTests
{
    private static readonly DateTime Now =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid RevisionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AliceId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BobId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static User MakeUser(Guid id, string username, string displayName)
        => new(id, username, displayName, "h", "s", 1000, false);

    private sealed record Sut(
        SubmitForReviewViewModel Vm,
        Mock<IUserRepository> Users,
        Mock<ISignatureRolePrompter> RolePrompter,
        Mock<IDocumentLifecycleService> Lifecycle,
        List<bool> CloseCalls);

    private static Sut BuildSut(
        IReadOnlyList<User>? candidateUsers = null,
        string? rolePromptReturn = "QualityAuthor",
        Exception? rolePromptThrows = null,
        Exception? submitThrows = null)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUsersWithPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateUsers ?? Array.Empty<User>());

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
        if (submitThrows is not null)
        {
            lifecycle.Setup(l => l.SubmitForReviewAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(submitThrows);
        }
        else
        {
            lifecycle.Setup(l => l.SubmitForReviewAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentRevision)null!);
        }

        var closes = new List<bool>();
        var vm = new SubmitForReviewViewModel(
            RevisionId,
            users.Object,
            rolePrompter.Object,
            lifecycle.Object,
            new FixedClock(Now),
            ok => closes.Add(ok));

        return new Sut(vm, users, rolePrompter, lifecycle, closes);
    }

    [Fact]
    public void Constructor_EmptyRevisionId_Throws()
    {
        Action act = () => new SubmitForReviewViewModel(
            Guid.Empty,
            Mock.Of<IUserRepository>(),
            Mock.Of<ISignatureRolePrompter>(),
            Mock.Of<IDocumentLifecycleService>(),
            new FixedClock(Now),
            _ => { });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RevisionId*Guid.Empty*");
    }

    [Theory]
    [InlineData("users")]
    [InlineData("rolePrompter")]
    [InlineData("lifecycle")]
    [InlineData("clock")]
    [InlineData("closeDialog")]
    public void Constructor_NullDependency_Throws(string paramName)
    {
        IUserRepository? users = Mock.Of<IUserRepository>();
        ISignatureRolePrompter? rp = Mock.Of<ISignatureRolePrompter>();
        IDocumentLifecycleService? lc = Mock.Of<IDocumentLifecycleService>();
        FixedClock? clk = new(Now);
        Action<bool>? close = _ => { };
        switch (paramName)
        {
            case "users": users = null; break;
            case "rolePrompter": rp = null; break;
            case "lifecycle": lc = null; break;
            case "clock": clk = null; break;
            case "closeDialog": close = null; break;
        }

        Action act = () => new SubmitForReviewViewModel(
            RevisionId, users!, rp!, lc!, clk!, close!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_PickerStartsEmpty_CommandsExist()
    {
        var sut = BuildSut();

        sut.Vm.Picker.Should().NotBeNull();
        sut.Vm.Picker.Candidates.Should().BeEmpty();
        sut.Vm.Picker.SelectedCandidates.Should().BeEmpty();
        sut.Vm.LoadCandidatesCommand.Should().NotBeNull();
        sut.Vm.SubmitCommand.Should().NotBeNull();
        sut.Vm.CancelCommand.Should().NotBeNull();
        sut.Vm.ErrorMessage.Should().BeNull();
        sut.Vm.IsLoadingCandidates.Should().BeFalse();
    }

    [Fact]
    public async Task LoadCandidatesAsync_CallsRepoWithDocumentReviewPermissionAndClockUtcNowAsync()
    {
        var sut = BuildSut();

        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);

        sut.Users.Verify(u => u.GetUsersWithPermissionAsync(
            PermissionNames.DocumentReview,
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadCandidatesAsync_ReplacesPickerWithProjectedCandidatesAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var bob = MakeUser(BobId, "bjohnson", "Bob Johnson");
        var sut = BuildSut(candidateUsers: [alice, bob]);

        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);

        sut.Vm.Picker.Candidates.Should().HaveCount(2);
        sut.Vm.Picker.Candidates[0].Should().Be(
            new ReviewerCandidate(AliceId, "Alice Smith", "asmith"));
        sut.Vm.Picker.Candidates[1].Should().Be(
            new ReviewerCandidate(BobId, "Bob Johnson", "bjohnson"));
    }

    [Fact]
    public async Task LoadCandidatesAsync_RepositoryThrows_PopulatesErrorMessageAsync()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUsersWithPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var vm = new SubmitForReviewViewModel(
            RevisionId,
            users.Object,
            Mock.Of<ISignatureRolePrompter>(),
            Mock.Of<IDocumentLifecycleService>(),
            new FixedClock(Now),
            _ => { });

        await vm.LoadCandidatesCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNull();
        vm.ErrorMessage!.Should().Contain("db down");
        vm.IsLoadingCandidates.Should().BeFalse();
    }

    [Fact]
    public void SubmitCommand_CanExecute_FalseWhenNoReviewersSelected()
    {
        var sut = BuildSut();

        sut.Vm.SubmitCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitCommand_CanExecute_TrueWhenReviewerSelectedAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(candidateUsers: [alice]);
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);

        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);

        sut.Vm.SubmitCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_ResolvesRoleViaPrompterWithDocumentSubmitForReviewPermissionAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(candidateUsers: [alice]);
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);

        await sut.Vm.SubmitCommand.ExecuteAsync(null);

        sut.RolePrompter.Verify(p => p.ResolveSigningRoleAsync(
            PermissionNames.DocumentSubmitForReview,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_CallsLifecycleWithSelectedReviewerIdsAndResolvedRoleAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var bob = MakeUser(BobId, "bjohnson", "Bob Johnson");
        var sut = BuildSut(candidateUsers: [alice, bob], rolePromptReturn: "AuthorRole");
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[1]);

        await sut.Vm.SubmitCommand.ExecuteAsync(null);

        sut.Lifecycle.Verify(l => l.SubmitForReviewAsync(
            RevisionId,
            It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 && ids.Contains(AliceId) && ids.Contains(BobId)),
            null,
            "AuthorRole",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_OnSuccess_ClosesDialogWithTrueAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(candidateUsers: [alice]);
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);

        await sut.Vm.SubmitCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().ContainSingle().Which.Should().BeTrue();
        sut.Vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_RolePrompterCancelled_LeavesDialogOpenWithNoErrorAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(
            candidateUsers: [alice],
            rolePromptThrows: new OperationCanceledException("user cancelled the role picker"));
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);

        await sut.Vm.SubmitCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().BeEmpty();
        sut.Vm.ErrorMessage.Should().BeNull();
        sut.Lifecycle.Verify(l => l.SubmitForReviewAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_LifecycleThrows_PopulatesErrorMessageAndDialogStaysOpenAsync()
    {
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(
            candidateUsers: [alice],
            submitThrows: new InvalidOperationException("revision not in draft"));
        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);
        sut.Vm.Picker.SelectedCandidates.Add(sut.Vm.Picker.Candidates[0]);

        await sut.Vm.SubmitCommand.ExecuteAsync(null);

        sut.CloseCalls.Should().BeEmpty();
        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("revision not in draft");
    }

    [Fact]
    public void CancelCommand_ClosesDialogWithFalse()
    {
        var sut = BuildSut();

        sut.Vm.CancelCommand.Execute(null);

        sut.CloseCalls.Should().ContainSingle().Which.Should().BeFalse();
    }

    [Fact]
    public async Task LoadCandidatesAsync_SetsIsLoadingFlagDuringAndClearsAfterAsync()
    {
        var sut = BuildSut();
        sut.Vm.IsLoadingCandidates.Should().BeFalse();

        await sut.Vm.LoadCandidatesCommand.ExecuteAsync(null);

        sut.Vm.IsLoadingCandidates.Should().BeFalse();
    }
}
