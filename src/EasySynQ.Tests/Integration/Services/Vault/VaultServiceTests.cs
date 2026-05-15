using System.IO;
using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Authorization;
using EasySynQ.Services.Vault;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Vault;

/// <summary>
/// Integration tests for <see cref="VaultService"/> against a tempdir
/// vault root and a temp SQLite database (ADR 0008 C2). Exercises the
/// content-addressed write path, dedup behavior, integrity-check on
/// read, and the permission-gated physical delete.
/// </summary>
/// <remarks>
/// Inherits from <see cref="ServiceIntegrationTestBase"/> which
/// registers the full DI graph (DbContext + interceptors + repositories
/// + vault services). The vault root is a per-test tempdir created in
/// the base's constructor and cleaned up on dispose.
/// </remarks>
public class VaultServiceTests : ServiceIntegrationTestBase
{
    private const string PdfMimeType = "application/pdf";

    private static byte[] HelloPdfBytes() =>
        Encoding.UTF8.GetBytes("%PDF-1.4 (test bytes — not a real PDF)");

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildShardedPath(string vaultRoot, string hash, string extension) =>
        Path.Combine(vaultRoot, hash[..2], hash + extension);

    [Fact]
    public async Task StoreAsync_NewContent_WritesFileAndRowAsync()
    {
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);

