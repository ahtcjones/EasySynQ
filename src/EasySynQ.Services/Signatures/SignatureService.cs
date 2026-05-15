using System.Security.Cryptography;
using System.Text;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;

namespace EasySynQ.Services.Signatures;

/// <summary>
/// Production <see cref="ISignatureService"/>. Computes SHA-256 of the
/// caller-supplied canonical payload and persists a <see cref="Signature"/>
/// row capturing user, role-at-time-of-sign, timestamp, and hash.
/// </summary>
public sealed class SignatureService : ISignatureService
{
    private readonly IRepository<Signature, Guid> _signatures;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Constructs the service over its dependencies.</summary>
    public SignatureService(
        IRepository<Signature, Guid> signatures,
        ICurrentUserAccessor currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _signatures = signatures;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Signature> SignAsync(
        string signedEntityType,
        string signedEntityId,
        string canonicalPayload,
        string signingAsRole,
        CancellationToken cancellationToken)
    {
        // Single-action wrapper: stage + save. Multi-entity callers
        // (lifecycle transitions per ADR 0008 C3) call
        // StageSignatureAsync directly so the signature row commits in
        // the same SaveChanges as the surrounding state changes.
        var signature = await StageSignatureAsync(
            signedEntityType, signedEntityId, canonicalPayload, signingAsRole, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return signature;
    }

    /// <inheritdoc />
    public async Task<Signature> StageSignatureAsync(
        string signedEntityType,
        string signedEntityId,
        string canonicalPayload,
        string signingAsRole,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAsRole);

        if (_currentUser.UserId is null)
        {
            throw new InvalidOperationException(
                "Cannot sign anonymously: ICurrentUserAccessor.UserId is null. " +
                "Authenticate before calling SignAsync.");
        }

        // ADR 0009 — role is supplied by the caller (the UI signing-
        // flow prompter resolved it for multi-role users; single-role
        // users passed their only role literally). Validate that the
        // user actually holds the role they claim to be signing as —
        // a defensive check against UI programming errors that would
        // produce an audit row with a fraudulent RoleAtTimeOfSign.
        if (!_currentUser.Roles.Contains(signingAsRole))
        {
            throw new InvalidOperationException(
                $"Cannot sign: current user does not hold role '{signingAsRole}'. " +
                "The role passed to SignAsync must be a member of " +
                "ICurrentUserAccessor.Roles. This catches UI programming " +
                "errors where the prompter or call site passes a role the " +
                "user is not authorized to sign as.");
        }

        var payloadHash = ComputePayloadHash(canonicalPayload);

        var signature = new Signature(
            id: Guid.NewGuid(),
            utcTimestamp: _clock.UtcNow,
            roleAtTimeOfSign: signingAsRole,
            signedEntityType: signedEntityType,
            signedEntityId: signedEntityId,
            payloadHash: payloadHash);

        await _signatures.AddAsync(signature, cancellationToken);
        return signature;
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(
        Signature signature,
        string canonicalPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPayload);

        var recomputed = ComputePayloadHash(canonicalPayload);
        // FixedTimeEquals on the bytes that produced the hex strings.
        // The hex strings themselves are equal-length and could be
        // ordinal-compared safely (no byte-divergence-leak risk because
        // a hash mismatch is not a secret), but using FixedTimeEquals
        // matches the discipline applied to the password verify path
        // and removes any room for "well, actually" debate.
        var storedBytes = Convert.FromHexString(signature.PayloadHash);
        var recomputedBytes = Convert.FromHexString(recomputed);
        var matches = CryptographicOperations.FixedTimeEquals(storedBytes, recomputedBytes);
        return Task.FromResult(matches);
    }

    private static string ComputePayloadHash(string canonicalPayload)
    {
        // Signature.PayloadHash is required to be exactly 64 lowercase
        // hex characters (validated in the Signature constructor via
        // HashFormat.IsValidSha256Hex). Convert.ToHexStringLower is the
        // BCL idiom that emits exactly that shape.
        var bytes = Encoding.UTF8.GetBytes(canonicalPayload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
