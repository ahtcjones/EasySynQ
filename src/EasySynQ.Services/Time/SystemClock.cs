namespace EasySynQ.Services.Time;

/// <summary>
/// Production implementation of <see cref="IClock"/> that reads
/// <see cref="DateTime.UtcNow"/> directly. Register as a singleton in DI.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
