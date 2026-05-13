namespace EasySynQ.Services.Identity;

/// <summary>
/// Outcome of <see cref="IPasswordHasher.Verify(string, string, string, int)"/>.
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>Verification succeeded; the stored hash is current.</summary>
    Success = 0,

    /// <summary>
    /// Verification succeeded, but the stored hash was computed under an
    /// iteration count below the current policy's
    /// <see cref="IPasswordPolicy.CurrentIterationCount"/>. The caller
    /// should re-hash the supplied password under the current policy and
    /// update the user's stored material in the same transaction as any
    /// other login-state writes.
    /// </summary>
    SuccessRequiresRehash = 1,

    /// <summary>Verification failed; supplied password does not match.</summary>
    Failure = 2,
}
