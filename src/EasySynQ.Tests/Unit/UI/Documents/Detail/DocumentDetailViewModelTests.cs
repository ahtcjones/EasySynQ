using System.IO;

using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Vault;
using EasySynQ.Tests.TestHelpers;
using EasySynQ.UI.Documents;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Documents.EditMetadata;

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
    private static (DocumentDetailViewModel Vm,
                    Mock<IDocumentRepository> Docs,
                    Mock<IDocumentRevisionRepository> Revs,
                    Mock<IVaultService> Vault,
                    Mock<IDocumentLifecycleService> Lifecycle,
                    Mock<IFilePicker> Picker,
                    Mock<IEditMetadataPrompter> EditPrompter)
        BuildSut(
            DocumentRevision? latest = null,
            IReadOnlyCollection<string>? permissions = null,
            Guid? currentUserId = null,
            string? vaultPath = null,
            Document? document = null)
    {
        var doc = document ?? NewDocument();
        var docs = new Mock<IDocumentRepository>();
        docs.Setup(d => d.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var revs = new Mock<IDocumentRevisionRepository>();
        revs.Setup(r => r.GetLatestRevisionAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

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
        var pathProvider = new Mock<IVaultPathProvider>();
        pathProvider.SetupGet(p => p.VaultRoot).Returns(TestVaultRoot);

        var vm = new DocumentDetailViewModel(
            doc,
            docs.Object,
            revs.Object,
            vault.Object,
            pathProvider.Object,
            clock,
            currentUser,
            lifecycle.Object,
            picker.Object,
            prompter.Object);

        return (vm, docs, revs, vault, lifecycle, picker, prompter);
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
}
