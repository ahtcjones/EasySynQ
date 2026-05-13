# EasySynQ — Session Handoff Notes

Dated handoff notes for picking up work across sessions. Each section is a session boundary. Append new sections at the bottom; do not overwrite earlier ones.

---

## 2026-05-12 — End of Phase 1 Data Layer (Chunks A / B / C)

### Where we are in SPEC §9

**Phase 1 (Foundation).** The Data layer is feature-complete. The remaining Phase 1 components are NOT yet built:

| Phase 1 component (per §9) | Status |
|---|---|
| Auth (login, PBKDF2 hashing, current-user accessor) | **Entities + password fields exist, no service.** ADR for PBKDF2 params is open. |
| Audit log | **Done** — entity, interceptor, repository, integration tests. |
| Signature service | **Entity exists, no service.** |
| Snapshot service | **Entity exists, no service.** SQLite WAL mode not yet enabled. |
| Repository interface (mode-agnostic) | **Done** — generic `IRepository<TEntity, TId>` + concrete user/audit repos + UoW. Local Service Mode swap path designed but not exercised. |
| Shell UI with Pulse drawer placeholders | **Not started.** WPF project exists with the SDK's default `MainWindow.xaml` only. |
| Lock inspector framework | **Data shape exists** (`LockReason` + `LockReasonLink`). UI side not built. |

### Last commit

```
3510102 Data layer chunk C: repository pattern, UnitOfWork, host wiring
```

Full chain on `master` (newest first):

```
3510102 Data layer chunk C: repository pattern, UnitOfWork, host wiring
6d4b67d Remove temp commit-message file inadvertently tracked in chunk B
9b239a3 Data layer chunk B: interceptors, effective-dating filter, ADR 0005
516874c Data layer chunk A: DbContext, configurations, initial migration
dc8839c Foundation domain entities + post-review refinements
4d4f0d7 Domain base types and value objects
b02bf11 Phase 1 scaffold: solution, projects, build config, ADR-0003, Spec Rev 3.1
1559cf7 Pin line endings to LF via .gitattributes
5546cb4 Add HTML5 doctype and head/body wrapper to UI_PROTOTYPE.html
b5bebfd Initialize repository with specification Rev 3, UI prototype, and ADRs 0001-0002
```

### Test count + working tree

**146 tests, all passing.** Working tree clean. Branch: `master`. Nothing pushed to a remote (no remote configured yet).

Breakdown:
- Domain unit tests: 87 (HashFormat, EffectiveDateRange, SignaturePayload, DocumentRevisionRef, AuditLogEntry, UserRole, LockReason, Signature, Snapshot)
- Data integration tests: 59 (Schema, BasicCrud, SoftDeleteFilter, OwnedCollection, ConcurrencyToken, IdentityForeignKey, StandardFields, AuditLogWrite, AuditLogBypassResistance, EffectiveDatingFilter, CorrelationId, GenericRepository, UserRepository, AuditLogRepository, UnitOfWork)

### Open / in-flight / deferred

1. **Auth ADR (working number 0006) is open.** SPEC §3.4 says "PBKDF2 or Argon2id"; the User entity's docs reference "ADR 0004" for encoding + iteration count, but slot 0004 ended up being the FK policy ADR. When the auth service is built, write the auth ADR (suggest **ADR 0006 — Auth Specifics**) covering: PBKDF2 vs Argon2id (PBKDF2 wins on no-extra-dependency grounds — see ADR 0001), iteration count (600,000+ is the OWASP 2024 floor for SHA-256; production should pin a specific number with annual review), salt size (16 bytes is standard), encoding (base64 for both salt and hash in `User.PasswordHash` / `User.PasswordSalt`), and password-change vs admin-reset flows.

2. **Packaging ADR (working number 0007) is open.** ADR 0001 deferred "MSIX vs ClickOnce — ADR 0004, before Phase 11." Slot 0004 was reused; packaging needs its own ADR before Phase 11 (Hardening).

