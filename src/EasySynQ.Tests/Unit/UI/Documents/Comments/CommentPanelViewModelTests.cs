using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Tests.TestHelpers;
using EasySynQ.UI.Documents.Comments;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.Comments;

/// <summary>
/// Unit tests for <see cref="CommentPanelViewModel"/> (ADR 0008
/// C6b stop 6). Pure VM behavior — no UserControl construction,
/// no DI graph. Covers the load → project → render shape and the
/// Add-command gate matrix (permission × non-whitespace text).
/// </summary>
public class CommentPanelViewModelTests
{
    private static readonly Guid RevisionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AliceId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BobId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GhostId =
        Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static DateTime Utc(int hour) =>
        new(2026, 5, 16, hour, 0, 0, DateTimeKind.Utc);

    private static User MakeUser(Guid id, string username, string display)
        => new(id, username, display, "h", "s", 1000, false);

    private static DocumentReviewComment MakeComment(
        Guid authorId, string body, DateTime createdAtUtc)
        => new(Guid.NewGuid(), RevisionId, authorId, body, createdAtUtc);

    private sealed record Sut(
        CommentPanelViewModel Vm,
        Mock<IDocumentReviewCommentRepository> Comments,
        Mock<IUserRepository> Users,
        Mock<IDocumentLifecycleService> Lifecycle,
        MutableCurrentUserAccessor CurrentUser);

