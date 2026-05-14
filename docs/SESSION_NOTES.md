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

## 2026-05-13 — Chunk E5.5 and Chunk F1 landed

Phase 1's auth-success loop is reachable end-to-end for the first
time. Three commits on master since the previous handoff:

- `9cab16f` feat(host): EF Core migration check on startup (E5.5)
- `76a7c55` feat(services): IBootstrapService for first-run
  administrator creation; remove bootstrap from
  IAuthenticationService (F1 part 1 of 2)
- `2c7dc46` feat(ui): BootstrapWindow + BootstrapViewModel; App
  detects and branches on bootstrap-required state (F1 part 2 of 2)

Test count progression: 225 → 230 (F1 Commit 1) → 237 (F1 Commit 2).
5/5 stability at every commit.

What's done in Chunk E5.5:

- App.xaml.cs's new `ApplyPendingMigrations` runs between the
  global exception handlers and the login flow. Sync
  (millisecond-scale at Phase 1 size), via a service scope +
  `Database.Migrate()`. On success with pending migrations, logs
  EventId 6002 Information with the list of migrations applied; on
  no pending, no log line (steady-state silence — decision (d)).
  On failure, logs Critical (EventId 6003), shows
  "EasySynQ — Cannot start" dialog, calls `Current.Shutdown(1)`.
- First-run launches now create the schema. The prod DB file at
  `%LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db` was empty
  pre-E5.5; auth threw `SqliteException: no such table: Users` on
  every sign-in attempt. Post-E5.5, first launch creates the
  seven-table schema + two columns from `AddUserLockoutState`,
  then `AnyAsync()` returns false → `FirstRunBootstrap`. That path
  was caught by the placeholder dialog in LoginViewModel for the
  first time during E5.5 smoke — and was then deleted in F1
  Commit 2 once App owned the bootstrap branch directly.

What's done in Chunk F1 (split across two commits):

