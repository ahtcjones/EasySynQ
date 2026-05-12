using EasySynQ.Domain.Common;
using EasySynQ.Domain.Enums;

namespace EasySynQ.Domain.Entities.Snapshots;

/// <summary>
/// One snapshot package — ZIP archive of the operational database plus
/// the Document Vault — produced by the snapshot service per SPEC §3.3.
/// </summary>
/// <remarks>
/// Snapshots are retained per tier:
/// <list type="bullet">
///   <item><see cref="SnapshotTier.Daily"/> — retained 90 days.</item>
///   <item><see cref="SnapshotTier.Weekly"/> — retained one year.</item>
///   <item><see cref="SnapshotTier.Monthly"/> — retained indefinitely
///   (configurable cap).</item>
/// </list>
/// Each snapshot's <see cref="ZipSha256"/> is recorded at creation; the
/// integrity-verification job re-hashes the ZIP and calls
/// <see cref="MarkIntegrityVerified(DateTime)"/> when the hashes match.
/// </remarks>
public class Snapshot : AuditableEntity
{
    /// <summary>Unique snapshot identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Retention tier governing this snapshot.</summary>
    public SnapshotTier Tier { get; protected set; }

    // CreatedUtc is inherited from AuditableEntity. The snapshot's
    // creation time is semantically the entity's CreatedUtc — passed
    // into the constructor and assigned to the inherited property.

    /// <summary>
    /// File path of the ZIP archive, relative to the snapshots root
    /// directory configured in the deployment topology (SPEC §3.1).
    /// </summary>
    public string FilePath { get; protected set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the snapshot ZIP file, encoded as 64 lowercase
    /// hexadecimal characters. Recorded at creation; used by the
    /// integrity-verification job.
    /// </summary>
    public string ZipSha256 { get; protected set; } = string.Empty;

    /// <summary>Total byte size of the snapshot ZIP file.</summary>
    public long ByteSize { get; protected set; }

    /// <summary>
    /// Byte size of the database file included in the snapshot.
    /// </summary>
    public long IncludedDatabaseSize { get; protected set; }

    /// <summary>
    /// Byte size of the vault directory included in the snapshot.
    /// </summary>
    public long IncludedVaultSize { get; protected set; }

    /// <summary>
    /// True when the integrity-verification job has confirmed that the
    /// stored ZIP still matches the recorded <see cref="ZipSha256"/>.
    /// </summary>
    public bool IntegrityVerified { get; protected set; }

    /// <summary>
    /// UTC instant of the most recent successful integrity verification,
    /// or <see langword="null"/> if the snapshot has never been verified.
    /// </summary>
    public DateTime? IntegrityVerifiedUtc { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected Snapshot()
    {
    }

    /// <summary>
    /// Constructs a new snapshot record. Initial integrity state is
    /// unverified.
    /// </summary>
    /// <param name="id">Unique snapshot identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="tier">Retention tier.</param>
    /// <param name="createdUtc">UTC instant of creation. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="filePath">Relative file path of the ZIP. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="zipSha256">SHA-256 hash of the ZIP, as 64 lowercase
    /// hexadecimal characters.</param>
    /// <param name="byteSize">Total ZIP size in bytes. Must be
    /// non-negative.</param>
    /// <param name="includedDatabaseSize">Database file size in bytes.
    /// Must be non-negative.</param>
    /// <param name="includedVaultSize">Vault directory size in bytes.
    /// Must be non-negative.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any
    /// size argument is negative.</exception>
    public Snapshot(
        Guid id,
        SnapshotTier tier,
        DateTime createdUtc,
        string filePath,
        string zipSha256,
        long byteSize,
        long includedDatabaseSize,
        long includedVaultSize)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (createdUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "CreatedUtc must have DateTimeKind.Utc.",
                nameof(createdUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipSha256);

        if (!HashFormat.IsValidSha256Hex(zipSha256))
        {
            throw new ArgumentException(
                "ZipSha256 must be exactly 64 lowercase hexadecimal characters.",
                nameof(zipSha256));
        }

        if (byteSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteSize), byteSize, "ByteSize must be non-negative.");
        }
        if (includedDatabaseSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(includedDatabaseSize),
                includedDatabaseSize,
                "IncludedDatabaseSize must be non-negative.");
        }
        if (includedVaultSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(includedVaultSize),
                includedVaultSize,
                "IncludedVaultSize must be non-negative.");
        }

        Id = id;
        Tier = tier;
        CreatedUtc = createdUtc;
        FilePath = filePath;
        ZipSha256 = zipSha256;
        ByteSize = byteSize;
        IncludedDatabaseSize = includedDatabaseSize;
        IncludedVaultSize = includedVaultSize;
        IntegrityVerified = false;
        IntegrityVerifiedUtc = null;
    }

    /// <summary>
    /// Marks this snapshot as integrity-verified at the supplied UTC
    /// instant. Callable multiple times — subsequent calls update the
    /// timestamp to the latest verification.
    /// </summary>
    /// <param name="utc">UTC instant of the successful verification.
    /// Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="utc"/> is not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    public void MarkIntegrityVerified(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "UTC instant must have DateTimeKind.Utc.",
                nameof(utc));
        }

        IntegrityVerified = true;
        IntegrityVerifiedUtc = utc;
    }
}
