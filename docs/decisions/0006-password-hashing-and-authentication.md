# ADR 0006 — Password Hashing and Authentication

**Status:** Accepted
**Date:** 2026-05-12
**Supersedes:** None
**Resolves:** ADR 0001's deferred "authentication implementation specifics" item.

---

## Context

SPEC §3.4 requires identity-based authentication backed by salted, hashed local passwords ("PBKDF2 or Argon2id") with no third-party identity provider (ADR 0001). ADR 0001 explicitly deferred algorithm choice, parameters, encoding, salt size, lockout policy, and password rules to a follow-up ADR before Phase 1 ships. This is that ADR.

The deployment target is a small heat-treat shop with no IT staff. The auth surface ships as part of the WPF binary — there is no separate admin tool, no OS-level identity store, no central configuration server. Every choice has to survive that constraint: nothing that adds an out-of-band secret, nothing that forces a NuGet dependency we can avoid, nothing that requires an administrator to choose tuning parameters they do not understand.

The User entity already carries `PasswordHash`, `PasswordSalt`, `PasswordIterationCount`, and `MustChangePassword` (ADR 0001 / Phase 1 domain entities). What this ADR adds: the algorithm and parameters that compute those values, the lockout state and policy, the bootstrap behavior on a fresh install, and the password requirement.

## Decision

### Algorithm: PBKDF2 with HMAC-SHA-256

`Rfc2898DeriveBytes` from the .NET BCL with `HashAlgorithmName.SHA256`. No third-party dependency.

- **Iteration count: 600,000** (OWASP 2023 recommendation for PBKDF2-SHA256). Stored per-user as `User.PasswordIterationCount`.
- **Salt: 16 bytes** from `RandomNumberGenerator.GetBytes(16)`. Stored as base64 in `User.PasswordSalt`.
- **Hash output: 32 bytes** (full SHA-256 output). Stored as base64 in `User.PasswordHash`.
- **No pepper.** A pepper is a process-wide secret that hashes alongside the per-user salt and would need to be loaded from outside the database — environment variable, secret store, OS keyring. The product ships with none of those surfaces, and adding one would introduce a "lose the pepper, lose every password" ops failure mode that this deployment cannot recover from. Per-user salt is sufficient when the database file is the realistic attack surface anyway: an attacker with the file already has every other compliance record in plaintext.

**Per-user iteration count is the load-bearing detail of this design.** When OWASP raises the recommendation in a future year, the policy's `CurrentIterationCount` increases. Existing users' hashes are still verifiable against their stored count. On their next successful login, `Verify` returns `SuccessRequiresRehash`, the auth service computes a new hash at the new count, and updates the user atomically with the rest of the login state. The user notices nothing; no flag day, no forced password reset.

### Constant-time comparison

Verification compares the recomputed hash bytes to the stored hash bytes via `CryptographicOperations.FixedTimeEquals`. `==` on byte arrays would short-circuit on the first byte mismatch and leak hash structure through timing. `FixedTimeEquals` is BCL-provided and is the correct choice for any cryptographic comparison.

### Lockout: 5 failures in 15 minutes → 15-minute lockout

State stored on `User`:

- `int FailedLoginCount` — consecutive failed login attempts, reset to 0 on successful login.
- `DateTime? LockedUntilUtc` — UTC instant the lockout expires, or `null` when the account is not locked.

Policy values exposed via `IPasswordPolicy`:

- `MaxFailedAttempts = 5`
- `LockoutDuration = TimeSpan.FromMinutes(15)`

Observation window: the counter resets on success. There is no separate "rolling 15-minute window" calculation. After 5 *consecutive* failures the lockout is applied; after `LockoutDuration` elapses the next attempt is evaluated normally (with the counter still at 5; one more failure re-locks immediately, one success resets it to 0).

A locked account returns `AccountLocked(LockedUntilUtc)` from `AuthenticateAsync` regardless of whether the supplied password is correct. This prevents using the failure response as an oracle for password correctness during a lockout.

### First-run bootstrap: first login provisions the Administrator

On a fresh install there are no users. The login screen detects "no users exist," prompts for desired username + password + display name, and on submit creates a single Administrator account with `MustChangePassword = false` and immediately authenticates the operator. This is the *only* code path that creates a user without an authenticated caller.

This is a deliberate small-shop convenience. The alternatives evaluated and rejected:

- **Seed data in a migration.** Forces a hardcoded password. The shop either keeps it (bad) or has a documentation-and-discipline obligation to change it on first login (also bad — forgettable, brittle, and the migration sits in source control with the seed value visible).
- **Separate setup tool.** Doubles the binary count. The shop's IT exposure is "open the program and use it"; a second program to run before the first program is one too many surfaces.
- **HTTP/CLI provisioning step.** No HTTP, no shell scripts. Out of scope for the deployment.

The bootstrap path is covered by integration tests and gated behind `Users.AnyAsync() == false`. Once any user exists, the bootstrap path returns `InvalidCredentials` (it does not reveal that the bootstrap window has closed, because the surface that calls it should already know).