3. **ADR 0002 Required Tests (b) — partially satisfied.** ADR 0002 requires raw-SQL bypass attempts to be "either rejected by the interceptor or produce a compensating audit entry." Current state: `AuditLogBypassResistanceTests` *documents* the bypass exists (raw-SQL UPDATE succeeds, no audit entry written) but no enforcement layer rejects raw-SQL writes. Two paths forward:
   - Build a Roslyn analyzer that flags `_context.AuditLogEntries.{Add,Remove,Update}` outside the audit interceptor file (and broader `DbContext.Database.ExecuteSql*` calls on compliance-critical tables). This is the "defense layer 3" from the Chunk B report.
   - OR write a thin wrapper repository surface that forbids raw-SQL writes for compliance-critical tables.
   - The current line of defense is code review + the CLAUDE.md / ADR 0002 policy. That's acceptable for v1 but should harden before any external contributors.

4. **Hardcoded test password iteration count = 600,000.** Tests construct `User` instances with iteration count 600_000 hard-coded. Once the auth ADR is written and the auth service is built, the production iteration count will be a constant in the auth service; tests should reference it rather than hard-coding.

5. **No `git remote` configured.** All 10 commits are local-only. When a remote is added (likely a private GitHub repo before sharing), nothing should require a force-push.

6. **UI shell not built.** `EasySynQ.UI` has the SDK's stock `MainWindow.xaml` from `dotnet new wpf` and nothing else. The prototype at `docs/UI_PROTOTYPE.html` is the visual reference (CLAUDE.md and SPEC §4 specify match-the-prototype discipline). Folders are scaffolded (`Shell/`, `Views/`, `ViewModels/`, `Login/`, `Printing/`, etc.) but empty.

7. **Print stylesheets (SPEC §4.5) — required but no infrastructure.** Every detail view needs a print stylesheet that strips chrome, inlines the audit trail, and renders cleanly on US Letter. No detail views exist yet, so this is a "build when the first view lands" item.

8. **SQLite WAL mode not yet enabled.** SPEC §2 mandates WAL mode for concurrent reads. The Data layer doesn't issue `PRAGMA journal_mode=WAL` anywhere. The right place is either the host's `AddEasySynQDataServices` connection-string handling or an `IHostedService` that runs on startup. For tests this doesn't matter (temp file, single connection) but production deployment needs it.

9. **Customer Portal Export (SPEC §5.5) — Phase 5+ concern.** Mentioned in the prototype's Export modal, but no infrastructure planned for Phase 1.

10. **Future entity that becomes `IEffectiveDated`** — the filter expression must be added inline in `EasySynQDbContext.OnModelCreating` next to the existing `UserRole` filter, referencing `this.AsOfUtc`. The static-helper pattern is explicitly forbidden by ADR 0005; do not try to extract this.

### Next intended chunk

**No specific prompt drafted yet for the next session.** Natural Phase 1 continuations, in dependency order:

1. **Auth service** — login flow, PBKDF2 hashing/verification, real `ICurrentUserAccessor` implementation, session management. The signature and snapshot services downstream both want `ICurrentUserAccessor.UserId` populated, so auth comes first. Lands ADR 0006.
2. **Signature service** — sign-payload + verify-hash, builds on top of the Signature entity. Likely small.
3. **Snapshot service** — tier scheduler, ZIP + SHA-256 generation, integrity verification job. Includes SQLite WAL mode enable on startup. May want to land alongside the host wiring.
4. **Shell UI** — WPF MainWindow + topbar + nav tree + Pulse drawer placeholder + Lock Inspector control. This is the largest unbuilt chunk; match prototype strictly. Will likely span multiple sub-chunks.
5. **Login window** — depends on auth service. Sub-chunk of UI work.

Suggested ordering: 1 → 2 → 3 → 4 (with login window threaded into 4).

### Subtle context the next session will benefit from

These are things I learned this session that aren't in CLAUDE.md, the SPEC, or any ADR yet. If any of them recurs, they should graduate to an ADR or a CLAUDE.md update.

**EF Core 10 behavior changes that bit us:**

- `DbSet.FindAsync(pk)` **applies query filters** in EF Core 10. Earlier EF versions documented FindAsync as bypassing filters. We assumed bypass, then `GetByIdIncludingDeletedAsync` failed its test. Fix in `Repository.cs`: explicit `IgnoreQueryFilters().FirstOrDefaultAsync(e => EF.Property<TId>(e, keyName)!.Equals(id), ct)`.
- `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` **does not translate** in EF Core 10's SQLite provider, even though CA1862 (the analyzer the build runs at error severity) explicitly recommends it. There is a conflict between the analyzer and the provider. Resolution: `EF.Functions.Collate(col, "NOCASE") == value` in `UserRepository.FindByUsernameAsync`. Documented inline. Watch for the same pattern in future repos that need case-insensitive comparisons.
- Query-filter expressions that capture external variables (parameters to a static helper, fields on another class) **get snapshotted at model build time** — EF Core's expression visitor flattens them to constants. Only member access on the DbContext (`this.AsOfUtc`) is parameterized per query. ADR 0005 captures this. The cost: every `IEffectiveDated` entity's filter must be inlined in `OnModelCreating` referencing a `this.SomeProperty`. The static configurator pattern is forbidden.
- `--idempotent` is not supported for SQLite by EF Core's migration scripter. Plain `dotnet ef migrations script` works, but the output doesn't gate on `__EFMigrationsHistory` presence. Acceptable; we ship migrations through `Database.Migrate()` at runtime, not via scripted DDL.

