using AwesomeAssertions;

using EasySynQ.Data.Context;
using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Domain.Events.Documents;
using EasySynQ.Services.Authorization;
using EasySynQ.Services.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Documents;

/// <summary>
/// Integration tests for <see cref="DocumentLifecycleService"/> (ADR
/// 0008 C3). Exercises every state-machine transition listed in ADR
/// 0008 §"Required tests": Draft→InReview, InReview→Draft with
/// signature reset, InReview→Approved on all-signed,
/// Active→Superseded on new approval, Active→Archived on retire. Pins
/// audit-row counts (derived from N reviewers per the C2 handoff's
/// historical-vs-current lesson) and verifies
/// <see cref="DocumentRevisionApprovedEvent"/> publication via the
/// recording dispatcher.
/// </summary>
public class DocumentLifecycleServiceTests : ServiceIntegrationTestBase
{
    private static readonly IReadOnlyList<string> AuthorPermissions =
    [
        PermissionNames.DocumentCreate,
        PermissionNames.DocumentEditDraft,
        PermissionNames.DocumentSubmitForReview,
        PermissionNames.DocumentAssignReviewers,
        PermissionNames.DocumentReturnForEdits,
        PermissionNames.DocumentRetire,
    ];

    private static readonly IReadOnlyList<string> ReviewerPermissions =
    [
        PermissionNames.DocumentReview,
    ];

    private const string AuthorRole = "TestAuthor";
    private const string ReviewerRole = "TestReviewer";

    /// <summary>Seeds a Document and a single Draft revision; returns
    /// both ids.</summary>
    private async Task<(Guid DocumentId, Guid RevisionId)> SeedDocumentAndDraftAsync(
        Guid authorUserId,
        string number = "SOP-001",
        string title = "Test Document",
        string revisionLabel = "Rev A")
    {
        // Use a non-service write path to set up fixture state. The
        // CurrentUser is set briefly so standard-fields stamping has
        // an actor; the business-rule services are not in play.
        CurrentUser.UserId = authorUserId;
        CurrentUser.Username = "fixture";
        CurrentUser.Roles = [AuthorRole];

        var doc = new Document(Guid.NewGuid(), number, title);
        var rev = new DocumentRevision(Guid.NewGuid(), doc.Id, revisionLabel, authorUserId);

        await using var ctx = NewContext();
        ctx.Documents.Add(doc);
        ctx.DocumentRevisions.Add(rev);
        await ctx.SaveChangesAsync(Ct);

        return (doc.Id, rev.Id);
    }

