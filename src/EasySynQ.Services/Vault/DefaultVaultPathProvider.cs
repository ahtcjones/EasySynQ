using System.IO;

namespace EasySynQ.Services.Vault;

/// <summary>
/// Production <see cref="IVaultPathProvider"/>. Returns
/// <c>%LOCALAPPDATA%\EasySynQ\vault\</c> — co-located with the
/// SQLite database file under <c>%LOCALAPPDATA%\EasySynQ\</c> so the
/// vault and the DB share a single host-side data root.
/// </summary>
/// <remarks>
/// The path is computed once at construction. <see cref="VaultRoot"/>
/// returns the cached string on every call — no I/O. Directory
/// creation is the caller's concern (<see cref="VaultService"/> creates
/// the root + sharded subdirectories on first write).
/// </remarks>
public sealed class DefaultVaultPathProvider : IVaultPathProvider
{
    /// <inheritdoc />
    public string VaultRoot { get; }

    /// <summary>
    /// Constructs the provider with the default
    /// <c>%LOCALAPPDATA%\EasySynQ\vault\</c> path.
    /// </summary>
    public DefaultVaultPathProvider()
    {
        VaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasySynQ",
            "vault");
    }
}
