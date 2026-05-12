namespace EasySynQ.Services.Time;

/// <summary>
/// Abstraction over the system clock. Every domain operation that needs
/// "what time is it now?" reads from <see cref="UtcNow"/> rather than calling
/// <see cref="DateTime.UtcNow"/> directly, so tests can substitute a fixed
/// clock and verify time-dependent behavior deterministically.
/// </summary>
/// <remarks>
/// The contract is strict: <see cref="UtcNow"/> always returns a
/// <see cref="DateTime"/> whose <see cref="DateTime.Kind"/> is
/// <see cref="DateTimeKind.Utc"/>. Implementations that read local time
/// must convert before returning.
/// </remarks>
public interface IClock
{
    /// <summary>Current UTC instant. <see cref="DateTime.Kind"/> is always
    /// <see cref="DateTimeKind.Utc"/>.</summary>
    DateTime UtcNow { get; }
}
