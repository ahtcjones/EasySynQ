using EasySynQ.Services.Abstractions;

namespace EasySynQ.Services.Authorization;

/// <summary>
/// Production <see cref="IRoleResolutionService"/>. Filters the current
/// user's role-permission snapshot (ADR 0009) by the supplied
/// permission name. No I/O; pure in-memory enumeration.
/// </summary>
public sealed class RoleResolutionService : IRoleResolutionService
{
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Constructs the service over the current-user accessor.</summary>
    public RoleResolutionService(ICurrentUserAccessor currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetEligibleRolesForPermission(string permissionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);

        // Sorted ordinal so the dialog's radio-button order is stable
        // across sessions (alphabetical) regardless of the dictionary's
        // iteration order. Per ADR 0009 §"What ships in C4" the dialog
        // is forward-looking; deterministic ordering matters when C6
        // wires the dialog into actual user-facing flows.
        var matches = _currentUser.RolePermissions
            .Where(kv => kv.Value.Contains(permissionName))
            .Select(kv => kv.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return matches;
    }
}
