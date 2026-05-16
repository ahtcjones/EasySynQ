using System.IO;

using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Tests.TestHelpers;
using EasySynQ.UI.Documents.CreateDocument;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Documents.List;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.List;

/// <summary>
/// Unit tests for <see cref="DocumentListViewModel"/> (ADR 0008 C6a).
/// Pure VM behavior — repositories, prompter, lifecycle service, and
/// detail-factory delegate all mocked.
/// </summary>
public class DocumentListViewModelTests
{
    private static readonly DateTime Now =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<string> CreatePerm =
    [
        PermissionNames.DocumentCreate,
    ];

    /// <summary>Builds a SUT with optional repository pre-population.</summary>
    private static (DocumentListViewModel Vm,
                    Mock<IDocumentRepository> Docs,
                    Mock<IDocumentRevisionRepository> Revs,
                    Mock<IUserRepository> Users,
                    Mock<ICreateDocumentPrompter> Prompter,
                    Mock<IDocumentLifecycleService> Lifecycle,
                    MutableCurrentUserAccessor CurrentUser,
                    List<Document> DetailFactoryCalls)
        BuildSut(
            IReadOnlyList<Document>? documents = null,
            Dictionary<Guid, DocumentRevision>? latestRevisions = null,
            IReadOnlyList<User>? users = null,
            IReadOnlyCollection<string>? permissions = null)
    {
        var docs = new Mock<IDocumentRepository>();
        docs.Setup(d => d.GetNonRetiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents ?? []);

        var revs = new Mock<IDocumentRevisionRepository>();
        if (latestRevisions is not null)
        {
            foreach (var kvp in latestRevisions)
            {
                revs.Setup(r => r.GetLatestRevisionAsync(kvp.Key, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(kvp.Value);
            }
        }
        else
        {
            revs.Setup(r => r.GetLatestRevisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentRevision?)null);
        }

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users ?? []);

        var prompter = new Mock<ICreateDocumentPrompter>();
        var lifecycle = new Mock<IDocumentLifecycleService>();
        var clock = new FixedClock(Now);
        var currentUser = new MutableCurrentUserAccessor
        {
            UserId = Guid.NewGuid(),
            Username = "tester",
            Roles = ["TestRole"],
            Permissions = permissions ?? [],
        };

        var detailCalls = new List<Document>();
        DocumentDetailViewModel DetailFactory(Document doc)
        {
            detailCalls.Add(doc);
            // Returning null would break the contract; the VM's
            // DetailViewModel getter never sees this stub in the
            // tests that don't exercise selection.
            return null!;
        }

        var vm = new DocumentListViewModel(
            docs.Object,
            revs.Object,
            userRepo.Object,
            currentUser,
            prompter.Object,
            lifecycle.Object,
            clock,
            DetailFactory);

        return (vm, docs, revs, userRepo, prompter, lifecycle, currentUser, detailCalls);
    }

    private static Document NewDoc(string number = "SOP-001", string title = "Test")
        => new(Guid.NewGuid(), number, title);

    private static DocumentRevision NewDraftRev(Guid documentId, Guid authorId)
        => new(Guid.NewGuid(), documentId, "Rev A", authorId);

    private static User NewUser(Guid id, string username)
        => new(id, username, username, "hash", "salt",
               passwordIterationCount: 1000,
               mustChangePassword: false);

