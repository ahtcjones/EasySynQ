using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Production <see cref="IAuthenticationService"/> implementing the
/// behaviors specified by ADR 0006: PBKDF2-SHA256 verification with
/// silent rehash on policy upgrade, lockout after a configurable number
/// of consecutive failures, first-run detection (returns
/// <see cref="AuthenticationResult.FirstRunBootstrap"/> when no users
/// exist; creation of the first user is owned by
/// <see cref="EasySynQ.Services.Bootstrap.IBootstrapService"/>), and
/// change-password.
/// </summary>
/// <remarks>
/// <para>
/// Username-existence timing leak is masked by computing a same-cost
/// dummy PBKDF2 verify when the supplied username is unknown. The dummy
/// material is cached for the lifetime of this service instance so the
/// first "unknown user" call pays the salt + hash construction cost
/// just once.
/// </para>
/// </remarks>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicy _policy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    private HashedPassword? _dummyHash;

    /// <summary>Constructs the service over its dependencies.</summary>
    public AuthenticationService(
        IUserRepository users,
        IPasswordHasher hasher,
        IPasswordPolicy policy,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);
        _users = users;
        _hasher = hasher;
        _policy = policy;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // First-run check: no users at all → ask the surface to invoke
        // the bootstrap flow.
        var anyUser = await _users.AnyAsync(cancellationToken);
        if (!anyUser)
        {
            return new AuthenticationResult.FirstRunBootstrap();
        }

        var user = await _users.FindByUsernameAsync(username.Trim(), cancellationToken);
        if (user is null)
        {
            // Username unknown. Spend the same compute as a normal
            // verify so the response time does not leak existence.
            ConsumeDummyVerifyCost(password);
            return new AuthenticationResult.InvalidCredentials();
        }

        if (user.IsDisabled)
        {
            // Disabled status takes precedence over lockout/credentials —
            // the disabled-account message is a different surface for
            // the UI ("contact your administrator") and the disabled
            // signal is fine to expose; the account is administratively
            // closed, not under attack.
            return new AuthenticationResult.AccountDisabled();
        }

        var now = _clock.UtcNow;

        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            // Currently locked. Return the lockout regardless of whether
            // the supplied password is correct — otherwise the response
            // becomes a "is my password correct?" oracle during lockout.
            return new AuthenticationResult.AccountLocked(lockedUntil);
        }

        var verification = _hasher.Verify(
            password,
            user.PasswordHash,
            user.PasswordSalt,
            user.PasswordIterationCount);

        if (verification == PasswordVerificationResult.Failure)
        {
            user.RegisterFailedLogin();
            if (user.FailedLoginCount >= _policy.MaxFailedAttempts)
            {
                user.ApplyLockout(now + _policy.LockoutDuration);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new AuthenticationResult.InvalidCredentials();
        }

        // Success path — reset lockout state, stamp last login, and
        // (if the stored hash is below the current policy iteration
        // count) silently rehash with the new count. All in one save.
        if (verification == PasswordVerificationResult.SuccessRequiresRehash)
        {
            var rehashed = _hasher.Hash(password);
            user.UpdatePasswordHash(
                rehashed.Hash,
                rehashed.Salt,
                rehashed.IterationCount,
                clearMustChangePassword: false);
        }

        user.RegisterSuccessfulLogin(now);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult.Success(user, user.MustChangePassword);
    }

    /// <inheritdoc />
    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No user with id {userId} exists.");

        var verification = _hasher.Verify(
            currentPassword,
            user.PasswordHash,
            user.PasswordSalt,
            user.PasswordIterationCount);

        if (verification == PasswordVerificationResult.Failure)
        {
            // Wrong current password. Do not write anything; the
            // change-password surface is not a lockout-eligible flow
            // (the user is already authenticated to reach it).
            return false;
        }

        // Hash the new password under the current policy. PasswordHasher
        // throws if newPassword is shorter than the policy minimum —
        // surfaces as ArgumentException to the caller.
        var rehashed = _hasher.Hash(newPassword);
        user.UpdatePasswordHash(
            rehashed.Hash,
            rehashed.Salt,
            rehashed.IterationCount,
            clearMustChangePassword: true);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ConsumeDummyVerifyCost(string suppliedPassword)
    {
        // Lazy initialization: the first unknown-user call pays the
        // dummy-hash construction cost; subsequent calls reuse it. The
        // dummy is hashed under the current policy's iteration count so
        // the verify time matches what a real user would consume.
        if (_dummyHash is null)
        {
            // Use a fixed dummy plaintext that satisfies the minimum
            // length under any reasonable policy. This is not a secret;
            // it exists only so PasswordHasher.Hash succeeds.
            var dummyPlain = new string('x', Math.Max(_policy.MinimumLength, 16));
            _dummyHash = _hasher.Hash(dummyPlain);
        }

        _ = _hasher.Verify(
            suppliedPassword,
            _dummyHash.Hash,
            _dummyHash.Salt,
            _dummyHash.IterationCount);
    }
}