    private static Sut BuildSut(
        IReadOnlyList<DocumentReviewComment>? loadedComments = null,
        IReadOnlyList<User>? loadedUsers = null,
        bool hasReviewPermission = true,
        Exception? loadThrows = null,
        Exception? addThrows = null)
    {
        var comments = new Mock<IDocumentReviewCommentRepository>();
        if (loadThrows is not null)
        {
            comments.Setup(c => c.GetByRevisionIdAsync(
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(loadThrows);
        }
        else
        {
            comments.Setup(c => c.GetByRevisionIdAsync(
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(loadedComments ?? Array.Empty<DocumentReviewComment>());
        }

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loadedUsers ?? Array.Empty<User>());

        var lifecycle = new Mock<IDocumentLifecycleService>();
        if (addThrows is not null)
        {
            lifecycle.Setup(l => l.AddCommentAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(addThrows);
        }
        else
        {
            lifecycle.Setup(l => l.AddCommentAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentReviewComment)null!);
        }

        var currentUser = new MutableCurrentUserAccessor
        {
            UserId = AliceId,
            Username = "alice",
            Permissions = hasReviewPermission
                ? new[] { PermissionNames.DocumentReview }
                : Array.Empty<string>(),
        };

        var vm = new CommentPanelViewModel(
            RevisionId,
            comments.Object,
            users.Object,
            lifecycle.Object,
            currentUser);

        return new Sut(vm, comments, users, lifecycle, currentUser);
    }

    [Fact]
    public void Constructor_EmptyRevisionId_Throws()
    {
        Action act = () => new CommentPanelViewModel(
            Guid.Empty,
            Mock.Of<IDocumentReviewCommentRepository>(),
            Mock.Of<IUserRepository>(),
            Mock.Of<IDocumentLifecycleService>(),
            new MutableCurrentUserAccessor());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RevisionId*Guid.Empty*");
    }

    [Theory]
    [InlineData("comments")]
    [InlineData("users")]
    [InlineData("lifecycle")]
    [InlineData("currentUser")]
    public void Constructor_NullDependency_Throws(string paramName)
    {
        IDocumentReviewCommentRepository? c = Mock.Of<IDocumentReviewCommentRepository>();
        IUserRepository? u = Mock.Of<IUserRepository>();
        IDocumentLifecycleService? l = Mock.Of<IDocumentLifecycleService>();
        ICurrentUserAccessor? cu = new MutableCurrentUserAccessor();
        switch (paramName)
        {
            case "comments": c = null; break;
            case "users": u = null; break;
            case "lifecycle": l = null; break;
            case "currentUser": cu = null; break;
        }

        Action act = () => new CommentPanelViewModel(RevisionId, c!, u!, l!, cu!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_CommentsStartEmpty_CommandsExist()
    {
        var sut = BuildSut();

        sut.Vm.Comments.Should().BeEmpty();
        sut.Vm.NewCommentText.Should().BeEmpty();
        sut.Vm.LoadCommand.Should().NotBeNull();
        sut.Vm.AddCommand.Should().NotBeNull();
        sut.Vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_QueriesRepoWithRevisionIdAsync()
    {
        var sut = BuildSut();

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Comments.Verify(c => c.GetByRevisionIdAsync(
            RevisionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_ProjectsToRowsInChronologicalForwardOrderAsync()
    {
        // Insert in reverse-chronological order; expect output in
        // chronological forward order (oldest first).
        var c10 = MakeComment(AliceId, "ten", Utc(10));
        var c11 = MakeComment(BobId, "eleven", Utc(11));
        var c12 = MakeComment(AliceId, "twelve", Utc(12));
        var alice = MakeUser(AliceId, "alice", "Alice Smith");
        var bob = MakeUser(BobId, "bob", "Bob Johnson");
        var sut = BuildSut(
            loadedComments: new[] { c12, c10, c11 },
            loadedUsers: new[] { alice, bob });

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.Comments.Should().HaveCount(3);
        sut.Vm.Comments[0].BodyText.Should().Be("ten");
        sut.Vm.Comments[1].BodyText.Should().Be("eleven");
        sut.Vm.Comments[2].BodyText.Should().Be("twelve");
    }

    [Fact]
    public async Task LoadAsync_ResolvesAuthorDisplayNamesAsync()
    {
        var c = MakeComment(AliceId, "hi", Utc(10));
        var alice = MakeUser(AliceId, "asmith", "Alice Smith");
        var sut = BuildSut(loadedComments: new[] { c }, loadedUsers: new[] { alice });

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.Comments.Should().ContainSingle();
        sut.Vm.Comments[0].AuthorDisplayName.Should().Be("Alice Smith");
        sut.Vm.Comments[0].AuthorUsername.Should().Be("asmith");
    }

    [Fact]
    public async Task LoadAsync_UnresolvableAuthor_FallsBackToUnknownUserAsync()
    {
        // Comment from a user whose row is gone (soft-deleted or
        // otherwise unresolvable). The row should still render.
        var c = MakeComment(GhostId, "ghost comment", Utc(10));
        var sut = BuildSut(loadedComments: new[] { c }, loadedUsers: Array.Empty<User>());

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.Comments.Should().ContainSingle();
        sut.Vm.Comments[0].AuthorDisplayName.Should().Be("(unknown user)");
        sut.Vm.Comments[0].AuthorUsername.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_RepoThrows_PopulatesErrorMessageAsync()
    {
        var sut = BuildSut(loadThrows: new InvalidOperationException("db down"));

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("db down");
        sut.Vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void AddCommand_CanExecute_FalseWhenTextEmpty()
    {
        var sut = BuildSut(hasReviewPermission: true);

        sut.Vm.NewCommentText = string.Empty;

        sut.Vm.AddCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddCommand_CanExecute_FalseWhenTextWhitespace()
    {
        var sut = BuildSut(hasReviewPermission: true);

        sut.Vm.NewCommentText = "   ";

        sut.Vm.AddCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddCommand_CanExecute_FalseWithoutReviewPermission()
    {
        var sut = BuildSut(hasReviewPermission: false);

        sut.Vm.NewCommentText = "valid text";

        sut.Vm.AddCommand.CanExecute(null).Should().BeFalse();
        sut.Vm.CanComment.Should().BeFalse();
    }

    [Fact]
    public void AddCommand_CanExecute_TrueWithPermissionAndNonWhitespaceText()
    {
        var sut = BuildSut(hasReviewPermission: true);

        sut.Vm.NewCommentText = "section 3 needs more detail";

        sut.Vm.AddCommand.CanExecute(null).Should().BeTrue();
        sut.Vm.CanComment.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_InvokesLifecycleWithRevisionIdAndBodyTextAsync()
    {
        var sut = BuildSut();
        sut.Vm.NewCommentText = "comment body";

        await sut.Vm.AddCommand.ExecuteAsync(null);

        sut.Lifecycle.Verify(l => l.AddCommentAsync(
            RevisionId, "comment body", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_OnSuccess_ClearsTextAndRefreshesAsync()
    {
        var sut = BuildSut();
        sut.Vm.NewCommentText = "first";

        await sut.Vm.AddCommand.ExecuteAsync(null);

        sut.Vm.NewCommentText.Should().BeEmpty();
        sut.Vm.ErrorMessage.Should().BeNull();
        // Reload was called once by the Add path. The constructor
        // doesn't call Load — only the panel control's Loaded
        // event handler does, and that's not exercised in unit
        // tests.
        sut.Comments.Verify(c => c.GetByRevisionIdAsync(
            RevisionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_LifecycleThrows_PopulatesErrorMessageAndSkipsReloadAsync()
    {
        var sut = BuildSut(addThrows: new InvalidOperationException("revision not in InReview"));
        sut.Vm.NewCommentText = "doomed";

        await sut.Vm.AddCommand.ExecuteAsync(null);

        sut.Vm.ErrorMessage.Should().NotBeNull();
        sut.Vm.ErrorMessage!.Should().Contain("revision not in InReview");
        // Text not cleared on failure so the user can edit and
        // retry.
        sut.Vm.NewCommentText.Should().Be("doomed");
        // No reload after a failed add.
        sut.Comments.Verify(c => c.GetByRevisionIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