    // ─── LoadAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_EmptyRepo_ProducesEmptyItemsAsync()
    {
        var (vm, _, _, _, _, _, _, _) = BuildSut();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_PopulatesItemsWithRevisionAndAuthorAsync()
    {
        var authorId = Guid.NewGuid();
        var doc = NewDoc("SOP-Q-001", "Quality Plan");
        var rev = NewDraftRev(doc.Id, authorId);

        var (vm, _, _, _, _, _, _, _) = BuildSut(
            documents: [doc],
            latestRevisions: new() { [doc.Id] = rev },
            users: [NewUser(authorId, "aliceb")]);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        var item = vm.Items[0];
        item.DocumentId.Should().Be(doc.Id);
        item.Number.Should().Be("SOP-Q-001");
        item.Title.Should().Be("Quality Plan");
        item.CurrentRevisionLabel.Should().Be("Rev A");
        item.LifecycleDisplay.Should().Be("Draft");
        item.AuthorDisplay.Should().Be("aliceb");
    }

    [Fact]
    public async Task LoadAsync_UnknownAuthor_RendersUnknownPlaceholderAsync()
    {
        var doc = NewDoc();
        var rev = NewDraftRev(doc.Id, Guid.NewGuid());

        // Bulk-fetch returns no users — the AuthorUserId resolves to
        // the (unknown) placeholder rather than throwing.
        var (vm, _, _, _, _, _, _, _) = BuildSut(
            documents: [doc],
            latestRevisions: new() { [doc.Id] = rev },
            users: []);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Items.Single().AuthorDisplay.Should().Be("(unknown)");
    }

    [Fact]
    public async Task LoadAsync_BulkFetchUsesDistinctIdsAcrossDocumentsAsync()
    {
        var sharedAuthor = Guid.NewGuid();
        var docA = NewDoc("SOP-A");
        var docB = NewDoc("SOP-B");
        var revA = NewDraftRev(docA.Id, sharedAuthor);
        var revB = NewDraftRev(docB.Id, sharedAuthor);

        var (vm, _, _, users, _, _, _, _) = BuildSut(
            documents: [docA, docB],
            latestRevisions: new() { [docA.Id] = revA, [docB.Id] = revB },
            users: [NewUser(sharedAuthor, "carol")]);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(2);
        vm.Items.Should().OnlyContain(i => i.AuthorDisplay == "carol");
        users.Verify(
            u => u.GetByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Single() == sharedAuthor),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── CanCreate / CreateCommand gating ───────────────────────────

    [Fact]
    public void CanCreate_FalseWithoutPermission()
    {
        var (vm, _, _, _, _, _, _, _) = BuildSut(permissions: []);

        vm.CanCreate.Should().BeFalse();
        vm.CreateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanCreate_TrueWithPermission()
    {
        var (vm, _, _, _, _, _, _, _) = BuildSut(permissions: CreatePerm);

        vm.CanCreate.Should().BeTrue();
        vm.CreateCommand.CanExecute(null).Should().BeTrue();
    }

    // ─── CreateAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PrompterReturnsNull_DoesNotCallServiceAsync()
    {
        var (vm, _, _, _, prompter, lifecycle, _, _) = BuildSut(permissions: CreatePerm);
        prompter.Setup(p => p.PromptAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentResult?)null);

        await vm.CreateCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.CreateDocumentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NoPdfPicked_InvokesCreateServiceAndRefreshesAsync()
    {
        var doc = NewDoc("SOP-NEW", "New Doc");
        var (vm, docs, _, _, prompter, lifecycle, _, _) = BuildSut(permissions: CreatePerm);
        prompter.Setup(p => p.PromptAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateDocumentResult("SOP-NEW", "New Doc", null));
        lifecycle.Setup(l => l.CreateDocumentAsync(
                "SOP-NEW", "New Doc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        await vm.CreateCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.CreateDocumentAsync("SOP-NEW", "New Doc", It.IsAny<CancellationToken>()),
            Times.Once);
        lifecycle.Verify(
            l => l.AttachPdfToDraftAsync(
                It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        // LoadAsync called once after the Create.
        docs.Verify(
            d => d.GetNonRetiredAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithPdfPicked_InvokesAttachAfterCreateAsync()
    {
        // Create a real temp PDF file the VM can open.
        var temp = Path.GetTempFileName();
        File.WriteAllText(temp, "fake pdf");
        try
        {
            var doc = NewDoc("SOP-PDF", "With PDF");
            var rev = NewDraftRev(doc.Id, Guid.NewGuid());
            var (vm, _, revs, _, prompter, lifecycle, _, _) = BuildSut(permissions: CreatePerm);
            prompter.Setup(p => p.PromptAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateDocumentResult("SOP-PDF", "With PDF", temp));
            lifecycle.Setup(l => l.CreateDocumentAsync(
                    "SOP-PDF", "With PDF", It.IsAny<CancellationToken>()))
                .ReturnsAsync(doc);
            revs.Setup(r => r.GetLatestRevisionAsync(doc.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(rev);

            await vm.CreateCommand.ExecuteAsync(null);

            lifecycle.Verify(
                l => l.AttachPdfToDraftAsync(
                    rev.Id,
                    It.IsAny<Stream>(),
                    Path.GetFileName(temp),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // ─── IDirtyStateAware ───────────────────────────────────────────

    [Fact]
    public async Task IsDirtyStateAware_NoUnsavedChanges_AllowsDiscardAsync()
    {
        var (vm, _, _, _, _, _, _, _) = BuildSut();
        vm.HasUnsavedChanges.Should().BeFalse();
        (await vm.ConfirmDiscardAsync(CancellationToken.None)).Should().BeTrue();
    }

    // ─── DetailViewModel.Deleted subscription (C6a smoke Finding 3) ──

    [Fact]
    public async Task DetailVm_Deleted_ClearsSelectionAndReloadsAsync()
    {
        // Setup: a single document in the list, authored by the
        // current user, with a Draft revision (so the detail VM's
        // CanHardDelete will evaluate true). The list VM uses a
        // custom factory that constructs a REAL DocumentDetailViewModel
        // with mocked deps — necessary because Deleted is a normal
        // C# event that can only be raised from inside the declaring
        // type. Driving the real HardDeleteDraftCommand exercises the
        // production code path that fires Deleted.
        var authorId = Guid.NewGuid();
        var doc = NewDoc("SOP-DEL-001", "Will be deleted");
        var rev = NewDraftRev(doc.Id, authorId);

        var docs = new Mock<IDocumentRepository>();
        // First load: returns the document. After the deleted-event
        // re-runs LoadAsync, returns empty (the real service would
        // have removed the row).
        docs.SetupSequence(d => d.GetNonRetiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([doc])
            .ReturnsAsync([]);
        docs.Setup(d => d.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var revs = new Mock<IDocumentRevisionRepository>();
        revs.Setup(r => r.GetLatestRevisionAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rev);

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewUser(authorId, "author")]);

        var prompter = new Mock<ICreateDocumentPrompter>();
        var lifecycle = new Mock<IDocumentLifecycleService>();
        // HardDeleteDraftAsync succeeds — no exception, no return value.
        lifecycle.Setup(l => l.HardDeleteDraftAsync(
                doc.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clock = new FixedClock(Now);
        var currentUser = new MutableCurrentUserAccessor
        {
            UserId = authorId,
            Username = "author",
            Roles = ["TestRole"],
            Permissions =
            [
                PermissionNames.DocumentEditDraft,
                PermissionNames.DocumentHardDelete,
            ],
        };

        // Real factory: constructs a real DocumentDetailViewModel so
        // its real HardDeleteDraftCommand can fire the real Deleted
        // event. The detail VM's deps are independent mocks; the
        // detail VM's repo mocks need their own setup so its LoadAsync
        // populates CurrentRevision (which CanHardDelete depends on).
        var detailDocs = new Mock<IDocumentRepository>();
        detailDocs.Setup(d => d.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        var detailRevs = new Mock<IDocumentRevisionRepository>();
        detailRevs.Setup(r => r.GetLatestRevisionAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rev);

        DocumentDetailViewModel? constructedDetail = null;
        DocumentDetailViewModel DetailFactory(Document d)
        {
            constructedDetail = new DocumentDetailViewModel(
                d,
                detailDocs.Object,
                detailRevs.Object,
                Mock.Of<EasySynQ.Services.Abstractions.IDocumentReviewAssignmentRepository>(),
                Mock.Of<EasySynQ.Services.Abstractions.IDocumentReviewCommentRepository>(),
                Mock.Of<EasySynQ.Services.Abstractions.IUserRepository>(),
                Mock.Of<EasySynQ.Services.Vault.IVaultService>(),
                Mock.Of<EasySynQ.Services.Vault.IVaultPathProvider>(
                    p => p.VaultRoot == @"C:\test-vault"),
                clock,
                currentUser,
                lifecycle.Object,
                Mock.Of<EasySynQ.UI.Documents.IFilePicker>(),
                Mock.Of<EasySynQ.UI.Documents.EditMetadata.IEditMetadataPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.SubmitForReview.ISubmitForReviewPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.ReviewAndSign.IReviewAndSignPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.ReturnToDraft.IReturnToDraftPrompter>());
            return constructedDetail;
        }

        var vm = new DocumentListViewModel(
            docs.Object, revs.Object, userRepo.Object,
            currentUser, prompter.Object, lifecycle.Object,
            clock, DetailFactory);

        await vm.LoadCommand.ExecuteAsync(null);
        vm.Items.Should().ContainSingle();

        // Select the row — getter constructs the detail VM and
        // subscribes to its Deleted event.
        vm.SelectedItem = vm.Items[0];
        _ = vm.DetailViewModel;
        constructedDetail.Should().NotBeNull("the factory should have been invoked");

        // Populate the detail VM's CurrentRevision so CanHardDelete
        // evaluates true.
        await constructedDetail!.LoadCommand.ExecuteAsync(null);
        constructedDetail.CanHardDelete.Should().BeTrue();

        // Fire the real HardDeleteDraftCommand — this is the
        // production code path; it invokes the (mocked) service then
        // raises Deleted. The list VM's handler should observe it.
        await constructedDetail.HardDeleteDraftCommand.ExecuteAsync(null);

        // The handler ran (async-void). Selection cleared and the
        // list re-loaded.
        vm.SelectedItem.Should().BeNull(
            "Deleted event should clear the now-stale selection");
        vm.Items.Should().BeEmpty(
            "list reload after delete should observe the document is gone");
        docs.Verify(
            d => d.GetNonRetiredAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "LoadAsync should run once for initial load and once for the post-delete refresh");

        // Sanity: the detail VM's command actually invoked the service.
        lifecycle.Verify(
            l => l.HardDeleteDraftAsync(doc.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DetailVm_SelectionChange_UnsubscribesPriorDeletedHandlerAsync()
    {
        // Belt-and-braces unsub test. Build two documents; select the
        // first (constructs detail VM A, subscribes), then select the
        // second (should unsub from A, construct B, subscribe to B).
        // Then raise Deleted on the ORIGINAL detail VM A — the list
        // should NOT respond because A is no longer the cached VM.
        var docA = NewDoc("SOP-A");
        var docB = NewDoc("SOP-B");
        var authorId = Guid.NewGuid();
        var revA = NewDraftRev(docA.Id, authorId);
        var revB = NewDraftRev(docB.Id, authorId);

        var docs = new Mock<IDocumentRepository>();
        docs.Setup(d => d.GetNonRetiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([docA, docB]);
        docs.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                id == docA.Id ? docA : (id == docB.Id ? docB : null));

        var revs = new Mock<IDocumentRevisionRepository>();
        revs.Setup(r => r.GetLatestRevisionAsync(docA.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revA);
        revs.Setup(r => r.GetLatestRevisionAsync(docB.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revB);

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewUser(authorId, "author")]);

        var lifecycle = new Mock<IDocumentLifecycleService>();
        lifecycle.Setup(l => l.HardDeleteDraftAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clock = new FixedClock(Now);
        var currentUser = new MutableCurrentUserAccessor
        {
            UserId = authorId,
            Username = "author",
            Roles = ["TestRole"],
            Permissions =
            [
                PermissionNames.DocumentEditDraft,
                PermissionNames.DocumentHardDelete,
            ],
        };

        var constructed = new List<DocumentDetailViewModel>();
        DocumentDetailViewModel DetailFactory(Document d)
        {
            var dvm = new DocumentDetailViewModel(
                d,
                docs.Object, revs.Object,
                Mock.Of<EasySynQ.Services.Abstractions.IDocumentReviewAssignmentRepository>(),
                Mock.Of<EasySynQ.Services.Abstractions.IDocumentReviewCommentRepository>(),
                Mock.Of<EasySynQ.Services.Abstractions.IUserRepository>(),
                Mock.Of<EasySynQ.Services.Vault.IVaultService>(),
                Mock.Of<EasySynQ.Services.Vault.IVaultPathProvider>(
                    p => p.VaultRoot == @"C:\test-vault"),
                clock, currentUser, lifecycle.Object,
                Mock.Of<EasySynQ.UI.Documents.IFilePicker>(),
                Mock.Of<EasySynQ.UI.Documents.EditMetadata.IEditMetadataPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.SubmitForReview.ISubmitForReviewPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.ReviewAndSign.IReviewAndSignPrompter>(),
                Mock.Of<EasySynQ.UI.Documents.ReturnToDraft.IReturnToDraftPrompter>());
            constructed.Add(dvm);
            return dvm;
        }

        var vm = new DocumentListViewModel(
            docs.Object, revs.Object, userRepo.Object,
            currentUser, new Mock<ICreateDocumentPrompter>().Object,
            lifecycle.Object, clock, DetailFactory);

        await vm.LoadCommand.ExecuteAsync(null);

        // Select A → constructs A, subscribes.
        vm.SelectedItem = vm.Items.Single(i => i.DocumentId == docA.Id);
        _ = vm.DetailViewModel;
        // Select B → unsubscribes from A, constructs B, subscribes to B.
        vm.SelectedItem = vm.Items.Single(i => i.DocumentId == docB.Id);
        _ = vm.DetailViewModel;

        constructed.Should().HaveCount(2);
        var detailA = constructed[0];
        var detailB = constructed[1];

        await detailA.LoadCommand.ExecuteAsync(null);
        await detailB.LoadCommand.ExecuteAsync(null);

        // Fire HardDelete on A (which is no longer the cached VM).
        // The list should NOT respond — B remains selected.
        await detailA.HardDeleteDraftCommand.ExecuteAsync(null);

        vm.SelectedItem.Should().NotBeNull(
            "selection on B must not be cleared by a stale Deleted from A");
        vm.SelectedItem!.DocumentId.Should().Be(docB.Id);
    }
}
