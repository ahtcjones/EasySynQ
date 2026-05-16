using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Vault;

/// <summary>
/// Content-addressed file storage service (SPEC §3.6, ADR 0008).
/// Stores and retrieves vault blobs by SHA-256 of their content;
/// dedupes identical content automatically (the same bytes written
/// twice produce one row + one file). Recomputes the hash on every
/// read so a tampered or corrupted on-disk file surfaces as a thrown
/// exception rather than silently returning bad bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>On-disk layout.</b> Files live at
/// <c>{VaultRoot}\{first-2-chars-of-hash}\{full-hash}{extension}</c>.
/// The 2-character sharding spreads files across 256 subdirectories so
/// no single directory accumulates an unbounded entry count. Sharded
/// subdirectories are created on first write.
/// </para>
/// <para>
/// <b>Atomicity.</b> Content is buffered to a temp file in the vault
/// root while the hash is computed incrementally, then atomically
/// renamed to its final sharded path. A concurrent write of the same
/// content loses the rename race; the loser deletes its temp file and
/// requeries for the winner's row. Within-process, this is rarely
/// exercised — but the recovery shape is the safe failure mode for
/// future multi-process Local Service Mode.
/// </para>
/// <para>
/// <b>PhysicalDelete is gated by permission.</b>
/// <see cref="PhysicalDeleteAsync"/> requires the current user to hold
/// <c>Vault.PhysicalDelete</c> (added to the system catalog in Phase 2's
/// vault-permission migration; seeded to the Administrator role). Other
/// methods require an authenticated user (<c>CurrentUserAccessor.UserId</c>
/// non-null) only insofar as the standard-fields interceptor needs one
/// to attribute writes; they do not gate on a specific permission.
/// </para>
/// </remarks>
public interface IVaultService
{
    /// <summary>
    /// Stores <paramref name="content"/> in the vault. Computes SHA-256
    /// of the bytes as they're streamed to a temp file; checks the DB
    /// for an existing <see cref="VaultBlob"/> row with the same hash;
    /// if present, dedupes (deletes the temp file, returns the existing
    /// row). If absent, atomically renames the temp file to its final
    /// sharded path and writes a new <see cref="VaultBlob"/> row in a
    /// single transactional save.
    /// </summary>
    /// <param name="content">The content stream. Read forward to end;
    /// caller retains ownership and disposes after this call returns.
    /// Must not be <see langword="null"/>.</param>
    /// <param name="mimeType">MIME type for the content (e.g.,
    /// <c>"application/pdf"</c>). Stored in
    /// <see cref="VaultBlob.MimeType"/>; not validated against actual
    /// content shape.</param>
    /// <param name="originalFileName">Display name as supplied by the
    /// uploader. Stored for provenance; not used for any addressing or
    /// retrieval logic.</param>
    /// <param name="fileExtension">Extension to use in the on-disk
    /// filename (with or without leading dot, e.g. <c>".pdf"</c> or
    /// <c>"pdf"</c>). The full filename becomes
    /// <c>{hash}{normalized-extension}</c>; an empty/whitespace
    /// extension produces <c>{hash}</c> with no suffix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="VaultBlob"/> row — new (just inserted)
    /// on the unique-content path; existing (dedup hit) when content
    /// matched a prior write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="mimeType"/> or
    /// <paramref name="originalFileName"/> is null/empty/whitespace.</exception>
    Task<VaultBlob> StoreAsync(
        Stream content,
        string mimeType,
        string originalFileName,
        string fileExtension,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the file backing <paramref name="blobId"/>, recomputes its
    /// SHA-256, and returns a stream over the bytes. Throws if the
    /// recomputed hash does not match the stored
    /// <see cref="VaultBlob.Sha256Hash"/> (tamper / corruption
    /// detection); throws if the row exists but the on-disk file does
    /// not.
    /// </summary>
    /// <remarks>
    /// Returns a fully-materialized <see cref="MemoryStream"/> positioned
    /// at zero; caller disposes. Memory cost is proportional to file
    /// size; acceptable for Phase 2's pilot-deployment file sizes
    /// (typically &lt; 20 MB). A streaming-wrapper-with-trailer-check
    /// alternative was rejected because callers would see partial bytes
    /// before the trailer throw — bad shape for compliance-sensitive
    /// content.
    /// </remarks>
    /// <param name="blobId">Identifier of the <see cref="VaultBlob"/> to
    /// retrieve. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="MemoryStream"/> positioned at zero, owning
    /// the buffered bytes. Caller disposes.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="blobId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no
    /// non-soft-deleted <see cref="VaultBlob"/> row exists for the
    /// supplied id.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the row
    /// exists but the on-disk file is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the on-disk
    /// content's SHA-256 does not match the stored hash (tamper /
    /// corruption).</exception>
    Task<MemoryStream> RetrieveAsync(Guid blobId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <see langword="true"/> when both the
    /// <see cref="VaultBlob"/> row (non-soft-deleted) and its on-disk
    /// file exist. Both must be true; either missing returns
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="blobId">Identifier to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when both row and file exist;
    /// <see langword="false"/> otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="blobId"/> is <see cref="Guid.Empty"/>.</exception>
    Task<bool> ExistsAsync(Guid blobId, CancellationToken cancellationToken);

    /// <summary>
    /// Physically removes the vault blob — hard-deletes the
    /// <see cref="VaultBlob"/> row (writing a <c>HardDelete</c> audit
    /// row per SPEC §3.5 / ADR 0002) and deletes the on-disk file.
    /// Gated on the current user holding the
    /// <c>Vault.PhysicalDelete</c> permission.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ordering.</b> The DB row is deleted (and its audit row written)
    /// in a single transactional save BEFORE the on-disk file is removed.
    /// If the file delete fails post-commit, the audit log remains the
    /// source of truth ("this blob was hard-deleted at T"); the orphan
    /// file is a finding for follow-up but does not corrupt the
    /// compliance posture.
    /// </para>
    /// <para>
    /// <b>Safety.</b> This operation is unrecoverable. Hard-deletes are
    /// permitted only on draft state per SPEC §3.5; callers (lifecycle
    /// service, future admin tool) must verify the blob is not
    /// referenced by any signed record before invoking this.
    /// VaultService does not check those references — it is the
    /// physical-delete primitive, not the policy gate.
    /// </para>
    /// </remarks>
    /// <param name="blobId">Identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="blobId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no
    /// non-soft-deleted <see cref="VaultBlob"/> row exists for the
    /// supplied id.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">
    /// Thrown when the current user does not hold the
    /// <c>Vault.PhysicalDelete</c> permission.</exception>
    Task PhysicalDeleteAsync(Guid blobId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the on-disk file path for the supplied vault blob,
    /// without opening or buffering the file's contents (ADR 0010
    /// C5). Used by the PDF viewer integration which needs a path to
    /// hand to WebView2's URI loader rather than a stream. Performs
    /// the same defensive checks as <see cref="RetrieveAsync"/>: the
    /// row exists, the file exists at the expected sharded path, and
    /// the on-disk content's SHA-256 matches the stored hash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hash validation cost.</b> The hash recomputation reads the
    /// entire file from disk — same cost as
    /// <see cref="RetrieveAsync"/>. For typical pilot-deployment PDFs
    /// (under 20 MB), this is sub-second on local or fast network
    /// storage. The cost is accepted as the price of the
    /// tamper-evidence guarantee SPEC §3.6 requires; a "fast path"
    /// variant that skips validation is not exposed (the viewer call
    /// site is the moment where the user is about to look at the
    /// document — hash validation is exactly the right time).
    /// </para>
    /// </remarks>
    /// <param name="blobId">Identifier of the
    /// <see cref="VaultBlob"/> whose path to return. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The absolute on-disk path of the vault file.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="blobId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no
    /// non-soft-deleted <see cref="VaultBlob"/> row exists for the
    /// supplied id.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the row
    /// exists but the on-disk file is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the on-disk
    /// content's SHA-256 does not match the stored hash (tamper /
    /// corruption).</exception>
    Task<string> GetVaultFilePathAsync(Guid blobId, CancellationToken cancellationToken);
}