**Architectural decisions made under pressure:**

- **Services → Data was inverted to Data → Services.** The scaffold's default direction (Services depends on Data) would have created a circular dependency with Chunk B's interceptors consuming Services abstractions. Documented in ADR 0003's "Dependency Direction" subsection. Future application services that legitimately need a DbContext compose at the host level (e.g., a WPF command handler that injects both `IUserRepository` from Services and the actual DbContext-based logic from a host-level orchestrator).
- **Hard-delete signaling = `EntityState.Deleted`**, not a marker interface or a context-level set. AuditSaveChangesInterceptor reads the state and emits `AuditAction.HardDelete` with `After = null`. The user originally suggested "an entity in Deleted state that does NOT inherit AuditableEntity" — that was self-contradictory and replaced with the cleaner idiom.
- **Tests are async test methods named `_Async` per IDE1006.** The naming rule applies uniformly; sync tests omit the suffix. CA1707 (underscores) and CA1806 (unused constructor result) are suppressed for the Tests project — these are tests-only conventions and the suppression is in `EasySynQ.Tests.csproj`. CA1861 (static-readonly arrays) is suppressed in `**/Migrations/*.cs` because that's EF-generated code.

**Test infrastructure conventions:**

- **Two integration-test base classes.** `IntegrationTestBase` (no interceptors, no test doubles) for tests that only validate schema/mapping/CRUD. `InterceptorIntegrationTestBase` (full DI with mutable test doubles) for tests that exercise interceptors, audit, or the effective-dating filter. The repository tests inherit from `InterceptorIntegrationTestBase` because they need audit entries to fire on SoftDelete / HardDelete / Add.
- **Mutable test doubles** in `TestHelpers/TestDoubles.cs`: `FixedClock`, `MutableCurrentUserAccessor`, `MutableAuditCorrelationProvider`, `MutableTemporalResolver`. All four have public settable properties. Pattern: set the values, do the work, observe via the same instance.
- **`Ct` property on the base classes** = `TestContext.Current.CancellationToken`. xUnit v3's `xUnit1051` analyzer requires async methods accepting CancellationToken to receive one; passing `Ct` through every EF call satisfies it.
- **Per-test temp SQLite file** in `%TEMP%`. xUnit instantiates a new test class per test method, so each test gets a fresh DB. Slower than in-memory but matches deployment topology (ADR 0003 explicitly rejects the in-memory provider — "its semantics differ from real SQLite enough to be misleading").

**Tooling / repo housekeeping:**

- `dotnet-tools.json` is at the **repo root**, not in `.config/`. .NET 10 SDK's `dotnet new tool-manifest` template changed the default location. Both locations are valid lookup paths. Don't move it.
- `git-msg.tmp` and `git-msg-*.tmp` are gitignored. Use them for long commit messages via `git commit -F`. The pattern lives at the bottom of `.gitignore`.
- **No AI attribution in repo content** — standing rule, stored as `feedback_no_ai_attribution.md` in the auto-memory directory. No `Co-Authored-By` trailers, no comments mentioning AI tooling. `CLAUDE.md` is the only sanctioned location for that fact.

**ADR numbering note:**

ADR 0001 (Stack Choice) originally projected future ADRs as `0002 — Document Vault content-addressed`, `0003 — Effective dating`, etc. — those were illustrative, not a schedule. Actual ADR numbering as committed:

- 0001 — Stack Choice
- 0002 — Hard-Delete Audit-Log Behavior
- 0003 — ORM Choice: EF Core (with Dependency Direction subsection added in Chunk B)
- 0004 — Foreign Key Policy
- 0005 — Effective-Dating Filter Mechanism