    /// <summary>Configures CurrentUser as the supplied "author"
    /// identity with the author permission set + single role.</summary>
    private void BecomeAuthor(Guid authorId)
    {
        CurrentUser.UserId = authorId;
        CurrentUser.Username = $"author-{authorId:N}".Substring(0, 16);
        CurrentUser.Roles = [AuthorRole];
        CurrentUser.Permissions = AuthorPermissions.ToList();
        // ADR 0009 — RolePermissions snapshot mirrors the flat
        // Permissions: the author's only role grants every permission
        // in AuthorPermissions. SignatureService validates that
        // signingAsRole is a member of Roles; lifecycle methods now
        // require this.
        CurrentUser.RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [AuthorRole] = AuthorPermissions.ToList(),
        };
    }

    /// <summary>Configures CurrentUser as the supplied "reviewer"
    /// identity with Document.Review + single role.</summary>
    private void BecomeReviewer(Guid reviewerId)
    {
        CurrentUser.UserId = reviewerId;
        CurrentUser.Username = $"rev-{reviewerId:N}".Substring(0, 16);
        CurrentUser.Roles = [ReviewerRole];
        CurrentUser.Permissions = ReviewerPermissions.ToList();
        CurrentUser.RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [ReviewerRole] = ReviewerPermissions.ToList(),
        };
    }

    private async Task<int> CountAuditRowsAsync(Guid? correlationId = null)
    {
        await using var ctx = NewContext();
        var query = ctx.AuditLogEntries.AsQueryable();
        if (correlationId is not null)
        {
            query = query.Where(a => a.CorrelationId == correlationId.Value);
        }
        return await query.CountAsync(Ct);
    }

    private async Task<List<EasySynQ.Domain.Entities.Audit.AuditLogEntry>> AuditRowsByCorrelationAsync(
        Guid correlationId)
    {
        await using var ctx = NewContext();
        return await ctx.AuditLogEntries
            .Where(a => a.CorrelationId == correlationId)
            .ToListAsync(Ct);
    }

    private static IDocumentLifecycleService Lifecycle(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDocumentLifecycleService>();

    // ─── SubmitForReviewAsync ───────────────────────────────────────

    [Fact]
    public async Task Submit_HappyPathTwoReviewers_TransitionsAndAuditsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewerA = Guid.NewGuid();
        var reviewerB = Guid.NewGuid();

        BecomeAuthor(authorId);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewerA, reviewerB], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.InReview);
            rev.AuthorSignatureId.Should().NotBeNull();
            rev.LockedAtUtc.Should().Be(Clock.UtcNow);

            var assignments = await ctx.DocumentReviewAssignments
                .Where(a => a.DocumentRevisionId == revisionId)
                .ToListAsync(Ct);
            assignments.Should().HaveCount(2);
            assignments.Should().OnlyContain(a => a.Status == DocumentReviewAssignmentStatus.Pending);
            assignments.Select(a => a.ReviewerUserId)
                .Should().BeEquivalentTo([reviewerA, reviewerB]);
        }

        // Audit row count = 2 + N (revision Update, Signature Insert,
        // N assignment Inserts), all sharing the test-set CorrelationId.
        var rows = await AuditRowsByCorrelationAsync(corr);
        const int reviewerCount = 2;
        rows.Count.Should().Be(2 + reviewerCount);
        rows.Should().OnlyContain(r => r.CorrelationId == corr);
    }

    [Fact]
    public async Task Submit_SingleReviewer_AuditCountIsThreeAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();

        BecomeAuthor(authorId);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        // 2 + N where N = 1 → 3 rows.
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(2 + 1);
    }

    [Fact]
    public async Task Submit_WithFutureEffectiveFromUtc_StoresOnRevisionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        var futureEffective = Clock.UtcNow.AddDays(30);

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], futureEffective, AuthorRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.EffectiveFromUtc.Should().Be(futureEffective);
            rev.Lifecycle.Should().Be(DocumentLifecycle.InReview);
        }
    }

    [Fact]
    public async Task Submit_EmptyReviewerList_ThrowsArgumentExceptionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*at least one reviewer*");
    }

    [Fact]
    public async Task Submit_DuplicateReviewerIds_ThrowsArgumentExceptionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [reviewer, reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*duplicate user IDs*");
    }

    [Fact]
    public async Task Submit_AuthorInOwnReviewerList_ThrowsArgumentExceptionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [authorId], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Author cannot review their own document*");
    }

    [Fact]
    public async Task Submit_MissingSubmitForReviewPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);
        // Drop SubmitForReview, keep AssignReviewers.
        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentSubmitForReview)
            .ToList();

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [Guid.NewGuid()], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentSubmitForReview);
    }

    [Fact]
    public async Task Submit_MissingAssignReviewersPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);
        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentAssignReviewers)
            .ToList();

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [Guid.NewGuid()], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentAssignReviewers);
    }

    [Fact]
    public async Task Submit_FromInReview_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).SubmitForReviewAsync(
            revisionId, [Guid.NewGuid()], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Submit_UnauthenticatedCaller_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);

        // Default test-base CurrentUser state — UserId is null.
        CurrentUser.UserId = null;
        CurrentUser.Username = string.Empty;
        CurrentUser.Roles = [];
        CurrentUser.Permissions = [];

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, [Guid.NewGuid()], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ICurrentUserAccessor.UserId is null*");
    }

    // ─── ReturnToDraftAsync ─────────────────────────────────────────

    [Fact]
    public async Task ReturnToDraft_AllPendingAssignments_DiscardsThemAndResetsRevisionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewerA = Guid.NewGuid();
        var reviewerB = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewerA, reviewerB], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).ReturnToDraftAsync(revisionId, "test return reason", Ct);
        }

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.Draft);
            rev.AuthorSignatureId.Should().BeNull();
            // LockedAtUtc preserved per SignableEntity contract.
            rev.LockedAtUtc.Should().NotBeNull();
            // C6b: reason stamped on the revision so the author sees it.
            rev.LastReturnToDraftReason.Should().Be("test return reason");

            var assignments = await ctx.DocumentReviewAssignments
                .Where(a => a.DocumentRevisionId == revisionId)
                .ToListAsync(Ct);
            assignments.Should().HaveCount(2);
            assignments.Should().OnlyContain(a => a.Status == DocumentReviewAssignmentStatus.Discarded);
        }

        // Audit count for return = 1 + N (revision Update, N assignment
        // Updates). The reason rides on the revision-Update row's After
        // snapshot — adding the reason parameter does not change the
        // 1+N formula.
        var rows = await AuditRowsByCorrelationAsync(corr);
        const int reviewerCount = 2;
        rows.Count.Should().Be(1 + reviewerCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReturnToDraft_NullOrWhitespaceReason_ThrowsAsync(string? reason)
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using var scope2 = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope2).ReturnToDraftAsync(revisionId, reason!, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReturnToDraft_ResubmitAfterReturn_ClearsLastReturnReasonAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).ReturnToDraftAsync(revisionId, "first reject", Ct);
        }

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.InReview);
            // C6b: re-submission clears the live reason — the prior
            // value survives in the audit log only.
            rev.LastReturnToDraftReason.Should().BeNull();
        }
    }

    [Fact]
    public async Task ReturnToDraft_OneSignedAssignment_DiscardsItButPreservesSignatureRowAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewerA = Guid.NewGuid();
        var reviewerB = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewerA, reviewerB], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        // Reviewer A signs.
        BecomeReviewer(reviewerA);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        // Author returns to draft. We need the Author permission set.
        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).ReturnToDraftAsync(revisionId, "test return reason", Ct);
        }

        await using (var ctx = NewContext())
        {
            var assignments = await ctx.DocumentReviewAssignments
                .Where(a => a.DocumentRevisionId == revisionId)
                .ToListAsync(Ct);
            assignments.Should().OnlyContain(a => a.Status == DocumentReviewAssignmentStatus.Discarded);

            // The signed assignment kept its signature reference per
            // ADR 0008 §"Signatures reset" — the signature row is
            // preserved for audit even though the assignment no longer
            // counts toward approval.
            var signedAndDiscarded = assignments.Single(a => a.SignatureId is not null);
            signedAndDiscarded.SignatureId.Should().NotBeNull();
            signedAndDiscarded.SignedAtUtc.Should().NotBeNull();

            // The Signature row is still in the database.
            var sigRow = await ctx.Signatures
                .SingleAsync(s => s.Id == signedAndDiscarded.SignatureId!.Value, Ct);
            sigRow.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ReturnToDraft_FromDraft_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).ReturnToDraftAsync(revisionId, "test return reason", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReturnToDraft_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        // Strip the ReturnForEdits permission.
        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentReturnForEdits)
            .ToList();

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).ReturnToDraftAsync(revisionId, "test return reason", Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentReturnForEdits);
    }

    // ─── SignAsReviewerAsync ────────────────────────────────────────

    [Fact]
    public async Task Sign_FirstOfTwoReviewers_RevisionStaysInReviewAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewerA = Guid.NewGuid();
        var reviewerB = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewerA, reviewerB], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewerA);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        DocumentReviewAssignment signed;
        await using (var scope = NewScope())
        {
            signed = await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        signed.Status.Should().Be(DocumentReviewAssignmentStatus.Signed);
        signed.SignedAtUtc.Should().Be(Clock.UtcNow);
        signed.SignatureId.Should().NotBeNull();

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.InReview);
            rev.ApprovedAtUtc.Should().BeNull();
        }

        // Audit count for non-final reviewer sign = 2 (Signature
        // Insert + assignment Update).
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(2);

        // No event published — revision is not yet Approved.
        EventDispatcher.Recorded
            .OfType<DocumentRevisionApprovedEvent>()
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Sign_FinalReviewer_TransitionsToApprovedAndPublishesEventAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewer);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.Approved);
            rev.ApprovedAtUtc.Should().Be(Clock.UtcNow);
        }

        // Audit count for final reviewer sign with no prior Active = 3
        // (Signature Insert + assignment Update + revision Update).
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(3);

        // DocumentRevisionApprovedEvent published with PriorRevisionId
        // null (no prior Active for this fresh document).
        var ev = EventDispatcher.Recorded
            .OfType<DocumentRevisionApprovedEvent>()
            .Should().ContainSingle().Subject;
        ev.DocumentRevisionId.Should().Be(revisionId);
        ev.DocumentId.Should().Be(documentId);
        ev.PriorRevisionId.Should().BeNull();
        ev.ApprovedAtUtc.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public async Task Sign_FinalReviewerOnRevB_SupersedesRevAAndPublishesEventWithPriorIdAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, revAId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();

        // Drive Rev A to Approved (Active immediately, no
        // EffectiveFromUtc).
        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revAId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revAId, ReviewerRole, Ct);
        }

        // Now create Rev B as a sibling Draft revision on the same
        // Document. (The "create new revision" UI surface ships in
        // C6; here we set up the row directly per fixture.)
        Guid revBId;
        BecomeAuthor(authorId);
        await using (var ctx = NewContext())
        {
            var revB = new DocumentRevision(Guid.NewGuid(), documentId, "Rev B", authorId);
            ctx.DocumentRevisions.Add(revB);
            await ctx.SaveChangesAsync(Ct);
            revBId = revB.Id;
        }

        // Submit + sign Rev B → expect Rev A → Superseded, Rev B →
        // Approved, event with PriorRevisionId = Rev A.
        Clock.UtcNow = Clock.UtcNow.AddMinutes(10);  // distinguish ApprovedAtUtc
        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revBId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);

        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revBId, ReviewerRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var revA = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revAId, Ct);
            revA.Lifecycle.Should().Be(DocumentLifecycle.Superseded);

            var revB = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revBId, Ct);
            revB.Lifecycle.Should().Be(DocumentLifecycle.Approved);
            revB.ApprovedAtUtc.Should().Be(Clock.UtcNow);
        }

        // Audit count for final reviewer sign with prior Active = 4
        // (Signature Insert + assignment Update + Rev B Update + Rev A Update).
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(4);

        var ev = EventDispatcher.Recorded
            .OfType<DocumentRevisionApprovedEvent>()
            .Where(e => e.DocumentRevisionId == revBId)
            .Should().ContainSingle().Subject;
        ev.PriorRevisionId.Should().Be(revAId);
    }

    [Fact]
    public async Task Sign_ReviewerNotInAssignmentList_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var assignedReviewer = Guid.NewGuid();
        var unrelatedUser = Guid.NewGuid();

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [assignedReviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(unrelatedUser);
        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not in the assigned reviewer list*");
    }

    [Fact]
    public async Task Sign_ReviewerAlreadySigned_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewerA = Guid.NewGuid();
        var reviewerB = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewerA, reviewerB], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewerA);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        // Same reviewer signs again — already signed → throws.
        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already signed*");
    }

    [Fact]
    public async Task Sign_RevisionNotInReview_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeReviewer(reviewer);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Sign_MissingReviewPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewer);
        CurrentUser.Permissions = [];

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentReview);
    }

    // ─── RetireAsync ────────────────────────────────────────────────

    [Fact]
    public async Task Retire_DocumentWithActiveRevision_ArchivesItAndStampsRetirementFieldsAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();

        // Drive revision to Active.
        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        // Retire as the author (who has Document.Retire permission).
        BecomeAuthor(authorId);
        Clock.UtcNow = Clock.UtcNow.AddMinutes(5);  // distinct timestamp
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).RetireAsync(documentId, AuthorRole, Ct);
        }

        await using (var ctx = NewContext())
        {
            var doc = await ctx.Documents.SingleAsync(d => d.Id == documentId, Ct);
            doc.RetiredAtUtc.Should().Be(Clock.UtcNow);
            doc.RetiredByUserId.Should().Be(authorId);
            doc.RetirementSignatureId.Should().NotBeNull();

            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.Archived);
        }

        // Audit count for retire = 3 (Signature Insert + Document
        // Update + DocumentRevision Update).
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(3);
    }

    [Fact]
    public async Task Retire_DocumentAlreadyRetired_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }
        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).RetireAsync(documentId, AuthorRole, Ct);
        }

        // Second retire on the same doc → entity guard throws.
        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).RetireAsync(documentId, AuthorRole, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been retired*");
    }

    [Fact]
    public async Task Retire_NoActiveRevision_ThrowsInvalidOperationAsync()
    {
        // Document with only a Draft revision → no Active → cannot retire.
        var authorId = Guid.NewGuid();
        var (documentId, _) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).RetireAsync(documentId, AuthorRole, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no currently-Active revision*");
    }

    [Fact]
    public async Task Retire_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionId, ReviewerRole, Ct);
        }

        // Author with Retire permission stripped.
        BecomeAuthor(authorId);
        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentRetire)
            .ToList();

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).RetireAsync(documentId, AuthorRole, Ct);
        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentRetire);
    }

    [Fact]
    public async Task Retire_UnknownDocument_ThrowsKeyNotFoundAsync()
    {
        BecomeAuthor(Guid.NewGuid());
        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).RetireAsync(Guid.NewGuid(), AuthorRole, Ct);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ─── Misc ───────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitForReview_RejectsNullReviewerCollectionAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        BecomeAuthor(authorId);

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            revisionId, null!, effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UnknownRevision_ThrowsKeyNotFound_OnSubmitAsync()
    {
        BecomeAuthor(Guid.NewGuid());
        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).SubmitForReviewAsync(
            Guid.NewGuid(), [Guid.NewGuid()], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ─── AddCommentAsync (C6b) ──────────────────────────────────────

    [Fact]
    public async Task AddComment_HappyPath_InsertsCommentAndOneAuditRowAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewer);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            var comment = await Lifecycle(scope).AddCommentAsync(
                revisionId, "this section needs more detail", Ct);

            comment.Id.Should().NotBe(Guid.Empty);
            comment.DocumentRevisionId.Should().Be(revisionId);
            comment.AuthorUserId.Should().Be(reviewer);
            comment.BodyText.Should().Be("this section needs more detail");
            comment.CreatedAtUtc.Should().Be(Clock.UtcNow);
        }

        await using (var ctx = NewContext())
        {
            var comments = await ctx.DocumentReviewComments
                .Where(c => c.DocumentRevisionId == revisionId)
                .ToListAsync(Ct);
            comments.Should().ContainSingle()
                .Which.BodyText.Should().Be("this section needs more detail");
        }

        // Audit row count = 1 (comment Insert).
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(1);
    }

    [Fact]
    public async Task AddComment_NotInReview_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        // Revision still in Draft — never submitted.
        BecomeReviewer(Guid.NewGuid());

        await using var scope = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope).AddCommentAsync(revisionId, "premature", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot add a comment*expected 'InReview'*");
    }

    [Fact]
    public async Task AddComment_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        // Strip Document.Review.
        BecomeReviewer(reviewer);
        CurrentUser.Permissions = [];
        CurrentUser.RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.Ordinal)
        {
            [ReviewerRole] = [],
        };

        await using var scope2 = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope2).AddCommentAsync(revisionId, "denied", Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentReview);
    }

    [Fact]
    public async Task AddComment_UnknownRevision_ThrowsKeyNotFoundAsync()
    {
        BecomeReviewer(Guid.NewGuid());

        await using var scope = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope).AddCommentAsync(Guid.NewGuid(), "ghost", Ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddComment_NullOrWhitespaceBody_ThrowsAsync(string? body)
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        BecomeReviewer(reviewer);

        await using var scope2 = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope2).AddCommentAsync(revisionId, body!, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddComment_NoAuthenticatedUser_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId);
        var reviewer = Guid.NewGuid();
        BecomeAuthor(authorId);

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        // Unauthenticated.
        CurrentUser.UserId = null;
        CurrentUser.Username = string.Empty;
        CurrentUser.Roles = [];
        CurrentUser.Permissions = [];

        await using var scope2 = NewScope();
        Func<Task> act = async () =>
            await Lifecycle(scope2).AddCommentAsync(revisionId, "anon", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
