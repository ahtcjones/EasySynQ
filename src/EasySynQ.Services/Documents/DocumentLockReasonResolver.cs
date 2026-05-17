using System.Globalization;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;

namespace EasySynQ.Services.Documents;

/// <summary>
/// Resolves <see cref="LockReason"/> chains for
/// <see cref="LockedEntityTypes.Document"/> lockouts (ADR 0012 C7a).
/// Phase 2 document-level lockouts:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>L5 Retired Document</b> — <see cref="Document.RetiredAtUtc"/>
///   is non-null; the document is permanently withdrawn.</item>
///   <item><b>L6 Soft-deleted Document</b> — administratively removed.
///   Takes precedence over L5 (a soft-deleted retired document
///   surfaces the soft-delete cause, not the retirement).</item>
/// </list>
/// <para>
/// Returns <see langword="null"/> for any other state (the document
/// is not locked) and for unknown ids.
/// </para>
/// </remarks>
public sealed class DocumentLockReasonResolver : ILockReasonResolver
{
    private readonly IDocumentRepository _documents;

    /// <inheritdoc />
    public string LockedEntityType => LockedEntityTypes.Document;

    /// <summary>Constructs the resolver over the supplied document
    /// repository.</summary>
    /// <param name="documents">Repository surface for
    /// <see cref="Document"/> rows.</param>
    public DocumentLockReasonResolver(IDocumentRepository documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public async Task<LockReason?> ResolveAsync(
        string lockedEntityId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);

        if (!Guid.TryParse(lockedEntityId, out var documentId))
        {
            return null;
        }

        // Reach soft-deleted rows too — the L6 chain is the primary
        // case for a soft-deleted Document. Live rows are returned by
        // the same call.
        var document = await _documents
            .GetByIdIncludingDeletedAsync(documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // L6 takes precedence: a soft-deleted row's primary cause is
        // the soft-delete, not whatever lifecycle state it held when
        // removed.
        if (document.IsDeleted)
        {
            return BuildSoftDeleteChain(
                LockedEntityType,
                document.Id,
                document.Number,
                document.ModifiedUtc,
                document.ModifiedBy);
        }

        // L5: Retired Document.
        if (document.RetiredAtUtc is not null)
        {
            return BuildRetiredDocumentChain(document);
        }

        // Not locked.
        return null;
    }

    private static LockReason BuildRetiredDocumentChain(Document document)
    {
        var retiredAt = document.RetiredAtUtc!.Value;
        var retiredBy = document.RetiredByUserId!.Value;

        var link = new LockReasonLink(
            tag: LockedEntityTypes.Document,
            id: document.Number,
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"Retired on {retiredAt:yyyy-MM-dd HH:mm:ss} UTC by user {retiredBy:D}. No further revisions may be authored."),
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.Document,
            lockedEntityId: document.Id.ToString("D", CultureInfo.InvariantCulture),
            chain: [link]);
    }

    internal static LockReason BuildSoftDeleteChain(
        string lockedEntityType,
        Guid entityId,
        string displayIdentifier,
        DateTime modifiedUtc,
        string modifiedBy)
    {
        var link = new LockReasonLink(
            tag: lockedEntityType,
            id: displayIdentifier,
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"Soft-deleted on {modifiedUtc:yyyy-MM-dd HH:mm:ss} UTC by {modifiedBy}. The row remains for audit purposes; restoration requires admin action."),
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: lockedEntityType,
            lockedEntityId: entityId.ToString("D", CultureInfo.InvariantCulture),
            chain: [link]);
    }
}