### Password requirements: minimum 12 characters

That is the entire rule. No required mix of uppercase / lowercase / digit / symbol. No banned-substring list. No "password ≠ username" check (still desirable but trivially circumventable by users motivated to circumvent it).

This follows NIST 800-63B (Digital Identity Guidelines, post-2017): *length matters; complexity rules push users toward predictable patterns*. A 12-character pass-phrase has more entropy than a 10-character "must contain a symbol" string that ends up being `Password1!`. Documenting this explicitly because operators (and quality managers) accustomed to corporate IT password rules will find it surprising.

`MinimumLength = 12` is exposed via `IPasswordPolicy` so the policy can tighten without code changes, but the default is the recommendation.

### Argon2id rejected

Argon2id is the modern PHC-winner password-hashing function and is stronger than PBKDF2 against GPU/ASIC attackers. We rejected it for this product because:

1. **Not in the BCL.** Requires a NuGet package (`Konscious.Security.Cryptography.Argon2` is the most-cited implementation). Per ADR 0001, every dependency is a decision.
2. **PBKDF2-SHA256 at 600k is OWASP-current** for the threat model that fits this product (offline cracking of a stolen DB by a non-state actor with consumer-grade hardware). The marginal security gain from Argon2id is real but does not outweigh the dependency cost for our deployment posture.
3. **The realistic attack surface is the database file**, not online password guessing (the lockout policy handles online). Anyone with the DB file already has the audit log, the document vault index, the customer concession records, and every signed certificate. Faster password cracking is not the most valuable thing they get from that breach.

If a future deployment context shifts the threat model — for example, adopting an enterprise customer with explicit Argon2id requirements — the algorithm choice swap is a one-class change inside `PasswordHasher` plus a migration that sets `User.PasswordHashAlgorithm` (a column we would add then). The per-user iteration-count pattern is the precedent: per-user algorithm storage would extend it. Not building the per-user algorithm column today because YAGNI and SPEC §10 ("Implements the spec without scope creep").

## Alternatives Considered

### bcrypt

- **Why considered:** Well-known; supported in many languages; battle-tested.
- **Why rejected:** Not in the BCL (NuGet dependency), 72-byte input truncation, no native iteration parameter (just a "work factor"), and OWASP's 2023 guidance prefers Argon2id or PBKDF2 over bcrypt for new designs. If we are paying the dependency cost, Argon2id is the better target.

### scrypt

- **Why considered:** Memory-hard; designed to resist GPU/ASIC attacks.
- **Why rejected:** Not in the BCL. Same dependency math as Argon2id, with weaker industry momentum.

### "Just store SHA-256(salt || password)"

- **Why considered:** Simple, in the BCL, fast.
- **Why rejected:** Trivially crackable at modern GPU speeds. Per-user salt prevents rainbow-table attacks but not brute force on a single user's hash. Iteration count is the entire point of using PBKDF2 instead of a single-pass hash.

### Lockout by IP address or session, not by user

- **Why considered:** Avoids the "denial-of-service against a known username" attack where an attacker repeatedly fails logins to lock out a target.
- **Why rejected:** This is a desktop application running on a closed network with named-workstation use. There is no public surface to attack. The DoS-against-a-coworker concern is real but social, not technical — a Quality Manager's response to "I can't log in" is to ask the operator to wait 15 minutes, not to file a bug. If multi-tenant or remote-access deployment ever happens, IP-based throttling becomes a reasonable addition.

### Forced password complexity rules

- **Why considered:** Many corporate-IT environments require them; auditors sometimes ask.
- **Why rejected:** NIST 800-63B explicitly recommends *against* them post-2017; modern guidance is "length over complexity." Documenting the choice in this ADR means an auditor who pushes back has a written, reasoned answer. The minimum-length policy is exposed as configuration so a deployment with non-negotiable corporate rules can raise the floor.

### No lockout (rely on PBKDF2 cost as the only throttle)

- **Why considered:** Simpler. No lockout state to manage.
- **Why rejected:** A 600k-iteration PBKDF2 verify takes ~200ms; that is enough to deter manual guessing but not enough to deter an attacker who can script 5 attempts per second across a couple of minutes for thousands of accounts. Account lockout is the standard online-attack countermeasure and is required for ISO 9001 §7.5.3 (control of documented information) defensibility.

## Consequences

### Positive

- Zero new top-level dependencies. PBKDF2, `RandomNumberGenerator`, `CryptographicOperations.FixedTimeEquals`, and base64 encoding are all in `System.Security.Cryptography`.
- Per-user iteration count means the policy can rise without a flag day. The first user to log in after a policy bump silently re-hashes; everyone else re-hashes as they next log in.
- Lockout state is two columns on `User` — no separate failed-attempts table, no rolling-window calculation, no garbage collection. Fits SQLite well.
- First-run bootstrap matches the deployment story: open the program, set up your first user, start working. No second binary, no documented seed credential.
- Password rule is short enough to fit in a tooltip and defensible in audit. "Minimum 12 characters" is a sentence; "must contain uppercase + lowercase + digit + symbol, may not contain dictionary words..." is a wall of text that drives operators to sticky notes.

