namespace EasySynQ.UI.Signing;

/// <summary>
/// Resolves which role the current user is signing as for an action
/// gated by a permission (ADR 0009 C4). The signing-flow caller invokes
/// this once per signing operation and forwards the returned role to
/// the lifecycle service / signature service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "prompter" not "resolver."</b> The defining behavior of this
/// type is prompting the user for input — it shows a modal Window in
/// the multi-role case. A "resolver" name would imply pure computation;
/// the type is impure (UI-coupled). The single-eligible-role auto-
/// return path is the convenience case, not the defining one.
/// </para>
/// <para>
/// <b>Behavior summary.</b>
/// <list type="bullet">
///   <item>Zero eligible roles → <see cref="InvalidOperationException"/>
///   naming the permission. Defensive — the lifecycle-service
///   permission check should fire first for any user who can't
///   sign at all; reaching the prompter with zero eligible roles
///   implies the user's only path to the permission is via a direct
///   <c>UserPermission</c> grant (Phase 2 has no operational use).</item>
///   <item>Exactly one eligible role → returns that role
///   automatically; no dialog appears.</item>
///   <item>Two or more eligible roles → shows
///   <see cref="SignAsRoleDialog"/>; returns the user's pick on OK;
///   throws <see cref="OperationCanceledException"/> on Cancel or
///   window close.</item>
/// </list>
/// </para>
/// </remarks>
public interface ISignatureRolePrompter
{
    /// <summary>
    /// Resolves the signing role for an action gated by
    /// <paramref name="permissionName"/>.
    /// </summary>
    /// <param name="permissionName">Canonical permission name gating
    /// the action being signed (e.g.,
    /// <see cref="EasySynQ.Domain.PermissionNames.DocumentReview"/>).
    /// Must not be null/empty/whitespace.</param>
    /// <param name="cancellationToken">Cancellation token. Cancellation
    /// surfaces as <see cref="OperationCanceledException"/>; the
    /// dialog's Cancel button raises the same exception so callers
    /// can handle both paths uniformly.</param>
    /// <returns>The role the user is signing as. Always a member of
    /// the current user's effective roles.</returns>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="permissionName"/> is null, empty, or
    /// whitespace.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no role the current user holds grants the supplied permission
    /// (defensive — see remarks).</exception>
    /// <exception cref="System.OperationCanceledException">Thrown
    /// when the user cancels the picker dialog or
    /// <paramref name="cancellationToken"/> fires.</exception>
    Task<string> ResolveSigningRoleAsync(
        string permissionName,
        CancellationToken cancellationToken);
}
