using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// Content-addressed file storage metadata (SPEC §3.6, ADR 0008).
/// One row per unique file content; identical files (same SHA-256
/// hash) are deduplicated to a single row. The actual file lives on
/// disk at <c>vault/&lt;first-2-chars&gt;/&lt;full-hash&gt;.&lt;ext&gt;</c>;
/// this entity holds the metadata index used to look the file up
/// and to record provenance (original file name, MIME type, size).
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> AuditableEntity — vault metadata is audit-logged
/// but never signed. Attesting to a blob's content is the
/// responsibility of whatever entity references it (typically a
/// <see cref="DocumentRevision"/> via its <c>VaultBlobId</c>); the blob
/// row itself just records the index.
/// </para>
/// <para>
/// <b>Hash uniqueness.</b> The <see cref="Sha256Hash"/> column is
/// uniquely indexed in the data layer — the content addressing
/// invariant depends on it. Attempts to insert a row with a
/// duplicate hash fail at the data layer, which is the dedup
/// mechanism: the vault service detects the existing row before
/// insert and reuses it.
/// </para>
/// <para>
/// <b>Hash format.</b> 64-character lowercase hexadecimal, validated
/// via <see cref="HashFormat.IsValidSha256Hex(string?)"/>. The choice
/// of string over byte[] is per ADR 0008 — 2× storage cost is
/// negligible compared to the human-readability and direct-grep
/// benefits.
/// </para>
/// </remarks>
public class VaultBlob : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// SHA-256 hash of the file content, encoded as exactly 64
    /// lowercase hexadecimal characters. Unique across all rows.
    /// </summary>
    public string Sha256Hash { get; protected set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; protected set; }

    /// <summary>MIME type as known at storage time (e.g., "application/pdf").</summary>
    public string MimeType { get; protected set; } = string.Empty;

    /// <summary>
    /// Original file name as supplied by the uploading client. Stored
    /// for provenance and UI display; not used for any addressing or
    /// retrieval logic (the hash is the address).
    /// </summary>
    public string OriginalFileName { get; protected set; } = string.Empty;

    /// <summary>UTC instant at which the blob was first stored.</summary>
    public DateTime StoredAtUtc { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected VaultBlob()
    {
    }

    /// <summary>
    /// Constructs a new VaultBlob metadata row.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="sha256Hash">SHA-256 hash, exactly 64 lowercase hex
    /// characters.</param>
    /// <param name="fileSizeBytes">File size in bytes. Must be
    /// non-negative.</param>
    /// <param name="mimeType">MIME type. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="originalFileName">Original file name. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="storedAtUtc">UTC instant of storage. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public VaultBlob(
        Guid id,
        string sha256Hash,
        long fileSizeBytes,
        string mimeType,
        string originalFileName,
        DateTime storedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        if (!HashFormat.IsValidSha256Hex(sha256Hash))
        {
            throw new ArgumentException(
                "Sha256Hash must be exactly 64 lowercase hexadecimal characters.",
                nameof(sha256Hash));
        }

        if (fileSizeBytes < 0)
        {
            throw new ArgumentException(
                "FileSizeBytes must be non-negative.",
                nameof(fileSizeBytes));
        }

        if (storedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "StoredAtUtc must have DateTimeKind.Utc.",
                nameof(storedAtUtc));
        }

        Id = id;
        Sha256Hash = sha256Hash;
        FileSizeBytes = fileSizeBytes;
        MimeType = mimeType;
        OriginalFileName = originalFileName;
        StoredAtUtc = storedAtUtc;
    }
}
