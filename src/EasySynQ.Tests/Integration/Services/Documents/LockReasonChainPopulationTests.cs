using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.LockReasons;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Documents;

/// <summary>
/// Regression net for SPEC §4.3 / CLAUDE.md non-negotiable rule #5:
/// every Phase 2 lockout state must produce a resolvable
/// <see cref="EasySynQ.Domain.Entities.Audit.LockReason"/> chain when
/// inspected (ADR 0012 C7b). Each test exercises a real lifecycle
/// transition end-to-end via <see cref="DocumentLifecycleService"/>,
/// then asks the registered <see cref="ILockReasonResolverRegistry"/>
/// to resolve the resulting state — the chain shape, navigation
/// references, and terminal placement are pinned per ADR 0012's
/// chain templates. The lifecycle service is unchanged from C7a;
/// these tests verify that the resolver reads the post-transition
/// state correctly across the full transition matrix.
/// </summary>
public sealed class LockReasonChainPopulationTests : ServiceIntegrationTestBase
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

    private async Task<(Guid DocumentId, Guid RevisionId)> SeedDocumentAndDraftAsync(
        Guid authorUserId,
        string number,
        string title = "Test Document",
        string revisionLabel = "Rev A")
    {
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

    private void BecomeAuthor(Guid authorId)
    {
        CurrentUser.UserId = authorId;
        CurrentUser.Username = $"author-{authorId:N}".Substring(0, 16);
        CurrentUser.Roles = [AuthorRole];
        CurrentUser.Permissions = AuthorPermissions.ToList();
        CurrentUser.RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [AuthorRole] = AuthorPermissions.ToList(),
        };
    }

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

    private static IDocumentLifecycleService Lifecycle(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDocumentLifecycleService>();

    private static ILockReasonResolverRegistry Registry(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ILockReasonResolverRegistry>();

    private static async Task<EasySynQ.Domain.Entities.Audit.LockReason?> ResolveRevisionAsync(
        AsyncServiceScope scope,
        Guid revisionId,
        CancellationToken ct)
    {
        var resolver = Registry(scope).GetResolver(LockedEntityTypes.DocumentRevision);
        resolver.Should().NotBeNull();
        return await resolver!.ResolveAsync(revisionId.ToString("D"), ct);
    }

    private static async Task<EasySynQ.Domain.Entities.Audit.LockReason?> ResolveDocumentAsync(
        AsyncServiceScope scope,
        Guid documentId,
        CancellationToken ct)
    {
        var resolver = Registry(scope).GetResolver(LockedEntityTypes.Document);
        resolver.Should().NotBeNull();
        return await resolver!.ResolveAsync(documentId.ToString("D"), ct);
    }

    [Fact]
    public async Task DraftRevision_ResolvesToNull_NotLockedAsync()
    {
        // L0 (not a lockout) — Draft revisions are author-editable.
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-001");

        await using var scope = NewScope();
        var chain = await ResolveRevisionAsync(scope, revisionId, Ct);

        chain.Should().BeNull();
    }

    [Fact]
    public async Task InReviewRevision_ResolvesToL2TerminalChainAsync()
    {
        var authorId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-002");

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }

        await using (var scope = NewScope())
        {
            var chain = await ResolveRevisionAsync(scope, revisionId, Ct);

            chain.Should().NotBeNull();
            chain!.LockedEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
            chain.Chain.Should().ContainSingle();
            chain.Chain[0].IsTerminal.Should().BeTrue();
            chain.Chain[0].Id.Should().Be("Rev A");
            chain.Chain[0].Detail.Should().Contain("In Review");
        }
    }

    [Fact]
    public async Task ApprovedRevision_ResolvesToL1TerminalChainAsync()
    {
        var authorId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-003");

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

        await using (var scope = NewScope())
        {
            var chain = await ResolveRevisionAsync(scope, revisionId, Ct);

            chain.Should().NotBeNull();
            chain!.Chain.Should().ContainSingle();
            chain.Chain[0].IsTerminal.Should().BeTrue();
            chain.Chain[0].Detail.Should().Contain("Approved on");
        }
    }

    [Fact]
    public async Task SupersededRevision_ResolvesToL3TwoLinkChainAsync()
    {
        // Approve Rev A, then create Rev B (Draft → InReview →
        // Approved). Rev A becomes Superseded atomically per ADR 0008.
        var authorId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var (documentId, revisionAId) = await SeedDocumentAndDraftAsync(authorId, "SOP-004");

        BecomeAuthor(authorId);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionAId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionAId, ReviewerRole, Ct);
        }

        // Seed a new Draft Rev B + submit + approve.
        BecomeAuthor(authorId);
        Clock.UtcNow = Clock.UtcNow.AddMinutes(5);
        var revisionBId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.DocumentRevisions.Add(new DocumentRevision(
                revisionBId, documentId, "Rev B", authorId));
            await ctx.SaveChangesAsync(Ct);
        }
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SubmitForReviewAsync(
                revisionBId, [reviewer], effectiveFromUtc: null, signingAsRole: AuthorRole, Ct);
        }
        BecomeReviewer(reviewer);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).SignAsReviewerAsync(revisionBId, ReviewerRole, Ct);
        }

        await using (var scope = NewScope())
        {
            // Rev A is now Superseded; resolver returns the L3
            // two-link chain with Rev B as the successor target.
            var chain = await ResolveRevisionAsync(scope, revisionAId, Ct);

            chain.Should().NotBeNull();
            chain!.Chain.Should().HaveCount(2);
            chain.Chain[0].IsTerminal.Should().BeFalse();
            chain.Chain[0].Id.Should().Be("Rev A");
            chain.Chain[0].Because.Should().Be("superseded by");
            chain.Chain[0].NavigationEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
            chain.Chain[0].NavigationEntityId.Should().Be(revisionBId.ToString("D"));
            chain.Chain[1].IsTerminal.Should().BeTrue();
            chain.Chain[1].Id.Should().Be("Rev B");
        }
    }

    [Fact]
    public async Task RetiredDocument_ResolvesToL5TerminalChainAsync()
    {
        var authorId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-005");

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
        Clock.UtcNow = Clock.UtcNow.AddMinutes(5);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).RetireAsync(documentId, AuthorRole, Ct);
        }

        await using (var scope = NewScope())
        {
            // Document-level resolve → L5 chain (Document is Retired).
            var docChain = await ResolveDocumentAsync(scope, documentId, Ct);

            docChain.Should().NotBeNull();
            docChain!.LockedEntityType.Should().Be(LockedEntityTypes.Document);
            docChain.Chain.Should().ContainSingle();
            docChain.Chain[0].IsTerminal.Should().BeTrue();
            docChain.Chain[0].Id.Should().Be("SOP-005");
            docChain.Chain[0].Detail.Should().Contain("Retired on");
        }
    }

    [Fact]
    public async Task ArchivedRevision_ResolvesToL4TwoLinkChainAsync()
    {
        // Retire the Document and the active revision becomes
        // Archived per ADR 0008. Revision-level resolve → L4 chain.
        var authorId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var (documentId, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-006");

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
        Clock.UtcNow = Clock.UtcNow.AddMinutes(5);
        await using (var scope = NewScope())
        {
            await Lifecycle(scope).RetireAsync(documentId, AuthorRole, Ct);
        }

        await using (var scope = NewScope())
        {
            var chain = await ResolveRevisionAsync(scope, revisionId, Ct);

            chain.Should().NotBeNull();
            chain!.Chain.Should().HaveCount(2);
            chain.Chain[0].IsTerminal.Should().BeFalse();
            chain.Chain[0].Because.Should().Be("parent Document was retired");
            chain.Chain[0].NavigationEntityType.Should().Be(LockedEntityTypes.Document);
            chain.Chain[0].NavigationEntityId.Should().Be(documentId.ToString("D"));
            chain.Chain[1].IsTerminal.Should().BeTrue();
            chain.Chain[1].Tag.Should().Be(LockedEntityTypes.Document);
        }
    }

    [Fact]
    public async Task SoftDeletedRevision_ResolvesToL6TerminalChainAsync()
    {
        // Soft-delete a Draft revision via direct repository call —
        // production has no service path that soft-deletes a revision
        // (HardDelete is the Draft removal path) but the resolver
        // must still surface the L6 chain when admin tooling lands.
        var authorId = Guid.NewGuid();
        var (_, revisionId) = await SeedDocumentAndDraftAsync(authorId, "SOP-007");

        CurrentUser.UserId = authorId;
        CurrentUser.Username = "admin-soft-delete";
        await using (var scope = NewScope())
        {
            var revisionRepo = scope.ServiceProvider
                .GetRequiredService<IRepository<DocumentRevision, Guid>>();
            var revision = await revisionRepo.GetByIdAsync(revisionId, Ct);
            revision.Should().NotBeNull();
            revisionRepo.SoftDelete(revision!);
            await revisionRepo.SaveChangesAsync(Ct);
        }

        await using (var scope = NewScope())
        {
            var chain = await ResolveRevisionAsync(scope, revisionId, Ct);

            chain.Should().NotBeNull();
            chain!.Chain.Should().ContainSingle();
            chain.Chain[0].IsTerminal.Should().BeTrue();
            chain.Chain[0].Detail.Should().Contain("Soft-deleted on");
        }
    }

    [Fact]
    public async Task SoftDeletedDocument_ResolvesToL6TerminalChainAsync()
    {
        var authorId = Guid.NewGuid();
        var (documentId, _) = await SeedDocumentAndDraftAsync(authorId, "SOP-008");

        CurrentUser.UserId = authorId;
        CurrentUser.Username = "admin-soft-delete";
        await using (var scope = NewScope())
        {
            var docRepo = scope.ServiceProvider
                .GetRequiredService<IRepository<Document, Guid>>();
            var doc = await docRepo.GetByIdAsync(documentId, Ct);
            doc.Should().NotBeNull();
            docRepo.SoftDelete(doc!);
            await docRepo.SaveChangesAsync(Ct);
        }

        await using (var scope = NewScope())
        {
            var chain = await ResolveDocumentAsync(scope, documentId, Ct);

            chain.Should().NotBeNull();
            chain!.Chain.Should().ContainSingle();
            chain.Chain[0].IsTerminal.Should().BeTrue();
            chain.Chain[0].Detail.Should().Contain("Soft-deleted on");
        }
    }

    [Fact]
    public async Task RegistryDispatchesByLockedEntityTypeAsync()
    {
        // Sanity pin: the two registered resolvers are
        // type-distinct. A scope's registry lookup returns the
        // correct one per key, and the wrong key returns null.
        await using var scope = NewScope();
        var registry = Registry(scope);

        registry.GetResolver(LockedEntityTypes.Document)
            .Should().NotBeNull()
            .And.Subject.As<ILockReasonResolver>()
            .LockedEntityType.Should().Be(LockedEntityTypes.Document);

        registry.GetResolver(LockedEntityTypes.DocumentRevision)
            .Should().NotBeNull()
            .And.Subject.As<ILockReasonResolver>()
            .LockedEntityType.Should().Be(LockedEntityTypes.DocumentRevision);

        registry.GetResolver("UnknownType").Should().BeNull();
    }
}
