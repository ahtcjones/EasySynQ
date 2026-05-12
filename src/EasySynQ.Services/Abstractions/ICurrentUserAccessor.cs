namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Exposes the identity of the user currently performing an operation, for
/// audit attribution. Returns <see langword="null"/>/empty when no user is
/// authenticated — for example, during the login flow itself, or for
/// system-initiated operations such as scheduled snapshot creation and
/// integrity verification.
/// </summary>
/// <remarks>
/// Consumed by the data layer's <c>StandardFieldsInterceptor</c> (to stamp
/// <c>CreatedBy</c> / <c>ModifiedBy</c>) and <c>AuditSaveChangesInterceptor</c>
/// (to populate <c>AuditLogEntry.UserId</c>). Implementations typically scope
/// to the current request, command, or UI operation.
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Identifier of the current user, or <see langword="null"/> when no
    /// user is authenticated.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Display name for the current user, or empty string when no user is
    /// authenticated.
    /// </summary>
    string UserDisplayName { get; }
}
