using AwesomeAssertions;

using EasySynQ.Services.Identity;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.Identity;

public class PasswordHasherTests
{
    private static PasswordHasher NewFastHasher(int iterationCount = 1000, int minimumLength = 4)
        => new(new PasswordPolicy(
            minimumLength: minimumLength,
            currentIterationCount: iterationCount,
            maxFailedAttempts: 5,
            lockoutDuration: TimeSpan.FromMinutes(15)));

    [Fact]
    public void Hash_PopulatesAllFields_AtTheCurrentIterationCount()
    {
        var hasher = NewFastHasher();
        var hashed = hasher.Hash("password");

        hashed.Hash.Should().NotBeNullOrWhiteSpace();
        hashed.Salt.Should().NotBeNullOrWhiteSpace();
        hashed.IterationCount.Should().Be(1000);
    }

    [Fact]
    public void Hash_ProducesUniqueSaltsForRepeatedHashesOfTheSamePassword()
    {
        var hasher = NewFastHasher();

        var a = hasher.Hash("password");
        var b = hasher.Hash("password");
        var c = hasher.Hash("password");

        a.Salt.Should().NotBe(b.Salt);
        b.Salt.Should().NotBe(c.Salt);
        a.Salt.Should().NotBe(c.Salt);

        // Different salts → different hashes for the same plaintext.
        a.Hash.Should().NotBe(b.Hash);
        b.Hash.Should().NotBe(c.Hash);
    }

    [Fact]
    public void Hash_RejectsTooShortPassword()
    {
        var hasher = NewFastHasher(minimumLength: 12);
        var act = () => hasher.Hash("short");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least 12 characters*");
    }

    [Fact]
    public void Hash_RejectsNullEmptyOrWhitespacePassword()
    {
        var hasher = NewFastHasher();
        ((Action)(() => hasher.Hash(null!))).Should().Throw<ArgumentException>();
        ((Action)(() => hasher.Hash(string.Empty))).Should().Throw<ArgumentException>();
        ((Action)(() => hasher.Hash("   "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_RoundTrip_ReturnsSuccessForCorrectPassword()
    {
        var hasher = NewFastHasher();
        var hashed = hasher.Hash("correct horse battery staple");

        hasher.Verify("correct horse battery staple", hashed.Hash, hashed.Salt, hashed.IterationCount)
            .Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_RoundTrip_ReturnsFailureForWrongPassword()
    {
        var hasher = NewFastHasher();
        var hashed = hasher.Hash("correct horse battery staple");

        hasher.Verify("wrong", hashed.Hash, hashed.Salt, hashed.IterationCount)
            .Should().Be(PasswordVerificationResult.Failure);
    }

    [Fact]
    public void Verify_ReturnsSuccessRequiresRehash_WhenStoredCountBelowPolicy()
    {
        // Hash under iteration count 500.
        var lowHasher = NewFastHasher(iterationCount: 500);
        var hashed = lowHasher.Hash("password");

        // Verify with a hasher whose policy is at iteration count 1000.
        var currentHasher = NewFastHasher(iterationCount: 1000);
        currentHasher.Verify("password", hashed.Hash, hashed.Salt, hashed.IterationCount)
            .Should().Be(PasswordVerificationResult.SuccessRequiresRehash);
    }

    [Fact]
    public void Verify_ReturnsSuccess_WhenStoredCountEqualsPolicy()
    {
        var hasher = NewFastHasher(iterationCount: 1000);
        var hashed = hasher.Hash("password");

        hasher.Verify("password", hashed.Hash, hashed.Salt, hashed.IterationCount)
            .Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ReturnsFailure_OnMalformedBase64Inputs()
    {
        var hasher = NewFastHasher();
        // "@@@" is not valid base64 — the verify path should fail
        // gracefully rather than throwing.
        hasher.Verify("password", "@@@", "@@@", 1000)
            .Should().Be(PasswordVerificationResult.Failure);
    }

    [Fact]
    public void Verify_ReturnsFailure_OnEmptyOrZeroInputs()
    {
        var hasher = NewFastHasher();
        hasher.Verify("password", "", "salt", 1000)
            .Should().Be(PasswordVerificationResult.Failure);
        hasher.Verify("password", "hash", "salt", 0)
            .Should().Be(PasswordVerificationResult.Failure);
    }

    [Fact]
    public void Hash_AndVerify_RoundTrip_AtProductionIterationCount()
    {
        // One slow test to exercise the production iteration count
        // (600,000) end-to-end. ~200 ms each call; the test runs in
        // under a second wall time and proves the production path
        // works.
        var hasher = new PasswordHasher(new PasswordPolicy());
        var hashed = hasher.Hash("twelve characters at minimum");

        hashed.IterationCount.Should().Be(PasswordPolicy.DefaultIterationCount);
        hasher.Verify("twelve characters at minimum", hashed.Hash, hashed.Salt, hashed.IterationCount)
            .Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_SourceCode_UsesFixedTimeEquals()
    {
        // Constant-time comparison is asserted by source inspection.
        // Direct timing-based testing is unreliable on a CI / dev box;
        // the production-code call site is the load-bearing detail
        // (ADR 0006). If a future refactor replaces FixedTimeEquals
        // with == on the byte arrays, this test fails loudly.
        var source = ReadServiceSource("Identity", "PasswordHasher.cs");
        source.Should().Contain(
            "CryptographicOperations.FixedTimeEquals",
            "ADR 0006 mandates constant-time comparison in PasswordHasher.Verify");
    }

    private static string ReadServiceSource(params string[] relativeParts)
    {
        // Walks up from the test bin folder to the repo root (identified
        // by EasySynQ.slnx), then descends into src/EasySynQ.Services
        // and the supplied relative parts. This is brittle to repo
        // layout but the layout is committed to the repo.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "EasySynQ.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new System.IO.FileNotFoundException(
                "Could not locate EasySynQ.slnx walking up from AppContext.BaseDirectory.");
        }
        var path = System.IO.Path.Combine(
            new[] { dir.FullName, "src", "EasySynQ.Services" }
                .Concat(relativeParts)
                .ToArray());
        return System.IO.File.ReadAllText(path);
    }
}
