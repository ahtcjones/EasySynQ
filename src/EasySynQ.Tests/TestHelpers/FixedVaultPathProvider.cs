using EasySynQ.Services.Vault;

namespace EasySynQ.Tests.TestHelpers;

/// <summary>
/// Test double for <see cref="IVaultPathProvider"/> returning a
/// caller-controlled root path. Used by integration tests that want
/// a tempdir vault root rather than the production
/// <c>%LOCALAPPDATA%\EasySynQ\vault\</c> default.
/// </summary>
public sealed class FixedVaultPathProvider : IVaultPathProvider
{
    /// <inheritdoc />
    public string VaultRoot { get; }

    /// <summary>Constructs the provider with the supplied root path.</summary>
    /// <param name="vaultRoot">Absolute path to use as the vault root.
    /// Must not be null/empty/whitespace.</param>
    public FixedVaultPathProvider(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        VaultRoot = vaultRoot;
    }
}
