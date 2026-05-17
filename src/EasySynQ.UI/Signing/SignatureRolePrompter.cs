using System.Windows;
using System.Windows.Interop;

using EasySynQ.Services.Authorization;

namespace EasySynQ.UI.Signing;

/// <summary>
/// Production <see cref="ISignatureRolePrompter"/>. Composes
/// <see cref="IRoleResolutionService"/> (the pure filtering helper)
/// with <see cref="SignAsRoleDialog"/> (the WPF picker). Returns the
/// chosen role; throws <see cref="OperationCanceledException"/> on
/// dialog cancel.
/// </summary>
/// <remarks>
/// <para>
/// The dialog is shown via <c>ShowDialog</c> on the WPF UI thread.
/// Per ADR 0009 §"Manual smoke (C4)", no production code path invokes
/// this prompter in C4 itself — the actual signing flows that wire
/// it land in C6 with the Document detail view.
/// </para>
/// </remarks>
public sealed class SignatureRolePrompter : ISignatureRolePrompter
{
    private readonly IRoleResolutionService _roleResolution;

    /// <summary>Constructs the prompter over the resolution service.</summary>
    public SignatureRolePrompter(IRoleResolutionService roleResolution)
    {
        ArgumentNullException.ThrowIfNull(roleResolution);
        _roleResolution = roleResolution;
    }

    /// <inheritdoc />
    public Task<string> ResolveSigningRoleAsync(
        string permissionName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);
        cancellationToken.ThrowIfCancellationRequested();

        var eligible = _roleResolution.GetEligibleRolesForPermission(permissionName);

        if (eligible.Count == 0)
        {
            // Defensive — the lifecycle-service permission check
            // (Permissions.Contains) should fire first for any user
            // who lacks the permission entirely. Reaching here means
            // the user's only path to the permission is a direct
            // UserPermission grant (Phase 2 has no operational use).
            throw new InvalidOperationException(
                $"Cannot resolve signing role for permission '{permissionName}': " +
                "the current user holds no role that grants this permission. " +
                "If this fires in production, the user's path to the permission " +
                "is via a direct UserPermission grant — a corner case ADR 0009 " +
                "explicitly does not handle.");
        }

        if (eligible.Count == 1)
        {
            // Single-role auto-return — no dialog. Matches the
            // pre-C4 behavior for users with exactly one role.
            return Task.FromResult(eligible[0]);
        }

        // Multi-role case — show the picker. Owned by the
        // application's main window when one is available; otherwise
        // ownerless (the picker still functions, just without an
        // owner-window relationship — Phase 1 / Phase 2 have no
        // currently-running flow that would invoke this without
        // MainWindow already being active, so the ownerless fallback
        // is a defensive default).
        var dialog = new SignAsRoleDialog(eligible);
        // PresentationSource guard — Application.MainWindow can be a
        // closed or never-shown Window after the sign-in flow; Owner
        // assignment on such a Window throws.
        if (Application.Current?.MainWindow is { } owner
            && !ReferenceEquals(owner, dialog)
            && PresentationSource.FromVisual(owner) is HwndSource)
        {
            dialog.Owner = owner;
        }

        var ok = dialog.ShowDialog();

        if (ok != true || dialog.ViewModel.SelectedRole is null)
        {
            throw new OperationCanceledException(
                "Signing-role selection was cancelled by the user.",
                cancellationToken);
        }

        return Task.FromResult(dialog.ViewModel.SelectedRole);
    }
}