- `IBootstrapService` with two methods:
  `IsBootstrapRequiredAsync` (wraps `!await _users.AnyAsync`) and
  `CreateAdministratorAsync(username, password, displayName, ct)`
  (creates `User` + `Administrator` `Role` + open-ended `UserRole`
  atomically in one `IUnitOfWork.SaveChangesAsync`). The previous
  `CreateBootstrapAdministratorAsync` on `IAuthenticationService`
  (which created the User only, by explicit "identity not
  authorization" scope) was moved + extended into
  `BootstrapService`; the auth service surface no longer mentions
  bootstrap. Path 1 resolution from the F1 pre-work design call —
  the prior scope left the first user role-less, which would have
  failed every Phase 2 authorization check.
- `App.xaml.cs IsBootstrapRequired(IHost)` detects the empty-Users
  state at startup (between migration check and login flow),
  branches to `ConfigureBootstrapFlow` OR `ConfigureLoginFlow`
  accordingly. No try/catch — bootstrap-detection failure surfaces
  via the global E5.3 handler (correct shape; can't proceed with
  broken data layer).
- `BootstrapWindow` + `BootstrapViewModel` collect Username,
  DisplayName, Password, Confirm Password. DisplayName pre-fill
  latch tracks Username until first manual edit; resets when both
  fields are emptied (both clear orderings handled symmetrically
  in `OnUsernameChanged` and `OnDisplayNameChanged`). Password
  policy validation deferred to `IPasswordHasher.Hash` —
  `ArgumentException` translates to a clean user-facing error,
  no log emit.
- On successful bootstrap, `OnBootstrapSucceeded` mirrors
  `OnLoginSucceeded`: SetCurrentUser → log EventId 6004 →
  `MainWindow.Show` → `BootstrapWindow.Close`. Auto-sign-in works
  end-to-end — the just-created admin lands in MainWindow without
  re-typing credentials.
- Idempotency-guard event path: if `CreateAdministratorAsync`
  throws `InvalidOperationException` (a user appeared between
  detection and create — vanishingly unlikely on a single-process
  desktop app), VM raises `IdempotencyGuardFired`, App handler
  logs EventId 6005 Warning + shows MessageBox "Setup is no longer
  required. Please restart the application to sign in." +
  `Application.Current.Shutdown(0)`. Next launch routes to
  LoginWindow.
- Audit attribution under null current user verified end-to-end:
  bootstrap writes **4 audit rows** (Role + User + UserRole +
  EffectiveDateRange owned-type), all `UserId = null`, all sharing
  one `CorrelationId`. The 4-row shape (rather than the
  pre-work-assumed 3) was the first exercise of the owned-type
  audit pathway — see Follow-Up #11 below.

New EventIds in use as of this session:

- App-tier 6xxx range continues: 6002/6003 (migrations applied /
  failed), 6004 (bootstrap-succeeded App handler), 6005
  (idempotency-guard warning). 6001 `LogSignInSucceeded` was
  wired in E5.4b but became reachable for the first time during
  F1 smoke Step B.
- **Service-tier 7xxx range introduced**: 7001
  `LogBootstrapCompleted` in `BootstrapService`. First Services
  project consumer of `[LoggerMessage]`.
- VM-tier 1xxx range continues: 1002 `LogBootstrapSystemError` in
  BootstrapViewModel (alongside 1001
  `LoginViewModel.LogSignInSystemError`).

Latent bugs surfaced during F1 smoke (worth keeping in mind for
future sessions):

- **BootstrapViewModel DisplayName latch reset required two
  iterations to land correctly.** First iteration only reset the
  `_displayNameManuallyEdited` flag in `OnDisplayNameChanged`;
  smoke discovered that clearing Username LAST (DisplayName already
  empty) left the latch flipped. The reset condition is a property
  of state (both fields empty), not of which handler fires. Second
  iteration mirrored the check into `OnUsernameChanged` (ordered
  before the pre-fill guard so the just-reset latch is observed on
  the same call). The original VM test exercised only one
  clear-order; a new test was added (Case A — DisplayName cleared
  first, Username last) that pins the regression closed.

Smoke verification protocol — VINDICATED THREE TIMES this session:

- The protocol from the E5 handoff — "smoke verification must
  drive every reachable user gesture end-to-end under the real
  host, not just confirm windows render" — caught real issues at
  every chunk this session:
  1. **E5.5 smoke** surfaced the placeholder bootstrap dialog
     firing (expected, harmless — the first time the path had been
     reachable since data layer landed). Established the
     post-E5.5 state for F1 design.
  2. **F1 Commit 1 pre-work** surfaced the existing
     `CreateBootstrapAdministratorAsync` on IAuthenticationService —
     a design-time discovery that changed the F1 commit shape from
     "build new service" to "move + extend existing method." Saved
     a follow-up commit.
  3. **F1 Commit 2 smoke** caught the DisplayName latch
     order-dependent gap (above). Tests at the time of first-fix
     submission passed 236/236 because they only exercised one
     clear order.
- Establishing this as project practice for all future sessions.
  Every chunk that touches user-facing surfaces should plan smoke
  verification as part of the commit, not as an afterthought. Tests
  pin behavior; smoke discovers behaviors worth pinning.

Phase 1 Follow-Ups (running list — ten carry over from the previous
handoff, two newly accumulated this session, totaling 12):

1. (existing) Sign-in audit coverage incomplete. Unknown-user /
   locked / disabled / bootstrap branches produce no audit row.
2. (existing) "Last successful sign-in" footer hint deferred.
3. (existing) Navigation events not audited.
4. (existing) Pulse drawer tile tints — inline alpha-bearing hex
   literals; promote to named tokens if a second surface needs them.
5. (existing, now ACTIONABLE) Plumb authenticated user's effective
   role through `AuthenticationResult.Success`,
   `AuthenticatedUserEventArgs`, and
   `BootstrapSucceededEventArgs`. The Administrator role + UserRole
   assignment now exist as of F1 Commit 1, so role-resolution is
   no longer blocked on "what role does the user have." Both
   `OnLoginSucceeded` and `OnBootstrapSucceeded` currently pass
   the literal `"Authenticated User"` placeholder string — grep for
   it to find both call sites. ADR 0006 amendment still needed to
   define semantics (single role / primary / selected-at-login).
6. (existing) Connection-string promotion to configuration
   (appsettings.json or in-app Settings flow).
7. (existing) AsyncLocal correlation-scope holder for multi-save
   logical operations (replace UiAuditCorrelationProvider's
   permanent-null implementation when Phase 2 Document Controller
   arrives).
8. (existing) `EasySynQ.Services` `Serilog.Sinks.File 7.0.0` inert
   dependency — DO NOT prune as "unused" before the consuming code
   lands.
9. (existing) Serilog file sink has no `retainedFileCountLimit` —
   production deployment needs a retention setting (~30–60 days).
10. (existing) EF Core log noise — tighten
    `MinimumLevel.Override("Microsoft.EntityFrameworkCore", Warning)`
    if dev noise persists post-E5.5.
11. **(NEW, F1 Commit 1) Audit-row shape for entities with owned
    types.** EF Core 10 tracks owned types as separate
    `EntityEntry` instances; `AuditSaveChangesInterceptor` walks
    `ChangeTracker.Entries()` and currently emits one audit row per
    entry, including the owned-type entry. `UserRole.EffectivePeriod`
    drove the discovery during F1 (the bootstrap UserRole insert
    produces a 4th audit row attributed to `"EffectiveDateRange"`).
    The owned-type row is currently the ONLY audit surface for the
    `effective_*` values — the owner's `PropertyValues.Properties`
    does not enumerate the flattened columns. Three resolution
    shapes considered (status quo / suppress + enrich /
    mixed-by-cardinality); decision deferred to its own ADR, with
    pre-work needed on audit-log consumer expectations before
    picking. Until that ADR lands, the status-quo shape is what F1
    ships with — pinned by
    `BootstrapServiceTests.CreateAdministratorAsync_WritesThreeAuditRows_*`
    (the test name predates the discovery; the assertion is 4 rows
    including EffectiveDateRange).
12. **(NEW, this handoff) EventId not rendered in Serilog text
    template.** The file sink outputs `{SourceContext}` but not
    `{EventId}`, so when reading the log it's not visible which
    specific `[LoggerMessage]` declaration fired — only the source
    class. Adding `{Properties}` or specifically `{EventId}` to the
    `outputTemplate` in `App.xaml.cs:ConfigureSerilog` would
    surface it. Small fix; defer until the next time someone is in
    Serilog config for another reason, or take a focused commit if
    grep-by-EventId becomes useful for debugging.

Next session entry point — three viable options:

1. **Role plumbing (Follow-Up #5).** Now actionable as a side
   effect of F1 Commit 1. Replace the `"Authenticated User"`
   placeholder in both `SetCurrentUser` call sites with the user's
   actual effective role. Requires ADR 0006 amendment defining
   semantics (single role / primary / selected-at-login). Medium
   scope — touches `AuthenticationResult.Success`,
   `BootstrapSucceededEventArgs`, `AuthenticatedUserEventArgs`, a
   new role-lookup method (likely on `IUserRepository` or a new
   `IUserRoleRepository`), the two App handlers, and tests.
2. **Follow-up grooming pass.** Knock out the smaller items —
   Serilog retention (#9), EventId in template (#12), connection
   string config (#6). Each is contained; bundled commit feasible
   if scoped together.
3. **Phase 2 Document Controller** per SPEC §9. Largest scope —
   first feature module after the foundation. Would start fresh on
   a real domain entity (Document, DocumentRevision) with the full
   machinery (signature, lockout, content-addressed vault, detail
   views, print stylesheet). Best home for the next 3–5 chunks.

Working tree clean as of this entry's commit.

---

## 2026-05-14 — ADR 0007 (Permission-Based Authorization Model) landed

Phase 1's authorization model has been rebuilt from title-based
role membership to permission-based authorization with admin-defined
role bundles. Four commits on master since the previous handoff
(this docs commit is the fourth):

- `339a216` feat(data): Permission entities, link tables, seeded
  catalog, and resolution repositories (ADR 0007 commit 1 of 4)
- `df9db9b` feat(services): ICurrentUserAccessor permission shape;
  BootstrapResult; auth + signature wiring; every caller synced
  (ADR 0007 commit 2 of 4)
- `04de843` feat(ui): MainShellViewModel pass-through; SPEC §3.4
  amended; ADR 0007 Accepted (ADR 0007 commit 3 of 4)
- (this commit) docs: session-handoff notes for ADR 0007

ADR 0007 ships in `docs/decisions/0007-permission-based-authorization-model.md`;
its status flipped from Proposed (2026-05-13) to Accepted (2026-05-14)
when the SPEC amendment landed in commit 3. SPEC §3.4 Authorization
is now the permission-based form described by the ADR (catalog +
role bundles + effective-dated per-user grants). Spec revision
bumped 3.2 → 3.3.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Pre-ADR-0007 (post-F1) | 237 | — | baseline |
| Post-C1 (data layer) | 276 | +39 | entity unit tests, migration-seed test, PermissionRepository + UserRoleRepository integration |
| Post-C2 (services) | 286 | +10 | WpfCurrentUserAccessor rewrite, auth + bootstrap snapshots, signature single-role constraint, ViewModel pass-through assertions |
| Post-C3 (UI + SPEC) | 291 | +5 | MainShellViewModel pass-through |

5/5 stress at 100% run-level pass rate at every commit stop point.

### EventId allocations

No new IDs landed in this chain. Three existing IDs gained
structured `{Roles}` and `{Permissions}` payload properties:

- 6001 `LogSignInSucceeded` (App-tier, sign-in)
- 6004 `LogBootstrapSucceeded` (App-tier, bootstrap success)
- 7001 `LogBootstrapCompleted` (Services-tier, BootstrapService)

The EventId table doesn't grow — the emit payloads on these three
do. Log-analysis tooling can now group sessions by role / permission
sets, which is more useful than the previous "just the username"
shape.

### Smoke verification protocol — sharper after C3

The C3 smoke had a pre-smoke false start worth pinning explicitly:
Step A was *intended* to drive the BootstrapWindow flow against an
empty Users table, but the leftover DB from the F1 smoke wasn't
moved aside before launch, so App's bootstrap-detection returned
false and routed to LoginWindow instead. The user reported "Step A
passed" based on the topbar values they observed — which were the
correct values for the LoginWindow flow, just not for the
bootstrap path the smoke was supposed to exercise. The PowerShell
log inspection caught the gap (EventId 6004 absent, `Permissions: []`
on the signed-in legacy admin) and the smoke was re-run with the
actual Move-Item step before C3 committed.

The lesson: smoke gestures must verify against the *intended*
state, not whatever happens to be on disk. The Move-Item DB-aside
step is load-bearing for bootstrap-path verification, not optional
setup. Verify state-on-disk before claiming a flow was exercised.
Adding to the project's standing smoke protocol.

### EF Core 10 owned-type gotcha worth pinning

Each entity that owns a value-typed property (`OwnsOne` in EF
configuration) must construct a **fresh instance** of the owned
type, even when the value is identical across owners. Sharing one
`EffectiveDateRange` instance across multiple `RolePermission`
rows during the bootstrap loop caused EF Core's change-tracker to
write NULLs for the second owner's flattened columns — the save
failed with `"NOT NULL constraint failed:
RolePermissions.EffectiveFromUtc"` the first time the bootstrap
test ran. Documented inline at the BootstrapService loop body;
surfaces anywhere multiple entities batch-create with identical
owned-type values. Each owned EntityEntry expects to belong to
exactly one owner; reuse violates that invariant.

### Phase 1 Follow-Ups (running list, grooming pass after ADR 0007)

**Resolved in this chain:**

- ~~#5 Plumb authenticated user's effective role through
  `AuthenticationResult.Success` / `AuthenticatedUserEventArgs` /
  `BootstrapSucceededEventArgs`~~ — superseded by ADR 0007. The
  `"Authenticated User"` placeholder string is gone; role plumbing
  carries both `Roles` and `Permissions` collections end-to-end.
  Removed from the open list.

**Newly added:**

- **"Sign-as-which-role" UX ADR.** `SignatureService` currently
  throws `InvalidOperationException` when the current user's
  `Roles.Count != 1`. Phase 1 Administrator is single-role so all
  current paths succeed; the throw fires the first time a
  multi-role user attempts to sign. The first Phase 2 feature
  that consumes `ISignatureService` is the natural trigger for
  the design ADR — likely a sign-as-role picker in the signature
  dialog, with the picked role captured into
  `Signature.RoleAtTimeOfSign`. Defer until that feature is
  concrete.

- **Upgrade-path migration gap.** `AddPermissionsAndLinkTables`
  seeds the eleven `Permission` catalog rows but does NOT write
  `RolePermission` link rows for an Administrator role that
  pre-existed the migration (created by an F1-era bootstrap).
  Pre-ADR-0007 installs (the current dev DB is one) upgrade to a
  legacy admin with `Permissions: []` and every authorization
  check would fail. New installs are unaffected because
  post-ADR-0007 `BootstrapService` writes the link rows in its
  transaction. Decision needed: (a) additive data migration that
  detects the legacy Administrator role and writes the eleven
  `RolePermission` rows for it, or (b) defer to a Phase 2
  admin-tool command. Cheap to do as (a); also a natural
  pre-Phase-2 commit so the dev DB has correct permissions
  before Document Controller work starts checking them.

**Still open (carry forward unchanged; numbering retained from
previous handoff so prior cross-refs don't break — #5 is gone but
the open-list shape is otherwise intact):**

1. Sign-in audit coverage incomplete. Unknown-user / locked /
   disabled / bootstrap branches produce no audit row.
2. "Last successful sign-in" footer hint deferred.
3. Navigation events not audited.
4. Pulse drawer tile tints — inline alpha-bearing hex literals;
   promote to named tokens if a second surface needs them.
6. Connection-string promotion to configuration
   (`appsettings.json` or in-app Settings flow).
7. AsyncLocal correlation-scope holder for multi-save logical
   operations (replace `UiAuditCorrelationProvider`'s
   permanent-null implementation when Phase 2 Document Controller
   arrives).
8. `EasySynQ.Services` `Serilog.Sinks.File 7.0.0` inert dependency
   — DO NOT prune as "unused" before the consuming code lands.
9. Serilog file sink has no `retainedFileCountLimit` — production
   deployment needs a retention setting (~30–60 days).
10. EF Core log noise — tighten
    `MinimumLevel.Override("Microsoft.EntityFrameworkCore", Warning)`
    if dev noise persists.
11. Audit-row shape for entities with owned types. The bootstrap
    audit-row count is now 26 (1 User + 1 Role + 1 UserRole + 1
    EffectiveDateRange [UserRole's] + 11 RolePermission + 11
    EffectiveDateRange [each RolePermission's]). The owned-type
    rows are the only audit surface for the `effective_*` values;
    the open ADR question on whether to suppress + enrich vs.
    keep the current shape is unchanged but considerably more
    weight is now on the answer.
12. EventId not rendered in Serilog text template. Adding
    `{EventId}` to the `outputTemplate` in
    `App.xaml.cs:ConfigureSerilog` would surface it.

### Next-direction options (next session picks)

1. **Upgrade-path migration** (newly tracked Follow-Up above).
   Small, well-scoped, naturally pre-Phase-2. **Light
   recommendation as the immediate next step** since the dev DB
   is currently in the broken state and Phase 2 will start
   writing authorization checks against an admin with empty
   permissions.
2. **Follow-up grooming pass** (#9 retention, #10 EF noise,
   #12 EventId in template, possibly #6 connection-string config).
   One commit, low stakes.
3. **Phase 2 Document Controller** per SPEC §9. New architectural
   territory; largest scope. First feature module after the
   foundation. Would start fresh on a real domain entity
   (`Document`, `DocumentRevision`) with the full machinery
   (signature, lockout, content-addressed vault, detail views,
   print stylesheet). Best home for the next 3–5 chunks.

Working tree clean as of this entry's commit.

---

## 2026-05-14 (follow-up) — Upgrade-path migration closes the ADR 0007 follow-up

Single commit on master since the previous handoff (this docs
commit will be the second):

- `2107ac9` feat(data): LinkLegacyAdministratorToSystemPermissions
  migration closes the ADR 0007 upgrade-path gap

The commit closes the "Upgrade-path migration gap" Phase 1
Follow-Up that the [previous 2026-05-14 entry](#2026-05-14--adr-0007-permission-based-authorization-model-landed)
added to the "NEWLY ADDED" list. The migration writes the eleven
Phase 1 `RolePermission` link rows for an Administrator role that
pre-existed `AddPermissionsAndLinkTables` — exactly the legacy
shape any pre-ADR-0007 install would carry forward.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-ADR-0007 (commit 82455aa) | 291 | — | baseline |
| Post-this-commit | 294 | +3 | `LinkLegacyAdministratorMigrationTests` — legacy-shape / fresh-shape / partial-state |

5/5 stress at 100% run-level pass rate.

### New test infrastructure

`TempSqliteDb.CreateMigratedTo(string targetMigrationName)` joins
the existing `Create()` and `CreateWithInterceptors()` helpers in
`src/EasySynQ.Tests/TestHelpers/TempSqliteDb.cs`. Creates a temp
DB and migrates only up to (and including) the named target via
`IMigrator.Migrate(name)`; returns `(path, options)` like the
existing helpers. Lets tests stage a "pre-X state" data shape,
then apply migration X explicitly via `ctx.Database.Migrate()` on
a freshly-opened context. Mirrors how the production E5.5 startup
path applies pending migrations. Reusable for any future test
that wants to verify a migration's behavior against a controlled
pre-state.

### EF Core 10 migration-pattern note worth pinning

EF Core migrations cannot read DB state during `Up()` — the
method runs at code-generation time, not execution time — so
conditional logic must live in SQL. The TEMP-trigger pattern
used in this migration is the cleanest available shape for
"detect → branch → loud-fail" semantics inside a migration:

1. `CREATE TEMP TABLE __MigrationGuard (placeholder INTEGER);`
2. `CREATE TEMP TRIGGER … BEFORE INSERT ON __MigrationGuard
   BEGIN SELECT RAISE(ABORT, '<diagnostic>'); END;`
3. `INSERT INTO __MigrationGuard SELECT 1 WHERE EXISTS (…)` —
   fires the trigger iff the precondition is violated.
4. Real work (INSERTs, UPDATEs, etc.) — runs only if step 3 did
   not abort.
5. `DROP TABLE __MigrationGuard` — happy-path cleanup.

`RAISE(ABORT, msg)` preserves the message verbatim in the
resulting `SqliteException` (`SqliteErrorCode = 19`), so a
human reading the log sees the diagnostic directly rather than
a generic "constraint failed" string. TEMP table + TEMP trigger
are connection-scoped and auto-cleaned on connection close, so
an aborted `Up()` leaves no schema residue.

Reusable for any future migration needing the same shape.

### Smoke verification — production confirmation of the loud-failure trigger

The migration's smoke verification surfaced an unplanned but
load-bearing production proof of the loud-failure trigger
pattern, end-to-end:

At 18:08:17 the user launched the app BEFORE running the
synthetic-legacy-state `DELETE` step. The dev DB still carried
the eleven `RolePermission` rows from C3's bootstrap. The
migration's precondition guard detected this, the trigger
RAISE(ABORT)'d with the diagnostic message verbatim:

> `LinkLegacyAdministratorToSystemPermissions: precondition violation — the Administrator role already has one or more RolePermission rows. This migration expects zero pre-existing link rows for the Administrator role. A human must investigate before proceeding. See ADR 0007 and the 2026-05-14 entry in docs/SESSION_NOTES.md.`

E5.5's terminal-error path caught the `SqliteException`,
logged EventId 6003 at Critical, surfaced the "EasySynQ —
Cannot start" dialog, and called `Current.Shutdown(1)`. The
full chain — SQL trigger → SqliteException →
`Migrator.Migrate` → E5.5's `ApplyPendingMigrations` catch →
user-facing dialog → exit code 1 — worked exactly as designed
on the first contact with a real failing-state DB.

The design choice (loud-failure trigger over silent SQL no-op)
is now demonstrated working through every layer it touches.
Worth more than a unit test; this is the integration-with-the-
operating-environment proof.

The user then ran the `DELETE`, relaunched at 18:12:05; the
migration applied cleanly; sign-in at 18:14:32 returned
`Roles: ["Administrator"]` and `Permissions: [the eleven]` in
the structured log fields. Post-smoke DB inspection (via
PowerShell + Microsoft.Data.Sqlite) confirmed 11
RolePermission rows, all linked to the Administrator role, all
with `CreatedBy = "system:migration"`.

### Smoke protocol learning to pin

When smoke flows involve a move-aside-and-restore step (the C3
smoke moved the prod DB aside to force the bootstrap path),
**running both halves of the cycle keeps the dev DB in the
state subsequent follow-ups assume**. C3's smoke ran the
move-aside but did not run the corresponding restore, leaving
the dev DB in fresh-post-ADR-0007 state instead of the legacy
pre-ADR-0007 state we had originally been operating against.
This led to a detour during this commit's smoke setup: verify
dev-DB state → discover it's not legacy → reconstruct synthetic
legacy state via `DELETE` before launching.

The detour was contained and resolved within the session, and
arguably produced the bonus production confirmation noted above
(the user's first launch without the `DELETE` was the
real-world trigger fire). But the underlying lesson is
durable: **complete move-aside-and-restore cycles fully when
the smoke result is intended to leave the dev DB in a
specific state**. Adding to the project's standing smoke
protocol alongside the C3 entry's "verify state-on-disk before
claiming a flow was exercised" rule.

### Phase 1 Follow-Ups grooming

**Resolved in this commit:**

- ~~Upgrade-path migration gap~~ (newly added in 82455aa
  handoff) — resolved by 2107ac9. Removed from the open list.

**Still open (carry forward from the 82455aa handoff with no
changes):**

- **"Sign-as-which-role" UX ADR.** `SignatureService` throws
  `InvalidOperationException` on `Roles.Count != 1`. Defer
  until the first Phase 2 feature consuming `ISignatureService`
  is concrete.

- 11 carry-overs from the prior handoff (numbered list,
  unchanged: #1 sign-in audit, #2 PreviousLoginUtc, #3
  navigation audit, #4 pulse tint tokens, #6 connection-string
  config, #7 AsyncLocal correlation scope, #8 inert
  `Serilog.Sinks.File` reference, #9 file sink retention,
  #10 EF Core log-noise tightening, #11 owned-type audit ADR,
  #12 EventId in Serilog template). #5 remains gone
  (superseded by ADR 0007).

### Next-direction options (next session picks)

1. **Phase 1 Follow-Up grooming pass.** Pick off lighter items
   — #9 file sink retention, #10 EF noise tightening, #12
   EventId in template, possibly #6 connection-string config
   if bundled. One commit, low stakes.
2. **Phase 2 Document Controller** per SPEC §9. New
   architectural territory; largest scope. Benefits from a
   fresh session with SPEC §9 loaded — first feature module
   after the foundation, with the full machinery (signature,
   lockout, content-addressed vault, detail views, print
   stylesheet).

The "upgrade-path migration" option from the 82455aa handoff is
gone — resolved by this commit. The dev DB is now in the same
state a fresh post-ADR-0007 install would be in (11
RolePermission rows linking the Administrator role to every
Phase 1 system permission), ready for any Phase 2 work that
will check authorization against `_currentUser.Permissions`.

Working tree clean as of this entry's commit.

---
