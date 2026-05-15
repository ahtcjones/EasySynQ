namespace EasySynQ.Services.Authorization;

/// <summary>
/// Thrown when an operation is attempted by a user who lacks the
/// required permission. Per ADR 0007's permission-based authorization
/// model, services that gate behind a permission throw this when the
/// current user's <see cref="EasySynQ.Services.Abstractions.ICurrentUserAccessor.Permissions"/>
/// snapshot does not include the required permission name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Distinct from <see cref="InvalidOperationException"/>.</b>
/// Services that throw <c>InvalidOperationException</c> are signalling
/// "this operation is not valid in the current state" (no authenticated
/// user, malformed input, state machine violation). This exception
/// specifically means "the operation is valid, but the caller is not
/// authorized to perform it" — a different remediation
/// (administrator grants the permission) than for state violations.
/// </para>
/// <para>
/// <see cref="PermissionName"/> carries the name of the missing
/// permission so callers can render an actionable message
/// (<c>"You need 'Vault.PhysicalDelete' to do that"</c>) rather than
/// a generic refusal. The property is nullable for parameterless
/// construction; callers that throw with the typed-message constructor
/// always populate it.
/// </para>
/// </remarks>
public sealed class UnauthorizedOperationException : Exception
{
    /// <summary>
    /// Canonical name of the permission the operation required, or
    /// <see langword="null"/> when the exception was constructed
    /// without one.
    /// </summary>
    public string? PermissionName { get; }

    /// <summary>Constructs a permission-context-free instance.</summary>
    public UnauthorizedOperationException()
    {
    }

    /// <summary>Constructs an instance with a custom message.</summary>
    public UnauthorizedOperationException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs an instance with a custom message and inner exception.</summary>
    public UnauthorizedOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Constructs an instance carrying the missing permission's
    /// canonical name. Message is auto-generated:
    /// <c>"The current user does not have the required permission '{PermissionName}'."</c>
    /// </summary>
    /// <param name="permissionName">Canonical permission name (e.g.,
    /// <c>"Vault.PhysicalDelete"</c>). Must not be null/empty/whitespace.</param>
    /// <returns>A new <see cref="UnauthorizedOperationException"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="permissionName"/> is null, empty, or whitespace.</exception>
    public static UnauthorizedOperationException ForMissingPermission(string permissionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);
        return new UnauthorizedOperationException(
            $"The current user does not have the required permission '{permissionName}'.",
            permissionName);
    }

    private UnauthorizedOperationException(string message, string permissionName)
        : base(message)
    {
        PermissionName = permissionName;
    }
}
