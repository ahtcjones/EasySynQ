namespace EasySynQ.Services.Vault;

/// <summary>
/// Source of the vault root directory path (SPEC §3.6, ADR 0008).
/// Abstracted behind this interface so the default production location
/// (<c>%LOCALAPPDATA%\EasySynQ\vault\</c>) and test-time tempdir paths
/// share a single dependency-injection seam. Mirrors the
/// <see cref="EasySynQ.Services.Time.IClock"/> pattern: production code
/// uses the default implementation; tests substitute a controlled
/// alternative without environment-variable mutation.
/// </summary>
/// <remarks>
/// Follow-Up #6 (connection-string promotion to configuration) will
/// eventually replace the default implementation with an
/// <c>IConfiguration</c>-backed read so deployments can override the
/// vault location via appsettings.json. The interface stays unchanged
/// across that swap.
/// </remarks>
public interface IVaultPathProvider
{
    /// <summary>
    /// Absolute path to the vault root directory. The directory is not
    /// guaranteed to exist; callers must create it (the
    /// <see cref="VaultService"/> ensures it exists before any write).
    /// </summary>
    string VaultRoot { get; }
}