### Negative (and accepted)

- **600k PBKDF2 iterations cost ~200ms per verify on a current laptop.** Login feels intentional rather than instant. This is the OWASP recommendation; we accept the latency as the security feature.
- **Lockout can be weaponized internally.** Anyone who knows a username can lock the account by failing five times in a row. Mitigation: in a small-shop deployment, this is a social problem with a named perpetrator; the audit log captures every failed attempt. If it becomes a real concern, a per-source-workstation throttle is a future addition.
- **No password complexity gate.** A user who picks `aaaaaaaaaaaa` (12 a's) passes the rule. We accept this because (a) NIST 800-63B says complexity gates make passwords *worse* on average by predictably patterning them and (b) the deployment audience is small enough that social pressure ("don't be the person whose password is twelve a's") is more effective than a technical gate.
- **First-run bootstrap is a one-shot trust window.** Anyone with physical access during install can claim the Administrator account. We accept this because physical access during install is also "ability to copy the database file"; the bootstrap is not the weakest link.
- **No password history / reuse prevention.** Out of scope for this ADR. Could be added later as a `PasswordHistory` table with the same per-row iteration count semantics.

## Implementation Notes

- `PasswordHasher` is constructed from `IPasswordPolicy` and is otherwise pure (no I/O, no DI graph beyond the policy). Fully unit-testable.
- `AuthenticationService` is the only service that mutates `User` lockout state. Repository writes go through `IUserRepository` and `IUnitOfWork` — no direct `DbContext` injection (per CLAUDE.md / ADR 0002).
- The "are there any users?" check during bootstrap uses `IUserRepository.Query().AnyAsync()` and respects soft-delete (a soft-deleted Administrator does not re-open the bootstrap window — that would be a recovery-tool concern, not a normal flow).
- `AuthenticateAsync` returns one of: `Success(User, requiresPasswordChange)`, `InvalidCredentials`, `AccountLocked(lockedUntilUtc)`, `AccountDisabled`, `FirstRunBootstrap`. The unknown-user and wrong-password paths both return `InvalidCredentials` after computing a comparable PBKDF2 verify against a dummy hash (constant-time enough for our threat model — see below).
- **Username-existence timing leak — pragmatic stance:** strict timing equivalence between "user not found" and "user found, wrong password" is hard to guarantee through an EF Core round-trip. The auth service computes a same-cost dummy verify when the user is not found, which closes the obvious gap. A determined attacker measuring nanosecond differentials over a network is not in this product's threat model (no network surface). Documented for honesty.
- Tests use a weakened policy (`MinimumLength = 4`, `CurrentIterationCount = 1000`) so the suite stays fast. Production iteration count is exercised by a single dedicated `PasswordHasherTests` test against the production policy.

## Required Tests (Phase 1 / Chunk D scope)

- **Hash + verify round-trip** with the production iteration count. Wrong password fails; correct password returns `Success`.
- **Salt uniqueness** — hashing the same password twice produces different salts and therefore different hashes.
- **Constant-time comparison** — verification path uses `CryptographicOperations.FixedTimeEquals` (asserted by inspection of the production code, not by timing).
- **Rehash signal** — when the policy iteration count exceeds the stored count, `Verify` returns `SuccessRequiresRehash`.
- **First-run bootstrap** creates an Administrator and immediately succeeds at the next authenticate.
- **Unknown username** returns `InvalidCredentials` (not "user not found").
- **Five consecutive failures** lock the account; sixth attempt with the right password still returns `AccountLocked`.
- **Successful auth after partial failure** resets the failure counter to 0.
- **Lockout expires** after `LockoutDuration` and the account is re-evaluable.
- **Disabled account** returns `AccountDisabled` regardless of password correctness.
- **Silent rehash** — login with the right password against an under-iterated stored hash updates `PasswordIterationCount` and `PasswordHash` to the current policy in the same `SaveChanges` as the lockout-state reset.
- **ChangePassword** verifies the current password before applying the new one; wrong current password returns `false` and writes nothing.

## References

- `docs/SPEC.md` §2 (Technology Stack), §3.4 (Security & Data Integrity)
- ADR 0001 (deferred auth specifics to this ADR)
- ADR 0002 (audit-log invariant — auth-service writes go through repository / interceptor pipeline like everything else)
- ADR 0003 (ORM choice — auth uses EF Core through `IUserRepository` and `IUnitOfWork`)
- OWASP Password Storage Cheat Sheet (2023) — PBKDF2-SHA256 600,000 iterations recommendation
- NIST SP 800-63B Section 5.1.1.2 (memorized secret verifiers) — length over complexity
