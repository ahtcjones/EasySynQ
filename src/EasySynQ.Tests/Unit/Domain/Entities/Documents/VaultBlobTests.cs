using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class VaultBlobTests
{
    // A representative valid SHA-256 hex string (lowercase, 64 chars).
    // Content arbitrary; format-shape is what the test cares about.
    private const string ValidHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly DateTime StoredAt =
        new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_PopulatesAllFields()
    {
        var id = Guid.NewGuid();

        var blob = new VaultBlob(
            id, ValidHash, 1024, "application/pdf", "report.pdf", StoredAt);

        blob.Id.Should().Be(id);
        blob.Sha256Hash.Should().Be(ValidHash);
        blob.FileSizeBytes.Should().Be(1024);
        blob.MimeType.Should().Be("application/pdf");
        blob.OriginalFileName.Should().Be("report.pdf");
        blob.StoredAtUtc.Should().Be(StoredAt);
    }

    [Fact]
    public void Constructor_AcceptsZeroByteFile()
    {
        // Edge case — an empty PDF is uncommon but valid; the hash of
        // an empty byte sequence is itself a valid SHA-256.
        var blob = new VaultBlob(
            Guid.NewGuid(), ValidHash, 0, "application/octet-stream", "empty.bin", StoredAt);
        blob.FileSizeBytes.Should().Be(0);
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new VaultBlob(
            Guid.Empty, ValidHash, 1, "x", "y", StoredAt);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsBlankSha256Hash()
    {
        Action act = () => new VaultBlob(
            Guid.NewGuid(), "   ", 1, "x", "y", StoredAt);
        act.Should().Throw<ArgumentException>().WithMessage("*sha256Hash*");
    }

    [Fact]
    public void Constructor_RejectsShortSha256Hash()
    {
        // 63 chars — one short of valid SHA-256 hex.
        const string Short = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85";
        Action act = () => new VaultBlob(
            Guid.NewGuid(), Short, 1, "x", "y", StoredAt);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsUppercaseHexInSha256Hash()
    {
        // Uppercase A-F — HashFormat enforces lowercase only.
        const string Uppercase =
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        Action act = () => new VaultBlob(
            Guid.NewGuid(), Uppercase, 1, "x", "y", StoredAt);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsBlankMimeType()
    {
        Action act = () => new VaultBlob(
            Guid.NewGuid(), ValidHash, 1, "", "y", StoredAt);
        act.Should().Throw<ArgumentException>().WithMessage("*mimeType*");
    }

    [Fact]
    public void Constructor_RejectsBlankOriginalFileName()
    {
        Action act = () => new VaultBlob(
            Guid.NewGuid(), ValidHash, 1, "x", "\t", StoredAt);
        act.Should().Throw<ArgumentException>().WithMessage("*originalFileName*");
    }

    [Fact]
    public void Constructor_RejectsNegativeFileSize()
    {
        Action act = () => new VaultBlob(
            Guid.NewGuid(), ValidHash, -1, "x", "y", StoredAt);
        act.Should().Throw<ArgumentException>().WithMessage("*FileSizeBytes*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcStoredAt()
    {
        var local = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
        Action act = () => new VaultBlob(
            Guid.NewGuid(), ValidHash, 1, "x", "y", local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