Auth ADR and Packaging ADR are the next open slots (0006, 0007).

---

## 2026-05-12 (follow-up) — CLAUDE.md hygiene pass

After the data-layer handoff entry above, one additional commit landed: a `CLAUDE.md` audit + sync pass. No code, services, tests, or specs changed.

### What changed

Commit `99f2491 docs: CLAUDE.md hygiene — sync stale references, add standing rules` updated `CLAUDE.md` in seven spots:

- "Full specification, Revision 2" → revision number dropped (SPEC.md's own Revision History table is now the source).
- Bootstrap reading list: "ADR 0001" → "every ADR in `docs/decisions/`" + most recent `SESSION_NOTES.md` entry.
- Non-Negotiable Rule #10 added: no AI attribution anywhere in repo content. This was previously only in auto-memory (`feedback_no_ai_attribution.md`), which doesn't load for anyone reading the repo cold.
- "What I always want with new code" gained a bullet: services consume `IRepository<,>` / `IUnitOfWork` rather than injecting `EasySynQDbContext` directly (ADR 0002's first defense layer against audit bypass).
- Repository Layout: `EasySynQ.Tests` description corrected — `xUnit + Moq + FluentAssertions` → `xUnit v3 + Moq + AwesomeAssertions` (FluentAssertions was swapped in SPEC Rev 3.1 for license reasons).
- Repository Layout: `EasySynQ.sln` → `EasySynQ.slnx` (matches the .NET 10 XML solution format actually in use).
- Repository Layout: `EasySynQ.Services` description now mentions the cross-cutting abstractions it hosts (IClock, ICurrentUserAccessor, IAuditCorrelationProvider, ITemporalResolver) and points at ADR 0003 for the Data → Services direction.

### Why this matters for the next session

The previous handoff entry above flagged several conventions ("**No AI attribution in repo content**" at line 117, the Services-consume-repositories pattern, the corrected ADR numbering at line 121) as "not in CLAUDE.md yet". They are in CLAUDE.md now. The handoff note's text is left as-written for historical accuracy, but anyone reading CLAUDE.md cold for the next session will find those rules without needing to also load the handoff note.

### Working tree state

```
99f2491 docs: CLAUDE.md hygiene — sync stale references, add standing rules
e40dd50 Session handoff note: end of data layer, ready for services
3510102 Data layer chunk C: repository pattern, UnitOfWork, host wiring
```

Working tree clean. Branch: `master`. Still no remote configured. Test count unchanged at 146; no code touched.

### Forward plan

Unchanged from the entry above. Next chunk in dependency order is still **Auth service → Signature service → Snapshot service → Shell UI** with the login window threaded into the Shell UI work. ADR 0006 (Auth Specifics) is the next ADR to write.

---

## 2026-05-12 (follow-up 2) — Test stability fix; metrics-drift correction

### Correction to the prior handoff metrics

The first 2026-05-12 entry above asserted "**146 tests, all passing**" as the suite's state. That number was based on a single `dotnet test` invocation and did not reflect actual stability under default parallel execution. When the next session ran the suite to confirm the handoff, it caught one failing test (`CorrelationIdTests.ExplicitCorrelationId_OverridesPerSaveDefaultAsync`). Investigation showed the failure was intermittent and not specific to that test.

**Measured flake rate before the fix (30 full-suite runs at default parallelism):** 23/30 passed → **77% run-level pass rate**, with **6 different tests** observed failing at least once. Every failure shared the same root exception:

```
System.ObjectDisposedException : Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
```

The failing test in any given run was always a bystander — the bug was upstream, in the test scaffolding.

### Root cause

`TempSqliteDb.Delete()` called `SqliteConnection.ClearAllPools()`, which is **process-wide**. xUnit v3 parallelizes test classes within an assembly by default. When test A in class X was mid-`SaveChangesAsync` holding a pooled connection and test B in class Y finished, B's teardown call to `ClearAllPools()` tore down A's connection from under it. A then threw `ObjectDisposedException` on the next sqlite3 access. The flake was timing-dependent on which pair of tests happened to overlap.

The original `ClearAllPools()` was added to fix a Windows file-lock race on `File.Delete()` after pool return. The fix worked for that symptom but introduced this race.

### Fix

Targeted pool flush keyed on this database's connection string:

```csharp
using (var conn = new SqliteConnection($"Data Source={path}"))
{
    SqliteConnection.ClearPool(conn);
}
```

`ClearPool(connection)` is scoped to the supplied connection's pool — other tests' pools are untouched. Preserves the original Windows file-lock-release intent without the cross-test interference.

### Verification

30 full-suite runs at default parallelism after the fix:

```
Total runs:                  30
Runs that fully passed:      30
Runs with any failure:       0
Run-level pass rate:         100%
```

The script lives at `scripts/stress-test.ps1` and is the canonical regression check for this class of bug going forward.

### Process change

The handoff template in `CLAUDE.md` (Working Style → "Test stability verification") now requires:

> Test stability is measured across runs, not a single observation. When reporting test results in session-handoff notes or PRs, run the suite at least 5 times and report the run-level pass rate, not just the final run's count. For changes touching test infrastructure (fixtures, base classes, parallelism, isolation), run the dedicated stress test (`scripts/stress-test.ps1`) and report the result.

This entry is the first observance of that rule. Future handoffs should not report a bare test count without a pass-rate observation behind it.

### Working tree

Single commit landed everything: the `TempSqliteDb` fix, `scripts/stress-test.ps1`, the `.gitignore` update for the transient output file, the `CLAUDE.md` rule, and this entry. Branch: `master`. No remote configured.

### Forward plan

Unchanged. Next chunk is still **Auth service → Signature service → Snapshot service → Shell UI**, with **ADR 0006 (Auth Specifics)** as the next ADR.

---

## Phase 1 Follow-Ups

Tracked here so design decisions surfaced mid-implementation do not get lost between chunks. These items are *not* in-scope for the chunk where they were first surfaced; the chunk proceeded with a documented gap so we could ship the immediate work without back-pressuring it on the unresolved decision.

- Sign-in audit coverage is incomplete: AuthenticationService writes implicit audit rows via User mutations only on success and failed-known-user paths. Unknown-user, AccountLocked, AccountDisabled, and FirstRunBootstrap outcomes produce zero audit trail. Resolve before Phase 1 close — likely via a new ADR extending AuditAction with sign-in event values and relaxing AuditLogEntry's Before/After shape requirement for event-class rows. Tracked here to avoid losing it.

- Login screen "last successful sign-in" footer hint is deferred. Requires either a PreviousLoginUtc field on User or a DateTime? PreviousLoginUtc on AuthenticationResult.Success. Decide before Phase 1 close.

- Navigation events are not audited. The shell's NavigateToAsync produces no audit-log row when the user changes the active module or detail view. Same root cause as the sign-in audit gap: the current audit pipeline emits one row per entity Insert/Update/Delete/HardDelete via AuditSaveChangesInterceptor, and navigation is a UI event with no entity mutation. Resolve under the same ADR that addresses sign-in audit — extending AuditAction with event-class values and relaxing AuditLogEntry's Before/After shape requirement — so the audit shape decision is made once for both surfaces.

---

## 2026-05-13 — Chunk E1 + E2 landed

Phase 1's user-facing surfaces are now in the repo. Two commits on master:

- `6f460d0` Chunk E1: WPF theme resources and login surface
- (latest) Chunk E2: WPF shell — navigation, Pulse drawer, content host

Test count: 225 across the suite, 5/5 stability.

What's done:
- Theme tokens (Colors / Typography / Spacing / BaseStyles) merged into App.xaml.
- LoginWindow + LoginViewModel translating IAuthenticationService's five-arm result; integration test pins ADR 0006 lockout-window behavior end-to-end.
- Shell: MainWindow with topbar (brand "EQ", module chip, breadcrumb, Pulse button, user chip), nav tree (13 entries grouped under five §4.1 sections), content area with placeholder views.
- Pulse drawer with IPulseSource contract (consumer-side, single async GetTilesAsync), MockPulseSource hardcoding prototype content, slide-in via RenderTransform.
- NavigationContentFactory as the single seam future module views replace into; today branches "pulse.dashboard" vs everything-else.
- IDirtyStateAware protocol in place, exercised end-to-end by unit tests, no production consumer yet (first will be Phase 2 Documents editing).
- IWritableCurrentUserAccessor + WpfCurrentUserAccessor (impl ready, registration deferred to E5).
- New converters: NullToVisibility, ReferenceEqualityMultiConverter. New brushes: BadgeRedBg / BadgeAmberBg. New button variant: ButtonGhost.

What's NOT done (Chunk E still open):
- **E3 — Lock Inspector control** (SPEC §4.3 "Why is this locked?"). Reusable WPF user control with the three lock states (NCR HELD, OOS asset, superseded document) and a demo view. Not started.
- **E5 — Host wiring.** App.xaml.cs currently constructs the dependency graph by hand with three seams (MockPulseSource, two NullLoggers) concentrated in OnStartup. E5 replaces this with Microsoft.Extensions.Hosting DI container, EF Core migration check on startup, global unhandled-exception handler, and the LoginWindow→MainWindow flow. Removes the three seams in one place.

E4 was originally scoped as "placeholder views" — landed inside E2.4. No separate E4 sub-chunk remains.

Phase 1 Follow-Ups (already in the file above, just enumerating for the next contributor):
1. Sign-in audit coverage incomplete (unknown-user / locked / disabled / bootstrap branches produce no audit row)
2. "Last successful sign-in" footer hint deferred (needs PreviousLoginUtc somewhere)
3. Navigation events not audited (same audit-pipeline root cause as #1)
4. Pulse drawer tile tints use inline alpha-bearing hex literals; promote to named tokens if a second surface needs them

Next session entry point: Chunk E3 (Lock Inspector) or Chunk E5 (host wiring). E5 is the more architecturally consequential of the two and unblocks the LoginWindow→MainWindow path the app currently doesn't have. E3 is more contained but doesn't unblock anything else. Recommend E5 next unless there's a reason to prefer E3.

Working tree clean as of this entry.

---

## 2026-05-13 — Chunk E5 landed (E5.1 through E5.4); E5.5 pending

Phase 1's host wiring is substantially complete. Six commits on master
since the previous handoff:

- `27b44a4` fix(ui): PulseDrawerView storyboard cannot reference
  SlideTransform by name from a Style — latent E2.3 bug; see
  "Smoke verification protocol" note below.
- `47040d0` Chunk E5.1: introduce Microsoft.Extensions.Hosting; manual
  DI moved into ConfigureServices.
- `23802da` Chunk E5.2: Serilog wired as ILogger<T> provider with file
  and debug sinks. Logs land in %LOCALAPPDATA%\EasySynQ\logs\.
- `b6a1486` Chunk E5.3: global unhandled-exception handlers
  (DispatcherUnhandledException, AppDomain.UnhandledException,
  TaskScheduler.UnobservedTaskException).
- `5109564` Chunk E5.4a: data services + cross-cutting abstractions
  registered (IClock, ITemporalResolver, IAuditCorrelationProvider,
  AddEasySynQDataServices, LoginViewModel, LoginWindow). No
  entry-point change in this commit.
- `b0586c6` Chunk E5.4b: LoginWindow as entry point; writable
  current-user accessor populated from auth result.

Test count: 225/225 across the suite, 5/5 stability at every commit.

What's done in Chunk E5:

- App opens LoginWindow first; on successful authentication, the
  current-user accessor is populated and MainWindow is resolved
  (lazy) and shown; LoginWindow closes. Failed auth keeps the user
  on LoginWindow with the E1 five-arm error display, now visible
  end-to-end through the real Serilog file.
- Full dependency graph resolves at host build time: DbContext,
  audit + temporal interceptors, repositories, UnitOfWork,
  IAuthenticationService, ISignatureService, the entire data layer.
- Real ILogger<T> emits land in the file sink with millisecond
  timestamps, timezone offset, and {SourceContext} attribution.
- Unhandled exceptions on the dispatcher thread surface to the
  user via MessageBox; non-dispatcher exceptions log via static
  Log.Fatal + CloseAndFlush as death-rattle; unobserved task
  exceptions are observed and logged.
- New EventIds in use: 1001 (LoginViewModel sign-in failure, E1),
  2001 (MainShellViewModel nav cancellation, E2), 3001
  (PulseDrawerViewModel refresh, E2), 5001/5002 (App dispatcher +
  unobserved task handlers, E5.3), 6001 (App sign-in success,
  E5.4). The 4xxx range is intentionally unused — reserve for
  future shell-level events.
- UiAuditCorrelationProvider (new, src/EasySynQ.UI/Audit/) returns
  null unconditionally; AuditSaveChangesInterceptor's per-save
  fallback handles correlation generation. Shape chosen as
  Phase 1's correct granularity; replacement path to AsyncLocal
  documented in the type's XML.

What's NOT done — E5.5 still open:

- **E5.5 — EF Core migration check on startup.** The production DB
  file at %LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db is empty
  on first run; auth currently fails with "no such table: Users"
  because no migration has been applied. E5.5 applies pending
  migrations on startup (after host build, before LoginWindow
  shows). Failed migration must log via the now-real Serilog
  pipeline and surface via the now-wired global handler, then
  prevent app launch with a user-visible failure dialog.
  Reuses GetDatabasePath() helper introduced in E5.4a — single
  source of truth on the DB path.

Latent E1/E2 bugs surfaced by E5 smoke (worth keeping in mind for
future sessions):

- **PulseDrawerView storyboard.** Style-scope Storyboard used
  TargetName="SlideTransform" — illegal in WPF; threw
  InvalidOperationException on first drawer toggle. Fixed in
  `27b44a4`. Originated in E2.3.
- **LoginWindow DialogResult-setting handlers.** Original
  OnLoginSucceeded and OnBootstrapRequired both set DialogResult
  before Close(); the setter throws under non-modal Show().
  Removed in E5.4b. Originated in E1.

**Smoke verification protocol — note for future sessions.** Both
bugs above were missed by previous sessions' "smoke verified"
claims because the smoke didn't drive real-host code paths. Tests
pass, integration tests pin VM-level logic, but Window-code-behind
paths only fire under production hosting. Going forward: smoke
verification must drive every reachable user gesture end-to-end
under the real host, not just confirm that windows render. "Window
opens cleanly" is necessary but not sufficient.

Phase 1 Follow-Ups (running list — five existing carry over from
the previous handoff, six newly accumulated during E5):

1. (existing) Sign-in audit coverage incomplete. Unknown-user /
   locked / disabled / bootstrap branches produce no audit row.
   E5.4b's new LoginSucceeded hook in App.xaml.cs is the natural
   place to write the success-path audit row when this is
   addressed.
2. (existing) "Last successful sign-in" footer hint deferred —
   needs PreviousLoginUtc plumbing.
3. (existing) Navigation events not audited (same audit-pipeline
   root cause as #1).
4. (existing) Pulse drawer tile tints use inline alpha-bearing hex
   literals; promote to named tokens if a second surface needs them.
5. (newly tracked, role plumbing) Plumb authenticated user's
   effective role through AuthenticationResult and
   AuthenticatedUserEventArgs. Today E5.4b passes the literal
   string "Authenticated User" as the role to SetCurrentUser —
   placeholder, explicit and obviously-not-real to make a future
   grep find it. ADR 0006 amendment required to define
   role-resolution semantics (single role / primary / selected-at-
   login).
6. (newly tracked, configuration) Connection string is hard-coded
   in App.xaml.cs's GetDatabasePath() helper to
   %LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db. Promote to
   configuration (appsettings.json or in-app Settings flow);
   requires Microsoft.Extensions.Configuration.Json dependency
   decision.
7. (newly tracked, correlation) Replace UiAuditCorrelationProvider's
   permanent-null implementation with an AsyncLocal-backed scope
   holder when the first multi-save logical operation arrives
   (Phase 2 Document Controller is the expected first consumer).
   Interface unchanged; concrete type gains
   BeginScope(Guid) → IDisposable that command handlers wrap
   around their multi-save operations.
8. (newly tracked, package) EasySynQ.Services'
   Serilog.Sinks.File 7.0.0 reference is currently inert —
   pre-staging for snapshot/audit-flush work that hasn't
   shipped. Do NOT prune as "unused" before the consuming code
   lands.
9. (newly tracked, retention) Serilog file sink has no
   retainedFileCountLimit — log files accumulate indefinitely
   (one per day). Production deployment needs a retention
   setting; ~30–60 days is the conventional default.
10. (newly tracked, log noise) Each user-facing auth failure
    currently produces three [ERR] lines (two EF Core diagnostic,
    one VM). Once E5.5 lands migrations the happy-path noise
    self-resolves; if dev-time noise persists, tighten
    MinimumLevel.Override to specifically target
    "Microsoft.EntityFrameworkCore" at Warning.

Next session entry point: Chunk E5.5 (EF Core migration check on
startup). After E5.5 lands, Chunk E5 is closed and Phase 1's host
wiring is complete; Phase 2 work (Document Controller is the
likely starting point per SPEC §9) becomes unblocked.

Working tree clean as of this entry (after the docs handoff commit
this entry is part of).

---
