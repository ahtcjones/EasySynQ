using System.IO;

using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Vault;
using EasySynQ.Tests.TestHelpers;
using EasySynQ.UI.Documents;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Documents.EditMetadata;
using EasySynQ.UI.Documents.ReturnToDraft;
using EasySynQ.UI.Documents.ReviewAndSign;
using EasySynQ.UI.Documents.SubmitForReview;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.Detail;

/// <summary>
/// Unit tests for <see cref="DocumentDetailViewModel"/> (ADR 0008
/// C6a). Pure VM behavior — no WebView2, no DI container.
/// Affordance-gating matrix from the C6a plan §D is exercised across
/// (lifecycle × permissions × is-author) cells.
/// </summary>
public class DocumentDetailViewModelTests
{
    private static readonly DateTime Now =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> AllAuthorPerms =
    [
        PermissionNames.DocumentEditDraft,
        PermissionNames.DocumentHardDelete,
    ];

    private static Document NewDocument()
        => new(DocumentId, "SOP-001", "Test Document");

    private static DocumentRevision NewDraftRevision(Guid? authorId = null)
        => new(Guid.NewGuid(), DocumentId, "Rev A", authorId ?? AuthorId);

    private static DocumentRevision NewInReviewRevision()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), Now);
        return rev;
    }

    private static DocumentRevision NewApprovedRevision(DateTime? effectiveFromUtc)
    {
        var rev = NewDraftRevision();
        if (effectiveFromUtc is not null)
        {
            rev.SetEffectiveFromUtc(effectiveFromUtc);
        }
        rev.Submit(Guid.NewGuid(), Now);
        rev.Approve(Now);
        return rev;
    }

    private static DocumentRevision NewSupersededRevision()
    {
        var rev = NewApprovedRevision(null);
        rev.Supersede();
        return rev;
    }

    private static DocumentRevision NewArchivedRevision()
    {
        var rev = NewApprovedRevision(null);
        rev.Archive();
        return rev;
    }

    /// <summary>Builds a SUT with stubbed deps. Pre-loads the supplied
    /// <paramref name="latest"/> into the revision repository so the
    /// VM's LoadAsync sees it.</summary>
    private static SutBundle BuildSut(
        DocumentRevision? latest = null,
        IReadOnlyCollection<string>? permissions = null,
        Guid? currentUserId = null,
        string? vaultPath = null,
        Document? document = null,
        IReadOnlyList<DocumentReviewAssignment>? assignments = null,
        IReadOnlyList<User>? users = null)
    {
        var doc = document ?? NewDocument();
        var docs = new Mock<IDocumentRepository>();
        docs.Setup(d => d.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var revs = new Mock<IDocumentRevisionRepository>();
        revs.Setup(r => r.GetLatestRevisionAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        var assignmentsRepo = new Mock<IDocumentReviewAssignmentRepository>();
        assignmentsRepo.Setup(a => a.GetByRevisionIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments ?? Array.Empty<DocumentReviewAssignment>());

        var commentsRepo = new Mock<IDocumentReviewCommentRepository>();
        commentsRepo.Setup(c => c.GetByRevisionIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DocumentReviewComment>());

        var usersRepo = new Mock<IUserRepository>();
        usersRepo.Setup(u => u.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users ?? Array.Empty<User>());

        var vault = new Mock<IVaultService>();
        if (vaultPath is not null)
        {
            vault.Setup(v => v.GetVaultFilePathAsync(
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(vaultPath);
        }

        var clock = new FixedClock(Now);
        var currentUser = new MutableCurrentUserAccessor
        {
            UserId = currentUserId ?? AuthorId,
            Username = "tester",
            Roles = ["TestRole"],
            Permissions = permissions ?? AllAuthorPerms,
        };

        var lifecycle = new Mock<IDocumentLifecycleService>();
        var picker = new Mock<IFilePicker>();
        var prompter = new Mock<IEditMetadataPrompter>();
        var submitPrompter = new Mock<ISubmitForReviewPrompter>();
        var signPrompter = new Mock<IReviewAndSignPrompter>();
        var returnPrompter = new Mock<IReturnToDraftPrompter>();
        var lockInspectorPrompter = new Mock<EasySynQ.UI.LockInspector.ILockInspectorPrompter>();
        var pathProvider = new Mock<IVaultPathProvider>();
        pathProvider.SetupGet(p => p.VaultRoot).Returns(TestVaultRoot);

        var vm = new DocumentDetailViewModel(
            doc,
            docs.Object,
            revs.Object,
            assignmentsRepo.Object,
            commentsRepo.Object,
            usersRepo.Object,
            vault.Object,
            pathProvider.Object,
            clock,
            currentUser,
            lifecycle.Object,
            picker.Object,
            prompter.Object,
            submitPrompter.Object,
            signPrompter.Object,
            returnPrompter.Object,
            lockInspectorPrompter.Object);

        return new SutBundle(
            vm, docs, revs, assignmentsRepo, commentsRepo, usersRepo,
            vault, lifecycle, picker, prompter,
            submitPrompter, signPrompter, returnPrompter, currentUser);
    }

    /// <summary>Bundle for the BuildSut helper — easier to add fields
    /// without disturbing every callsite (the tuple shape was already
    /// at its readable limit before C6b's three new prompters). The
    /// 7-position <see cref="Deconstruct"/> below preserves the
    /// pre-C6b deconstruction pattern existing tests use; new tests
    /// access the C6b-only fields by name.</summary>
    private sealed record SutBundle(
        DocumentDetailViewModel Vm,
        Mock<IDocumentRepository> Docs,
        Mock<IDocumentRevisionRepository> Revs,
        Mock<IDocumentReviewAssignmentRepository> AssignmentsRepo,
        Mock<IDocumentReviewCommentRepository> CommentsRepo,
        Mock<IUserRepository> UsersRepo,
        Mock<IVaultService> Vault,
        Mock<IDocumentLifecycleService> Lifecycle,
        Mock<IFilePicker> Picker,
        Mock<IEditMetadataPrompter> EditPrompter,
        Mock<ISubmitForReviewPrompter> SubmitPrompter,
        Mock<IReviewAndSignPrompter> SignPrompter,
        Mock<IReturnToDraftPrompter> ReturnPrompter,
        MutableCurrentUserAccessor CurrentUser)
    {
        /// <summary>Back-compat 7-position deconstruction matching
        /// the pre-C6b tuple shape so existing tests
        /// (<c>var (vm, docs, revs, vault, lifecycle, picker,
        /// editPrompter) = BuildSut(...)</c>) keep working without
        /// each site being mechanically rewritten.</summary>
        public void Deconstruct(
            out DocumentDetailViewModel vm,
            out Mock<IDocumentRepository> docs,
            out Mock<IDocumentRevisionRepository> revs,
            out Mock<IVaultService> vault,
            out Mock<IDocumentLifecycleService> lifecycle,
            out Mock<IFilePicker> picker,
            out Mock<IEditMetadataPrompter> editPrompter)
        {
            vm = Vm;
            docs = Docs;
            revs = Revs;
            vault = Vault;
            lifecycle = Lifecycle;
            picker = Picker;
            editPrompter = EditPrompter;
        }
    }

    /// <summary>Test vault root — must be a real-shape absolute path
    /// so PdfViewerControl.BuildContentUrl produces a sensible URL.
    /// Doesn't have to exist; only path-relative math runs on it.</summary>
    private const string TestVaultRoot = @"C:\test-vault";

    // ─── Affordance gating matrix ───────────────────────────────────

    [Theory]
    // Lifecycle           , IsAuthor, EditDraft, HardDelete, EditMeta, ReplacePdf, HardDel
    [InlineData("Draft",     true,    true,      true,       true,     true,       true)]
    [InlineData("Draft",     false,   true,      true,       true,     true,       false)]
    [InlineData("Draft",     true,    true,      false,      true,     true,       false)]
    [InlineData("Draft",     true,    false,     false,      false,    false,      false)]
    [InlineData("InReview",  true,    true,      true,       false,    false,      false)]
    [InlineData("Approved",  true,    true,      true,       false,    false,      false)]
    [InlineData("Superseded",true,    true,      true,       false,    false,      false)]
    [InlineData("Archived",  true,    true,      true,       false,    false,      false)]
    public async Task AffordanceGating_FollowsLifecyclePermissionsAuthorMatrixAsync(
        string lifecycle,
        bool isAuthor,
        bool editDraft,
        bool hardDelete,
        bool expectEditMeta,
        bool expectReplacePdf,
        bool expectHardDelete)
    {
        var perms = new List<string>();
        if (editDraft) perms.Add(PermissionNames.DocumentEditDraft);
        if (hardDelete) perms.Add(PermissionNames.DocumentHardDelete);

        DocumentRevision rev = lifecycle switch
        {
            "Draft" => NewDraftRevision(),
            "InReview" => NewInReviewRevision(),
            "Approved" => NewApprovedRevision(null),
            "Superseded" => NewSupersededRevision(),
            "Archived" => NewArchivedRevision(),
            _ => throw new InvalidOperationException($"Unknown lifecycle: {lifecycle}"),
        };

        var caller = isAuthor ? AuthorId : OtherUserId;
        var (vm, _, _, _, _, _, _) = BuildSut(
            latest: rev, permissions: perms, currentUserId: caller);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.CanEditMetadata.Should().Be(expectEditMeta);
        vm.CanReplacePdf.Should().Be(expectReplacePdf);
        vm.CanHardDelete.Should().Be(expectHardDelete);
    }

    // ─── LoadAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NoVaultBlob_LeavesVaultUrlNullAsync()
    {
        var rev = NewDraftRevision();
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.CurrentRevision.Should().BeSameAs(rev);
        vm.VaultDocumentUrl.Should().BeNull();
        vault.Verify(
            v => v.GetVaultFilePathAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_WithVaultBlob_ResolvesVirtualHostUrlFromVaultAsync()
    {
        // The VM now resolves the on-disk path via the vault service
        // then translates to a virtual-host URL via
        // PdfViewerControl.BuildContentUrl. The control's content-host
        // mapping (registered at WebView2 init time) resolves the URL
        // back to the file. Test the URL shape rather than the raw
        // path — that's what the viewer binds to.
        var rev = NewDraftRevision();
        rev.AttachVaultBlob(Guid.NewGuid());
        var vaultFilePath = $@"{TestVaultRoot}\ab\abc.pdf";
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev, vaultPath: vaultFilePath);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.VaultDocumentUrl.Should().Be("https://easysynq-pdfcontent.local/ab/abc.pdf");
        vault.Verify(
            v => v.GetVaultFilePathAsync(rev.VaultBlobId!.Value, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ContentRoot_PassesThroughVaultPathProvider()
    {
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());
        vm.ContentRoot.Should().Be(TestVaultRoot);
    }

    [Fact]
    public async Task LifecycleDisplay_ApprovedFutureEffective_RendersGapWindowAsync()
    {
        var rev = NewApprovedRevision(Now.AddDays(30));
        var (vm, _, _, _, _, _, _) = BuildSut(latest: rev);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.LifecycleDisplay.Should().StartWith("Approved (effective ");
    }

    [Fact]
    public async Task LifecycleDisplay_ApprovedPastEffective_RendersActiveAsync()
    {
        var rev = NewApprovedRevision(Now.AddDays(-10));
        var (vm, _, _, _, _, _, _) = BuildSut(latest: rev);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.LifecycleDisplay.Should().Be("Active");
    }

    // ─── EditMetadataCommand ────────────────────────────────────────

    [Fact]
    public async Task EditMetadata_PrompterReturnsResult_InvokesServiceAndReloadsAsync()
    {
        var rev = NewDraftRevision();
        var (vm, docs, revs, _, lifecycle, _, prompter) = BuildSut(latest: rev);
        prompter.Setup(p => p.PromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EditMetadataResult("SOP-NEW", "New Title"));

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.EditMetadataCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.EditDraftMetadataAsync(
                DocumentId, "SOP-NEW", "New Title", It.IsAny<CancellationToken>()),
            Times.Once);
        // LoadAsync called twice — initial + post-edit reload.
        docs.Verify(
            d => d.GetByIdAsync(DocumentId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        revs.Verify(
            r => r.GetLatestRevisionAsync(DocumentId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task EditMetadata_PrompterReturnsNull_DoesNotCallServiceAsync()
    {
        var rev = NewDraftRevision();
        var (vm, _, _, _, lifecycle, _, prompter) = BuildSut(latest: rev);
        prompter.Setup(p => p.PromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditMetadataResult?)null);

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.EditMetadataCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.EditDraftMetadataAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── ReplacePdfCommand ──────────────────────────────────────────

    [Fact]
    public async Task ReplacePdf_PickerReturnsNull_DoesNotCallServiceAsync()
    {
        var rev = NewDraftRevision();
        var (vm, _, _, _, lifecycle, picker, _) = BuildSut(latest: rev);
        picker.Setup(p => p.PickFile(It.IsAny<string>(), It.IsAny<string>()))
              .Returns((string?)null);

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ReplacePdfCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.AttachPdfToDraftAsync(
                It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReplacePdf_PickerReturnsPath_OpensFileAndInvokesServiceAsync()
    {
        // Create a real temp file so File.OpenRead succeeds — the VM
        // currently opens the file directly. (A future refactor could
        // inject a stream-opener abstraction; for now the test pays
        // the small temp-file cost.)
        var temp = Path.GetTempFileName();
        File.WriteAllText(temp, "fake pdf content");
        try
        {
            var rev = NewDraftRevision();
            var (vm, _, _, _, lifecycle, picker, _) = BuildSut(latest: rev);
            picker.Setup(p => p.PickFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(temp);

            await vm.LoadCommand.ExecuteAsync(null);
            await vm.ReplacePdfCommand.ExecuteAsync(null);

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

    // ─── HardDeleteDraftCommand ─────────────────────────────────────

    [Fact]
    public async Task HardDelete_InvokesService_RaisesDeletedEventAsync()
    {
        var rev = NewDraftRevision();
        var (vm, _, _, _, lifecycle, _, _) = BuildSut(latest: rev);

        var deletedRaised = 0;
        vm.Deleted += (_, _) => deletedRaised++;

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.HardDeleteDraftCommand.ExecuteAsync(null);

        lifecycle.Verify(
            l => l.HardDeleteDraftAsync(DocumentId, It.IsAny<CancellationToken>()),
            Times.Once);
        deletedRaised.Should().Be(1);
    }

    // ─── IDirtyStateAware ───────────────────────────────────────────

    [Fact]
    public async Task IsDirtyStateAware_NoUnsavedChanges_DiscardAllowedAsync()
    {
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());

        vm.HasUnsavedChanges.Should().BeFalse();
        (await vm.ConfirmDiscardAsync(CancellationToken.None)).Should().BeTrue();
    }

    // ─── Viewer-load-error banner state (smoke walk #2 Finding 3b) ──

    [Fact]
    public void HasViewerLoadError_StartsFalse()
    {
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());

        vm.HasViewerLoadError.Should().BeFalse();
        vm.ViewerErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void OnViewerNavigationFailed_SetsBannerWithReasonAndPath()
    {
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());

        vm.OnViewerNavigationFailed(
            reason: "NavigationCompletedWithError",
            attemptedPath: "https://easysynq-pdfcontent.local/2a/abc.pdf");

        vm.HasViewerLoadError.Should().BeTrue();
        vm.ViewerErrorMessage.Should().Contain("NavigationCompletedWithError");
        vm.ViewerErrorMessage.Should().Contain("https://easysynq-pdfcontent.local/2a/abc.pdf");
    }

    [Fact]
    public void OnViewerNavigationFailed_NullPath_OmitsAttemptedFromMessage()
    {
        // The WebView2-init-failure case has no attempted-path. The
        // banner still surfaces the reason but doesn't print an
        // empty / "Attempted:" suffix.
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());

        vm.OnViewerNavigationFailed(reason: "WebView2InitFailed", attemptedPath: null);

        vm.HasViewerLoadError.Should().BeTrue();
        vm.ViewerErrorMessage.Should().Contain("WebView2InitFailed");
        vm.ViewerErrorMessage.Should().NotContain("Attempted:");
    }

    [Fact]
    public async Task LoadAsync_ClearsPriorViewerErrorAsync()
    {
        // Set up a state where a prior load surfaced a viewer error,
        // then re-run LoadAsync. The banner should clear.
        var (vm, _, _, _, _, _, _) = BuildSut(latest: NewDraftRevision());

        vm.OnViewerNavigationFailed("NavigationCompletedWithError", "https://x/y.pdf");
        vm.HasViewerLoadError.Should().BeTrue();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasViewerLoadError.Should().BeFalse();
        vm.ViewerErrorMessage.Should().BeEmpty();
    }

    // ─── Layer 0 (VM-side) banner coverage — walk #5 finding ─────────

    [Fact]
    public async Task LoadAsync_VaultFileMissing_SurfacesBannerErrorAsync()
    {
        // Pre-C6a-amendment, IVaultService.GetVaultFilePathAsync
        // throwing FileNotFoundException would escape through the
        // async-void Loaded handler to the WPF dispatcher's
        // last-resort handler — wrong UX for what is a per-document
        // load failure. The Layer 0 fix routes the throw through the
        // same banner mechanism Layers 1+2 use.
        var rev = NewDraftRevision();
        rev.AttachVaultBlob(Guid.NewGuid());
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev);
        vault.Setup(v => v.GetVaultFilePathAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException(
                "VaultBlob row exists but the on-disk file is missing.",
                @"C:\test-vault\ab\missing.pdf"));

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasViewerLoadError.Should().BeTrue();
        vm.ViewerErrorMessage.Should().Contain("VaultFileUnavailable");
        vm.ViewerErrorMessage.Should().Contain("FileNotFoundException");
        // VaultDocumentUrl must be cleared so the viewer doesn't try
        // to render against a now-stale URL.
        vm.VaultDocumentUrl.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_VaultHashMismatch_SurfacesBannerErrorAsync()
    {
        // Tamper-detection path: the on-disk file exists but its
        // hash doesn't match the stored Sha256Hash. VaultService
        // throws InvalidDataException. Same banner route.
        var rev = NewDraftRevision();
        rev.AttachVaultBlob(Guid.NewGuid());
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev);
        vault.Setup(v => v.GetVaultFilePathAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException(
                "On-disk SHA-256 does not match stored hash."));

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasViewerLoadError.Should().BeTrue();
        vm.ViewerErrorMessage.Should().Contain("InvalidDataException");
        vm.VaultDocumentUrl.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_VaultBlobRowMissing_SurfacesBannerErrorAsync()
    {
        // The blob row itself is missing or soft-deleted (e.g., race
        // with a future PhysicalDelete). VaultService throws
        // KeyNotFoundException. Same banner route.
        var rev = NewDraftRevision();
        rev.AttachVaultBlob(Guid.NewGuid());
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev);
        vault.Setup(v => v.GetVaultFilePathAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException(
                "No VaultBlob row found for the supplied id."));

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasViewerLoadError.Should().BeTrue();
        vm.ViewerErrorMessage.Should().Contain("KeyNotFoundException");
        vm.VaultDocumentUrl.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_UnexpectedException_StillEscapesAsync()
    {
        // Catch is narrow to vault-content failure modes. A
        // programming error or an unrelated infrastructure exception
        // must still bubble to the dispatcher so it doesn't masquerade
        // as a vault-load failure. Pinning the catch's narrowness so
        // a future "catch (Exception ex)" widening surfaces here.
        var rev = NewDraftRevision();
        rev.AttachVaultBlob(Guid.NewGuid());
        var (vm, _, _, vault, _, _, _) = BuildSut(latest: rev);
        vault.Setup(v => v.GetVaultFilePathAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("programming error"));

        Func<Task> act = async () => await vm.LoadCommand.ExecuteAsync(null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        vm.HasViewerLoadError.Should().BeFalse(
            "out-of-bucket exceptions must not surface as banner errors");
    }

    // ─── C6b stop 7: SubmitForReview affordance gating ──────────────

    private static readonly IReadOnlyList<string> AllSubmitterPerms =
    [
        PermissionNames.DocumentSubmitForReview,
        PermissionNames.DocumentAssignReviewers,
    ];

    [Fact]
    public async Task SubmitForReview_DraftWithBothPermissions_CanExecuteAsync()
    {
        var sut = BuildSut(
            latest: NewDraftRevision(),
            permissions: AllSubmitterPerms);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanSubmitForReview.Should().BeTrue();
        sut.Vm.SubmitForReviewCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitForReview_DraftMissingSubmitPermission_CannotExecuteAsync()
    {
        var sut = BuildSut(
            latest: NewDraftRevision(),
            permissions: [PermissionNames.DocumentAssignReviewers]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanSubmitForReview.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitForReview_DraftMissingAssignReviewersPermission_CannotExecuteAsync()
    {
        var sut = BuildSut(
            latest: NewDraftRevision(),
            permissions: [PermissionNames.DocumentSubmitForReview]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanSubmitForReview.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitForReview_NonDraftLifecycle_CannotExecuteAsync()
    {
        var sut = BuildSut(
            latest: NewInReviewRevision(),
            permissions: AllSubmitterPerms);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanSubmitForReview.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitForReview_HappyPath_InvokesPrompterAndReloadsAsync()
    {
        var rev = NewDraftRevision();
        var sut = BuildSut(latest: rev, permissions: AllSubmitterPerms);
        await sut.Vm.LoadCommand.ExecuteAsync(null);
        sut.SubmitPrompter.Setup(p => p.PromptAsync(
                rev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.Vm.SubmitForReviewCommand.ExecuteAsync(null);

        sut.SubmitPrompter.Verify(p => p.PromptAsync(
            rev.Id, It.IsAny<CancellationToken>()), Times.Once);
        // Reload after success — Document fetched once at initial
        // Load, once after the submit. Document.GetByIdAsync is the
        // most distinctive call to count.
        sut.Docs.Verify(d => d.GetByIdAsync(
            rev.DocumentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SubmitForReview_PrompterCancels_DoesNotReloadAsync()
    {
        var rev = NewDraftRevision();
        var sut = BuildSut(latest: rev, permissions: AllSubmitterPerms);
        await sut.Vm.LoadCommand.ExecuteAsync(null);
        sut.SubmitPrompter.Setup(p => p.PromptAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await sut.Vm.SubmitForReviewCommand.ExecuteAsync(null);

        // Only the initial Load fetched the document — no second fetch
        // because the prompter cancelled.
        sut.Docs.Verify(d => d.GetByIdAsync(
            rev.DocumentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── C6b stop 7: ReviewAndSign affordance gating ────────────────

    private static User MakeUser(Guid id, string username, string display)
        => new(id, username, display, "h", "s", 1000, false);

    private static DocumentReviewAssignment MakeAssignment(
        Guid revisionId, Guid reviewerId, DocumentReviewAssignmentStatus status)
    {
        var a = new DocumentReviewAssignment(
            Guid.NewGuid(), revisionId, reviewerId, Now, Guid.NewGuid());
        if (status == DocumentReviewAssignmentStatus.Signed)
        {
            a.RecordSignature(Guid.NewGuid(), Now);
        }
        else if (status == DocumentReviewAssignmentStatus.Discarded)
        {
            a.Discard();
        }
        return a;
    }

    [Fact]
    public async Task ReviewAndSign_InReviewAsPendingNamedReviewer_CanExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var reviewer = MakeUser(OtherUserId, "reviewer", "Reviewer One");
        var assignment = MakeAssignment(
            rev.Id, OtherUserId, DocumentReviewAssignmentStatus.Pending);
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReview],
            currentUserId: OtherUserId,
            assignments: [assignment],
            users: [reviewer]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReviewAndSign.Should().BeTrue();
        sut.Vm.ReviewAndSignCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ReviewAndSign_InReviewButNotInAssignmentList_CannotExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var someoneElse = Guid.NewGuid();
        var assignment = MakeAssignment(
            rev.Id, someoneElse, DocumentReviewAssignmentStatus.Pending);
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReview],
            currentUserId: OtherUserId,
            assignments: [assignment],
            users: [MakeUser(someoneElse, "other", "Other")]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReviewAndSign.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewAndSign_AssignmentAlreadySigned_CannotExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var reviewer = MakeUser(OtherUserId, "reviewer", "Reviewer");
        var assignment = MakeAssignment(
            rev.Id, OtherUserId, DocumentReviewAssignmentStatus.Signed);
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReview],
            currentUserId: OtherUserId,
            assignments: [assignment],
            users: [reviewer]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReviewAndSign.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewAndSign_MissingPermission_CannotExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var reviewer = MakeUser(OtherUserId, "reviewer", "Reviewer");
        var assignment = MakeAssignment(
            rev.Id, OtherUserId, DocumentReviewAssignmentStatus.Pending);
        var sut = BuildSut(
            latest: rev,
            permissions: Array.Empty<string>(),
            currentUserId: OtherUserId,
            assignments: [assignment],
            users: [reviewer]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReviewAndSign.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewAndSign_HappyPath_InvokesPrompterAndReloadsAsync()
    {
        var rev = NewInReviewRevision();
        var reviewer = MakeUser(OtherUserId, "reviewer", "Reviewer");
        var assignment = MakeAssignment(
            rev.Id, OtherUserId, DocumentReviewAssignmentStatus.Pending);
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReview],
            currentUserId: OtherUserId,
            assignments: [assignment],
            users: [reviewer]);
        await sut.Vm.LoadCommand.ExecuteAsync(null);
        sut.SignPrompter.Setup(p => p.PromptAsync(
                rev.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.Vm.ReviewAndSignCommand.ExecuteAsync(null);

        sut.SignPrompter.Verify(p => p.PromptAsync(
            rev.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── C6b stop 7: ReturnToDraft affordance gating ────────────────

    [Fact]
    public async Task ReturnToDraft_InReviewWithPermission_CanExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReturnForEdits]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReturnToDraft.Should().BeTrue();
        sut.Vm.ReturnToDraftCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ReturnToDraft_InReviewWithoutPermission_CannotExecuteAsync()
    {
        var rev = NewInReviewRevision();
        var sut = BuildSut(latest: rev, permissions: Array.Empty<string>());

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReturnToDraft.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnToDraft_DraftLifecycle_CannotExecuteAsync()
    {
        var sut = BuildSut(
            latest: NewDraftRevision(),
            permissions: [PermissionNames.DocumentReturnForEdits]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanReturnToDraft.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnToDraft_HappyPath_InvokesPrompterAndReloadsAsync()
    {
        var rev = NewInReviewRevision();
        var sut = BuildSut(
            latest: rev,
            permissions: [PermissionNames.DocumentReturnForEdits]);
        await sut.Vm.LoadCommand.ExecuteAsync(null);
        sut.ReturnPrompter.Setup(p => p.PromptAsync(
                rev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.Vm.ReturnToDraftCommand.ExecuteAsync(null);

        sut.ReturnPrompter.Verify(p => p.PromptAsync(
            rev.Id, It.IsAny<CancellationToken>()), Times.Once);
        sut.Docs.Verify(d => d.GetByIdAsync(
            rev.DocumentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ─── C6b stop 7: Panel rendering by state ───────────────────────

    [Fact]
    public async Task AssignmentAndCommentPanels_ShownInInReviewLifecycleAsync()
    {
        var rev = NewInReviewRevision();
        var sut = BuildSut(latest: rev);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.ShowAssignmentPanel.Should().BeTrue();
        sut.Vm.ShowCommentPanel.Should().BeTrue();
        sut.Vm.CommentPanel.Should().NotBeNull();
    }

    [Fact]
    public async Task AssignmentAndCommentPanels_HiddenInDraftLifecycleAsync()
    {
        var sut = BuildSut(latest: NewDraftRevision());

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.ShowAssignmentPanel.Should().BeFalse();
        sut.Vm.ShowCommentPanel.Should().BeFalse();
        sut.Vm.Assignments.Should().BeEmpty();
        sut.Vm.CommentPanel.Should().BeNull();
    }

    [Fact]
    public async Task AssignmentAndCommentPanels_HiddenInApprovedLifecycleAsync()
    {
        var sut = BuildSut(latest: NewApprovedRevision(null));

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.ShowAssignmentPanel.Should().BeFalse();
        sut.Vm.ShowCommentPanel.Should().BeFalse();
    }

    [Fact]
    public async Task Assignments_RenderedWithResolvedDisplayNamesAndStatusBadgesAsync()
    {
        var rev = NewInReviewRevision();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var alice = MakeUser(aliceId, "alice", "Alice Smith");
        var bob = MakeUser(bobId, "bob", "Bob Johnson");
        var pendingA = MakeAssignment(rev.Id, aliceId, DocumentReviewAssignmentStatus.Pending);
        var signedB = MakeAssignment(rev.Id, bobId, DocumentReviewAssignmentStatus.Signed);
        var sut = BuildSut(
            latest: rev,
            assignments: [pendingA, signedB],
            users: [alice, bob]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.Assignments.Should().HaveCount(2);
        var aRow = sut.Vm.Assignments.Single(a => a.AssignmentId == pendingA.Id);
        aRow.ReviewerDisplayName.Should().Be("Alice Smith");
        aRow.Status.Should().Be(DocumentReviewAssignmentStatus.Pending);
        var bRow = sut.Vm.Assignments.Single(a => a.AssignmentId == signedB.Id);
        bRow.ReviewerDisplayName.Should().Be("Bob Johnson");
        bRow.Status.Should().Be(DocumentReviewAssignmentStatus.Signed);
    }

    [Fact]
    public async Task LastReturnToDraftReason_DraftAfterReturn_SurfacedAsync()
    {
        // Build a revision that's been submitted then returned.
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), Now);
        rev.ReturnToDraft("needs section 3 fix");
        var sut = BuildSut(latest: rev);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.LastReturnToDraftReason.Should().Be("needs section 3 fix");
        sut.Vm.HasLastReturnToDraftReason.Should().BeTrue();
    }

    [Fact]
    public async Task LastReturnToDraftReason_FreshDraft_NullAsync()
    {
        var sut = BuildSut(latest: NewDraftRevision());

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.LastReturnToDraftReason.Should().BeNull();
        sut.Vm.HasLastReturnToDraftReason.Should().BeFalse();
    }

    // ─── C6b stop 7: C6a affordance gating reconfirmation ───────────

    [Fact]
    public async Task C6aAffordances_HiddenWhenInReviewAsync()
    {
        // Plan §G affordance-hide-when-not-Draft requirement: the
        // C6a Edit/Replace/HardDelete commands must not be enabled
        // when the latest revision is no longer Draft. The C6a
        // gating already includes IsLatestDraft so this is a
        // confirmation test, not a new behavior.
        var sut = BuildSut(
            latest: NewInReviewRevision(),
            permissions:
            [
                PermissionNames.DocumentEditDraft,
                PermissionNames.DocumentHardDelete,
            ]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanEditMetadata.Should().BeFalse();
        sut.Vm.CanReplacePdf.Should().BeFalse();
        sut.Vm.CanHardDelete.Should().BeFalse();
    }

    [Fact]
    public async Task C6aAffordances_HiddenWhenApprovedAsync()
    {
        var sut = BuildSut(
            latest: NewApprovedRevision(null),
            permissions:
            [
                PermissionNames.DocumentEditDraft,
                PermissionNames.DocumentHardDelete,
            ]);

        await sut.Vm.LoadCommand.ExecuteAsync(null);

        sut.Vm.CanEditMetadata.Should().BeFalse();
        sut.Vm.CanReplacePdf.Should().BeFalse();
        sut.Vm.CanHardDelete.Should().BeFalse();
    }
}
