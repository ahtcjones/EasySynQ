using System.Globalization;
using System.IO;
using System.Security.Cryptography;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Authorization;
using EasySynQ.Services.Time;

using Microsoft.Extensions.Logging;

namespace EasySynQ.Services.Vault;

/// <summary>
/// Production <see cref="IVaultService"/>. Implements content-addressed
/// file storage on the local filesystem with database-backed metadata
/// (SPEC §3.6, ADR 0008).
/// </summary>
public sealed partial class VaultService : IVaultService
{
    private const string TempFilePrefix = ".tmp-";

    private readonly IVaultBlobRepository _blobs;
    private readonly IVaultPathProvider _pathProvider;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VaultService> _logger;

    /// <summary>Constructs the service over its dependencies.</summary>
    public VaultService(
        IVaultBlobRepository blobs,
        IVaultPathProvider pathProvider,
        ICurrentUserAccessor currentUser,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<VaultService> logger)
    {
        ArgumentNullException.ThrowIfNull(blobs);
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);
        _blobs = blobs;
        _pathProvider = pathProvider;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VaultBlob> StoreAsync(
        Stream content,
        string mimeType,
        string originalFileName,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        var vaultRoot = _pathProvider.VaultRoot;
        Directory.CreateDirectory(vaultRoot);

        // Stream input → temp file in vault root, computing SHA-256
        // incrementally. Temp file is named ".tmp-<guid>" so concurrent
        // writes don't collide and so post-crash temp files are
        // identifiable by prefix for cleanup tools.
        var tempPath = Path.Combine(vaultRoot, TempFilePrefix + Guid.NewGuid().ToString("N"));
        long fileSize;
        string hashHex;

        try
        {
            await using (var tempFile = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                fileSize = 0;
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await tempFile.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    fileSize += read;
                }
                await tempFile.FlushAsync(cancellationToken);
                hashHex = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            }

            // Dedup check — cheap path. If a row already exists, drop
            // the temp file and return the existing blob without
            // touching the sharded path.
            var existing = await _blobs.FindByHashAsync(hashHex, cancellationToken);
            if (existing is not null)
            {
                SafeDeleteTemp(tempPath);
                LogVaultDedupHit(_logger, hashHex, existing.Id);
                return existing;
            }

            // No existing row. Compute the final sharded path,
            // ensure the shard directory exists, and attempt an atomic
            // rename. File.Move with overwrite: false throws IOException
            // if the destination exists — that's the race signal.
            var finalPath = BuildFinalPath(vaultRoot, hashHex, fileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            try
            {
                File.Move(tempPath, finalPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // Rename-collision recovery path. Another writer placed
                // a file at finalPath between our FindByHashAsync and
                // our File.Move. The on-disk file is theirs; our temp
                // file's content matches theirs (same hash) so the
                // dedup target is correct. Drop our temp file and
                // requery for the row the other writer should have
                // just inserted (or about to insert).
                //
                // Within-process this is rarely exercised because
                // EF Core + SQLite serialize their writes; the
                // recovery is defensive shape for future multi-process
                // Local Service Mode (ADR 0001). Inline comment instead
                // of a flaky parallel test, per the C2 plan.
                SafeDeleteTemp(tempPath);
                var raced = await _blobs.FindByHashAsync(hashHex, cancellationToken);
                if (raced is not null)
                {
                    LogVaultDedupHit(_logger, hashHex, raced.Id);
                    return raced;
                }
                // File exists but no row — the other writer is
                // mid-insert or failed before inserting. We could try
                // to insert our own row pointing at their file
                // (self-healing) but that crosses a clear line into
                // surprising behavior. Throw with diagnostic.
                throw new InvalidOperationException(
                    $"Vault collision: file present at sharded path for hash {hashHex} but no " +
                    $"matching VaultBlob row exists. A concurrent writer may have failed mid-insert. " +
                    $"Manual reconciliation required.");
            }

            // File is in place at the sharded path. Insert the row;
            // the audit interceptor emits an Insert audit row in the
            // same transaction.
            var blob = new VaultBlob(
                id: Guid.NewGuid(),
                sha256Hash: hashHex,
                fileSizeBytes: fileSize,
                mimeType: mimeType,
                originalFileName: originalFileName,
                storedAtUtc: _clock.UtcNow);

            await _blobs.AddAsync(blob, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            LogVaultStoreNew(_logger, hashHex, blob.Id, fileSize);
            return blob;
        }
        catch
        {
            // Any pre-row-insert failure leaves at most the temp file
            // on disk; clean it up so repeated failures don't leak.
            SafeDeleteTemp(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MemoryStream> RetrieveAsync(Guid blobId, CancellationToken cancellationToken)
    {
        if (blobId == Guid.Empty)
        {
            throw new ArgumentException("BlobId must not be Guid.Empty.", nameof(blobId));
        }

        var blob = await _blobs.GetByIdAsync(blobId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"VaultBlob {blobId} not found (no row, or row is soft-deleted).");

        var path = BuildFinalPath(_pathProvider.VaultRoot, blob.Sha256Hash, GetExtensionFromFileName(blob.OriginalFileName));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"VaultBlob {blobId} row exists but the on-disk file at " +
                $"'{path}' is missing.",
                path);
        }

        // Read entire file into memory while hashing, then verify the
        // recomputed hash matches the stored hash. The MemoryStream is
        // returned positioned at zero; caller disposes.
        var buffer = new MemoryStream();
        string computedHash;
        await using (var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await fileStream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
            {
                hasher.AppendData(chunk, 0, read);
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            computedHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        if (!string.Equals(computedHash, blob.Sha256Hash, StringComparison.Ordinal))
        {
            // Hash mismatch — tamper or on-disk corruption. Throw with
            // both hashes in the message so the operator can
            // investigate. Don't return the buffer (caller would see
            // bad bytes).
            buffer.Dispose();
            LogVaultHashMismatch(_logger, blobId, blob.Sha256Hash, computedHash);
            throw new InvalidDataException(
                $"VaultBlob {blobId} on-disk content hash mismatch. " +
                $"Stored: {blob.Sha256Hash}. Computed: {computedHash}. " +
                $"The on-disk file may be tampered or corrupted.");
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid blobId, CancellationToken cancellationToken)
    {
        if (blobId == Guid.Empty)
        {
            throw new ArgumentException("BlobId must not be Guid.Empty.", nameof(blobId));
        }

        var blob = await _blobs.GetByIdAsync(blobId, cancellationToken);
        if (blob is null)
        {
            return false;
        }

        var path = BuildFinalPath(_pathProvider.VaultRoot, blob.Sha256Hash, GetExtensionFromFileName(blob.OriginalFileName));
        return File.Exists(path);
    }

    /// <inheritdoc />
    public async Task PhysicalDeleteAsync(Guid blobId, CancellationToken cancellationToken)
    {
        if (blobId == Guid.Empty)
        {
            throw new ArgumentException("BlobId must not be Guid.Empty.", nameof(blobId));
        }

        if (!_currentUser.Permissions.Contains(PermissionNames.VaultPhysicalDelete))
        {
            throw UnauthorizedOperationException.ForMissingPermission(
                PermissionNames.VaultPhysicalDelete);
        }

        var blob = await _blobs.GetByIdAsync(blobId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"VaultBlob {blobId} not found (no row, or row is soft-deleted).");

        var path = BuildFinalPath(_pathProvider.VaultRoot, blob.Sha256Hash, GetExtensionFromFileName(blob.OriginalFileName));

        // DB first, file second per the C2 design. The audit log is the
        // compliance source of truth; a stale on-disk file after a
        // committed row delete is recoverable, an orphan row pointing
        // at a missing file is worse.
        _blobs.HardDelete(blob);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            // Row + audit row are already committed. Surface the
            // failure at Critical so the operator can clean up the
            // orphan file; do NOT throw upstream — the compliance
            // state (audit log says blob deleted at T) is intact and
            // the caller's contract (the blob is no longer addressable
            // via VaultService) is satisfied.
            LogVaultOrphanFile(_logger, blobId, path, ex);
        }

        LogVaultPhysicalDelete(_logger, blobId, blob.Sha256Hash);
    }

    /// <inheritdoc />
    public async Task<string> GetVaultFilePathAsync(Guid blobId, CancellationToken cancellationToken)
    {
        if (blobId == Guid.Empty)
        {
            throw new ArgumentException("BlobId must not be Guid.Empty.", nameof(blobId));
        }

        var blob = await _blobs.GetByIdAsync(blobId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"VaultBlob {blobId} not found (no row, or row is soft-deleted).");

        var path = BuildFinalPath(
            _pathProvider.VaultRoot,
            blob.Sha256Hash,
            GetExtensionFromFileName(blob.OriginalFileName));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"VaultBlob {blobId} row exists but the on-disk file at " +
                $"'{path}' is missing.",
                path);
        }

        // Hash validation per ADR 0010 C5 — the path is about to be
        // handed to the PDF viewer; this is exactly the moment when
        // tamper / corruption must surface. Reads the whole file once
        // for the hash; same cost as RetrieveAsync's hash check.
        await ValidateHashOnDiskAsync(path, blob.Sha256Hash, blobId, cancellationToken);

        return path;
    }

    /// <summary>
    /// Reads the file at <paramref name="path"/> and recomputes its
    /// SHA-256, throwing <see cref="InvalidDataException"/> if the
    /// computed hash does not match <paramref name="expectedHash"/>.
    /// Used by <see cref="GetVaultFilePathAsync"/> per ADR 0010 C5;
    /// <see cref="RetrieveAsync"/> retains its own single-pass
    /// hash-while-reading shape and does not call this helper.
    /// </summary>
    private static async Task ValidateHashOnDiskAsync(
        string path,
        string expectedHash,
        Guid blobId,
        CancellationToken cancellationToken)
    {
        string computedHash;
        await using (var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await fileStream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
            {
                hasher.AppendData(chunk, 0, read);
            }
            computedHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        if (!string.Equals(computedHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"VaultBlob {blobId} on-disk content hash mismatch. " +
                $"Stored: {expectedHash}. Computed: {computedHash}. " +
                $"The on-disk file may be tampered or corrupted.");
        }
    }

    private static string BuildFinalPath(string vaultRoot, string sha256Hash, string fileExtension)
    {
        // SHA-256 hex is always lowercase 64 chars by the entity's
        // construction-time validation; the first two chars are the
        // shard directory.
        var shard = sha256Hash[..2];
        var normalizedExtension = NormalizeExtension(fileExtension);
        var fileName = sha256Hash + normalizedExtension;
        return Path.Combine(vaultRoot, shard, fileName);
    }

    private static string NormalizeExtension(string? fileExtension)
    {
        // Caller may pass ".pdf" or "pdf" or "" or null. Normalize to
        // ".pdf" or "". Lowercase the result; vault filenames are
        // hash-addressed and case stability matters on
        // case-sensitive filesystems.
        if (string.IsNullOrWhiteSpace(fileExtension))
        {
            return string.Empty;
        }

        var trimmed = fileExtension.Trim();
        var withDot = trimmed.StartsWith('.') ? trimmed : "." + trimmed;
        return withDot.ToLowerInvariant();
    }

    private static string GetExtensionFromFileName(string originalFileName)
    {
        // RetrieveAsync and ExistsAsync don't have a separate extension
        // input — they reconstruct the sharded path from the stored
        // row. Path.GetExtension returns ".pdf" or "" for fileless
        // names; normalize to lowercase for case-stable lookup.
        var ext = Path.GetExtension(originalFileName);
        return string.IsNullOrEmpty(ext) ? string.Empty : ext.ToLowerInvariant();
    }

    private static void SafeDeleteTemp(string tempPath)
    {
        // Best-effort cleanup; called from error paths where throwing
        // would mask the original failure.
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
            // Will be revisited by a future vault-housekeeping pass.
        }
    }

    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Information,
        Message = "Vault store: new blob {BlobId} stored for hash {Sha256Hash} ({FileSizeBytes} bytes).")]
    private static partial void LogVaultStoreNew(
        ILogger<VaultService> logger,
        string sha256Hash,
        Guid blobId,
        long fileSizeBytes);

    [LoggerMessage(
        EventId = 8002,
        Level = LogLevel.Information,
        Message = "Vault store: dedup hit for hash {Sha256Hash}; returning existing blob {BlobId}.")]
    private static partial void LogVaultDedupHit(
        ILogger<VaultService> logger,
        string sha256Hash,
        Guid blobId);

    [LoggerMessage(
        EventId = 8003,
        Level = LogLevel.Error,
        Message = "Vault retrieve: hash mismatch on blob {BlobId}. Stored: {StoredHash}. Computed: {ComputedHash}. Possible tamper or corruption.")]
    private static partial void LogVaultHashMismatch(
        ILogger<VaultService> logger,
        Guid blobId,
        string storedHash,
        string computedHash);

    [LoggerMessage(
        EventId = 8004,
        Level = LogLevel.Information,
        Message = "Vault physical delete: blob {BlobId} (hash {Sha256Hash}) removed.")]
    private static partial void LogVaultPhysicalDelete(
        ILogger<VaultService> logger,
        Guid blobId,
        string sha256Hash);

    [LoggerMessage(
        EventId = 8005,
        Level = LogLevel.Critical,
        Message = "Vault physical delete: row + audit committed for blob {BlobId} but on-disk file at '{Path}' could not be deleted. Orphan file requires manual cleanup.")]
    private static partial void LogVaultOrphanFile(
        ILogger<VaultService> logger,
        Guid blobId,
        string path,
        Exception exception);
}
