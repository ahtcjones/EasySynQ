using EasySynQ.Domain.Entities.Audit;

namespace EasySynQ.Services.Signatures;

/// <summary>
/// Computes and verifies digital signatures per SPEC §3.4 and the
/// <see cref="Signature"/> entity. A signature binds a user identity, a
/// role-at-time-of-sign snapshot, a UTC timestamp, and a SHA-256 hash of
/// a canonical payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>Canonical payload format is the caller's responsibility.</b> The
/// signature service hashes a string and stores the result; it does not
/// know what's inside the string. Callers are expected to produce a
/// stable byte-for-byte canonical serialization of the signed payload
/// (deterministic JSON, fixed field ordering, no trailing whitespace,
/// etc.) so that <see cref="VerifyAsync"/> can rehash the same bytes
/// later and arrive at the same hash. If the canonical form drifts
/// between sign and verify, the signature will not validate even though
/// the underlying record is unchanged — that is the correct behavior
/// (the auditor's question is "did this exact thing get signed?").
/// </para>
/// </remarks>
public interface ISignatureService
{
    /// <summary>
    /// Persists a new <see cref="Signature"/> binding the current user,
    /// the current role, and the supplied payload.
    /// </summary>
    /// <param name="signedEntityType">Canonical type name of the entity
    /// being signed (for example, <c>"DocumentRevision"</c>,
    /// <c>"CoC"</c>). Must not be null/empty/whitespace.</param>
    /// <param name="signedEntityId">Canonical string form of the signed
    /// entity's identifier. Must not be null/empty/whitespace.</param>
    /// <param name="canonicalPayload">Stable canonical serialization of
    /// the payload to bind. Must not be null/empty/whitespace.</param>
    /// <param name="signingAsRole">Role the user is signing as (ADR
    /// 0009). Must be a member of the current user's
    /// <see cref="EasySynQ.Services.Abstractions.ICurrentUserAccessor.Roles"/>.
    /// For single-role users the caller passes their only role
    /// literally; for multi-role users the role is resolved by the UI
    /// signing-flow prompter before invoking this method.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="Signature"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when any string input
    /// is null/empty/whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no
    /// authenticated user is available
    /// (<see cref="EasySynQ.Services.Abstractions.ICurrentUserAccessor.UserId"/>
    /// is <see langword="null"/>) — anonymous signatures are
    /// meaningless and disallowed — or when
    /// <paramref name="signingAsRole"/> is not a member of the
    /// current user's effective roles.</exception>
    Task<Signature> SignAsync(
        string signedEntityType,
        string signedEntityId,
        string canonicalPayload,
        string signingAsRole,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same shape as <see cref="SignAsync"/> but does NOT call
    /// <c>SaveChanges</c> on the unit of work — the new
    /// <see cref="Signature"/> row is added to the change tracker and
    /// returned to the caller, leaving the caller responsible for the
    /// final save. Used by services that need the signature staged in
    /// the same transaction as other entity changes (lifecycle
    /// transitions, multi-entity updates) so the entire operation
    /// commits or rolls back atomically (ADR 0008 C3 plan §A3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SignAsync"/> is now a thin wrapper around this method
    /// plus a SaveChanges; existing single-action callers and their
    /// tests are unaffected.
    /// </para>
    /// </remarks>
    /// <param name="signedEntityType">Canonical type name of the entity
    /// being signed. Must not be null/empty/whitespace.</param>
    /// <param name="signedEntityId">Canonical string form of the signed
    /// entity's identifier. Must not be null/empty/whitespace.</param>
    /// <param name="canonicalPayload">Stable canonical serialization of
    /// the payload to bind. Must not be null/empty/whitespace.</param>
    /// <param name="signingAsRole">Role the user is signing as (ADR
    /// 0009). Must be a member of the current user's effective roles.
    /// See <see cref="SignAsync"/> for the full role-resolution
    /// contract.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The staged (but not yet persisted)
    /// <see cref="Signature"/>. Its <c>Id</c> is fully populated; the
    /// row is committed when the caller's next <c>SaveChanges</c> runs.</returns>
    /// <exception cref="ArgumentException">Thrown when any string input
    /// is null/empty/whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no
    /// authenticated user is available, or when
    /// <paramref name="signingAsRole"/> is not a member of the
    /// current user's effective roles.</exception>
    Task<Signature> StageSignatureAsync(
        string signedEntityType,
        string signedEntityId,
        string canonicalPayload,
        string signingAsRole,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes the SHA-256 of <paramref name="canonicalPayload"/> and
    /// compares it (constant-time) to <see cref="Signature.PayloadHash"/>.
    /// </summary>
    /// <param name="signature">The stored signature to verify against.</param>
    /// <param name="canonicalPayload">The current canonical
    /// serialization of the payload.</param>
    /// <param name="cancellationToken">Cancellation token (unused today;
    /// reserved for future async I/O if cross-process verify becomes
    /// real).</param>
    /// <returns><see langword="true"/> when the payload's SHA-256
    /// matches <see cref="Signature.PayloadHash"/>; otherwise
    /// <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="signature"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="canonicalPayload"/> is
    /// null/empty/whitespace.</exception>
    Task<bool> VerifyAsync(
        Signature signature,
        string canonicalPayload,
        CancellationToken cancellationToken);
}