        // Stamp CurrentUser so the standard-fields interceptor has a
        // CreatedBy to write (anonymous bootstrap-time inserts aside,
        // most service-tier writes require an authenticated user).
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        VaultBlob blob;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            blob = await vault.StoreAsync(
                content, PdfMimeType, "report.pdf", ".pdf", Ct);
        }

        // On-disk: sharded path exists with the expected bytes.
        var expectedPath = BuildShardedPath(VaultRoot, expectedHash, ".pdf");
        File.Exists(expectedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(expectedPath, Ct)).Should().Equal(bytes);

        // DB row: hash matches, fields populated, StoredAtUtc set to
        // the fixture clock.
        blob.Sha256Hash.Should().Be(expectedHash);
        blob.FileSizeBytes.Should().Be(bytes.Length);
        blob.MimeType.Should().Be(PdfMimeType);
        blob.OriginalFileName.Should().Be("report.pdf");
        blob.StoredAtUtc.Should().Be(Clock.UtcNow);

        await using (var ctx = NewContext())
        {
            var rowCount = await ctx.VaultBlobs.CountAsync(Ct);
            rowCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task StoreAsync_DuplicateContent_DedupsToExistingRowAsync()
    {
        // Two sequential StoreAsync calls with identical content. The
        // second call should hit the FindByHashAsync dedup path, return
        // the existing blob, and leave on-disk file count at 1, row
        // count at 1.
        //
        // This is the simpler-of-the-two dedup tests proposed in C2's
        // plan; a "truly concurrent" version was rejected as flaky.
        // The rename-collision recovery path in VaultService is
        // defensive code with inline rationale rather than test-covered.
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);

        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        VaultBlob first;
        VaultBlob second;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content1 = new MemoryStream(bytes);
            first = await vault.StoreAsync(content1, PdfMimeType, "first.pdf", ".pdf", Ct);
        }
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content2 = new MemoryStream(bytes);
            second = await vault.StoreAsync(content2, PdfMimeType, "second.pdf", ".pdf", Ct);
        }

        // Same blob row returned (same Id).
        second.Id.Should().Be(first.Id);
        second.Sha256Hash.Should().Be(expectedHash);

        // OriginalFileName on the returned blob is the FIRST write's
        // name — the second call dedupped to the existing row without
        // updating provenance metadata. This is the documented
        // dedup-semantics shape per ADR 0008.
        second.OriginalFileName.Should().Be("first.pdf");

        // On disk: one file at the sharded path, no temp file left in
        // vault root.
        var shardedPath = BuildShardedPath(VaultRoot, expectedHash, ".pdf");
        File.Exists(shardedPath).Should().BeTrue();
        var tempFiles = Directory.EnumerateFiles(VaultRoot, ".tmp-*").ToList();
        tempFiles.Should().BeEmpty();

        // DB: one row total.
        await using (var ctx = NewContext())
        {
            var rowCount = await ctx.VaultBlobs.CountAsync(Ct);
            rowCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task RetrieveAsync_ReadsOriginalBytesAsync()
    {
        var bytes = HelloPdfBytes();
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var retrieved = await vault.RetrieveAsync(blobId, Ct);

            using var ms = new MemoryStream();
            await retrieved.CopyToAsync(ms, Ct);
            ms.ToArray().Should().Equal(bytes);
        }
    }

    [Fact]
    public async Task RetrieveAsync_HashMismatch_ThrowsAsync()
    {
        // Store legitimate content, then manually corrupt the on-disk
        // file by writing extra bytes to it. Retrieve must throw
        // InvalidDataException with both hashes in the message.
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        // Corrupt the on-disk file in place.
        var path = BuildShardedPath(VaultRoot, expectedHash, ".pdf");
        await File.AppendAllTextAsync(path, "corruption", Ct);

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            var act = async () => await vault.RetrieveAsync(blobId, Ct);
            (await act.Should().ThrowAsync<InvalidDataException>())
                .WithMessage("*hash mismatch*")
                .WithMessage($"*{expectedHash}*");
        }
    }

    [Fact]
    public async Task RetrieveAsync_MissingFile_ThrowsAsync()
    {
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        File.Delete(BuildShardedPath(VaultRoot, expectedHash, ".pdf"));

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            var act = async () => await vault.RetrieveAsync(blobId, Ct);
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
    }

    [Fact]
    public async Task RetrieveAsync_UnknownBlobId_ThrowsKeyNotFoundAsync()
    {
        await using var scope = NewScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
        var act = async () => await vault.RetrieveAsync(Guid.NewGuid(), Ct);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ExistsAsync_BothRowAndFile_TrueAsync()
    {
        var bytes = HelloPdfBytes();
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            (await vault.ExistsAsync(blobId, Ct)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ExistsAsync_FileMissing_FalseAsync()
    {
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        File.Delete(BuildShardedPath(VaultRoot, expectedHash, ".pdf"));

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            (await vault.ExistsAsync(blobId, Ct)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExistsAsync_RowMissing_FalseAsync()
    {
        // Unknown blobId — neither row nor file exists.
        await using var scope = NewScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
        (await vault.ExistsAsync(Guid.NewGuid(), Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_SoftDeletedRow_FalseAsync()
    {
        var bytes = HelloPdfBytes();
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        // Soft-delete the row directly (the repository's SoftDelete
        // path; the operational soft-delete UX is C3+ scope).
        await using (var ctx = NewContext())
        {
            var blob = await ctx.VaultBlobs.SingleAsync(b => b.Id == blobId, Ct);
            ctx.Entry(blob).Property("IsDeleted").CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            (await vault.ExistsAsync(blobId, Ct)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task PhysicalDeleteAsync_WithoutPermission_ThrowsAsync()
    {
        var bytes = HelloPdfBytes();
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";
        // Permissions intentionally empty — the throw should fire
        // regardless of any other state.

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            var act = async () => await vault.PhysicalDeleteAsync(blobId, Ct);
            var exception = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
            exception.PermissionName.Should().Be(PermissionNames.VaultPhysicalDelete);
        }
    }

    [Fact]
    public async Task PhysicalDeleteAsync_WithPermission_DeletesFileAndRowAsync()
    {
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "x.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        // Grant the permission and re-fire.
        CurrentUser.Permissions = [PermissionNames.VaultPhysicalDelete];

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await vault.PhysicalDeleteAsync(blobId, Ct);
        }

        // File gone.
        var path = BuildShardedPath(VaultRoot, expectedHash, ".pdf");
        File.Exists(path).Should().BeFalse();

        // Row gone (hard-deleted; not soft-deleted — verify by
        // bypassing the soft-delete filter).
        await using (var ctx = NewContext())
        {
            var anyRow = await ctx.VaultBlobs
                .IgnoreQueryFilters()
                .AnyAsync(b => b.Id == blobId, Ct);
            anyRow.Should().BeFalse();
        }
    }

    [Fact]
    public async Task PhysicalDeleteAsync_WritesHardDeleteAuditRowWithPreDeleteSnapshotAsync()
    {
        // The audit interceptor (per ADR 0002) emits a HardDelete row
        // with the pre-delete snapshot in `Before` and `After = null`.
        // Pin that shape against the VaultBlob hard-delete path.
        var bytes = HelloPdfBytes();
        var expectedHash = Sha256Hex(bytes);
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        Guid blobId;
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var content = new MemoryStream(bytes);
            var blob = await vault.StoreAsync(content, PdfMimeType, "report.pdf", ".pdf", Ct);
            blobId = blob.Id;
        }

        CurrentUser.Permissions = [PermissionNames.VaultPhysicalDelete];

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await vault.PhysicalDeleteAsync(blobId, Ct);
        }

        await using (var ctx = NewContext())
        {
            var hardDeleteRow = await ctx.AuditLogEntries
                .Where(a => a.EntityTypeName == "VaultBlob"
                         && a.EntityId == blobId.ToString())
                .OrderByDescending(a => a.UtcTimestamp)
                .FirstAsync(Ct);

            hardDeleteRow.Action.Should().Be(Domain.Enums.AuditAction.HardDelete);
            hardDeleteRow.Before.Should().NotBeNull();
            hardDeleteRow.Before!.Should().Contain(expectedHash);
            hardDeleteRow.Before.Should().Contain("report.pdf");
            hardDeleteRow.After.Should().BeNull();
        }
    }

    [Fact]
    public async Task PhysicalDeleteAsync_UnknownBlobId_ThrowsKeyNotFoundAsync()
    {
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";
        CurrentUser.Permissions = [PermissionNames.VaultPhysicalDelete];

        await using var scope = NewScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
        var act = async () => await vault.PhysicalDeleteAsync(Guid.NewGuid(), Ct);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task StoreAsync_TwoDifferentContents_WritesTwoRowsAndTwoFilesAsync()
    {
        // Sanity: dedup only fires for IDENTICAL content. Different
        // bytes → different hashes → two rows, two files.
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";

        var bytesA = Encoding.UTF8.GetBytes("alpha");
        var bytesB = Encoding.UTF8.GetBytes("beta");

        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var a = new MemoryStream(bytesA);
            await vault.StoreAsync(a, PdfMimeType, "a.bin", ".bin", Ct);
        }
        await using (var scope = NewScope())
        {
            var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
            await using var b = new MemoryStream(bytesB);
            await vault.StoreAsync(b, PdfMimeType, "b.bin", ".bin", Ct);
        }

        await using (var ctx = NewContext())
        {
            (await ctx.VaultBlobs.CountAsync(Ct)).Should().Be(2);
        }

        File.Exists(BuildShardedPath(VaultRoot, Sha256Hex(bytesA), ".bin")).Should().BeTrue();
        File.Exists(BuildShardedPath(VaultRoot, Sha256Hex(bytesB), ".bin")).Should().BeTrue();
    }
}
