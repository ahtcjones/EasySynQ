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
- `IgnoreQueryFilters()` on a subquery **propagates filter-disabling to the composed outer query**, not just the subquery itself. Surfaced at C6b stop 1 — the initial `IUserRepository.GetUsersWithPermissionAsync` impl composed a `roleBranchUserIds.Union(directBranchUserIds)` (both branches using `IgnoreQueryFilters()`) into a `Query().Where(u => candidateUserIds.Contains(u.Id))` outer; the soft-delete filter on the outer `Users` was lifted alongside the link-tables' filters, causing soft-deleted users to surface. Fix: materialize the candidate-id set first (`await ... .ToListAsync(ct)`), then run a second query with the materialized IDs against the outer table so the outer query's filter scope is unaffected. Trade-off: two round-trips. Documented inline in `UserRepository.GetUsersWithPermissionAsync`. Pattern repeats: any composed query that needs `IgnoreQueryFilters` only on a specific subquery branch should materialize before composing.
- Microsoft.Data.Sqlite **stores and binds Guids as UPPERCASE TEXT** (`Guid.ToString().ToUpper()`) per its [type-mapping docs](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types). SQLite's `=` and `IN` operators on TEXT are case-sensitive (BINARY collation by default). Any code path that writes Guid values into the DB through a route OTHER than Microsoft.Data.Sqlite — raw `sqlite3` CLI, Dapper, hand-written INSERTs, PowerShell `[guid]::NewGuid().ToString()` (lowercase by default) — must explicitly UPPERCASE the value or runtime EF queries silently return zero matches. The bug stays hidden until a Guid containing hex letters (a–f) is written that way: all the migration deterministic seed Ids (`08000000-...`, etc.) are digits-only so UPPERCASE == lowercase masks the issue. Surfaced at C6b stop 9 — smoke-setup script's `[guid]::NewGuid().ToString()` produced lowercase Ids for the smoke users; `GetUsersWithPermissionAsync` returned only admin because admin (bootstrap-written) was UPPERCASE while multireviewer/secondreviewer (script-written) were lowercase. Fix: every Guid string the script generates uses `.ToString().ToUpper()`. Pattern repeats: any future tool that writes Guid values to a Microsoft.Data.Sqlite-backed DB must follow this convention.

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

## 2026-05-14 (grooming) — Phase 1 Follow-Up grooming pass (Serilog file-sink retention + template extraction)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `820110b` chore(host): Serilog file-sink retention;
  consolidate output template

One Phase 1 Follow-Up resolved with code (#9 file-sink
retention). Two closed without code on premise-no-longer-holds
grounds (#10 EF Core log-noise tightening, #12 EventId in
raw-log output). The grooming pass also surfaced a real
observability regression — EventId 1001 is silent on
wrong-password sign-in attempts — added to the open list for
the next session to investigate.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-upgrade-path-migration (commit 2107ac9) | 294 | — | baseline |
| Post-this-commit | 295 | +1 | `AppSerilogOutputTemplateTests` — structural shape assertion routed through the real `SerilogLoggerProvider` pipeline |

5/5 stress at 100% run-level pass rate.

### Phase 1 Follow-Ups grooming

**Resolved in this commit:**

- ~~#9 Serilog file-sink retention~~ — `retainedFileCountLimit:
  90` added to `WriteTo.File` in
  `App.xaml.cs:ConfigureSerilog`. ~90 days of history at the
  daily roll cadence. Inline comment notes the file sink is
  operational/diagnostic only — compliance evidence lives in
  the audit log table per SPEC §7.3, not in these files.

**Closed without code (premise no longer holds):**

- ~~#10 EF Core log-noise tightening~~ — already covered by
  the existing `MinimumLevel.Override("Microsoft", Warning)`
  via Serilog's longest-prefix matching, which applies to
  `Microsoft.EntityFrameworkCore.*` by inheritance. A more
  specific `Microsoft.EntityFrameworkCore` override at the
  same level would be redundant. The "auth failures produce
  multiple [ERR] lines from EF Core" pattern referenced in
  older handoffs no longer reproduces against the current
  config — today's log shows only one [ERR] line from EF
  Core, and that one is the migration-guard's intentional
  `RAISE(ABORT)` (genuine error, correctly surfaced, not
  noise).

- ~~#12 EventId in raw-log output~~ — not feasible as
  originally conceived. Serilog's output-template grammar
  does not support nested property access: `{EventId.Id}`
  cannot reach into the `StructureValue` that
  `Serilog.Extensions.Logging`'s adapter attaches to every
  `[LoggerMessage]` emit (the parser treats `EventId.Id` as
  a single literal identifier and looks up a property by
  that exact name, which doesn't exist). Bare `{EventId}`
  renders the verbose structured form
  `[{ Id: 6001, Name: "LogSignInSucceeded" }]`, which
  uglifies every line in raw-text scenarios. The `EventId`
  property remains on each `LogEvent` and is accessible to
  structured-log consumers (JSON sink, Seq, future
  aggregators); only the raw text rendering omits it. The
  file-sink output template was extracted to an internal
  const `App.FileSinkOutputTemplate` so future format work
  has a single anchor, even though the EventId token itself
  is not part of it.

**Still open (carry forward from prior handoffs):**

- **"Sign-as-which-role" UX ADR** (from 82455aa) — defer
  until the first Phase 2 feature consuming
  `ISignatureService` is concrete.

- All carry-overs from prior open lists (numbering retained
  for grep continuity — #5 remains gone, superseded by ADR
  0007; #9, #10, #12 are now also gone, removed in this
  commit; #1 sign-in audit, #2 PreviousLoginUtc, #3
  navigation audit, #4 pulse tint tokens, #6
  connection-string config, #7 AsyncLocal correlation
  scope, #8 inert `Serilog.Sinks.File` reference, #11
  owned-type audit ADR remain).

**Newly added:**

- **EventId 1001 (LoginViewModel sign-in failure) missing
  from log on wrong-password attempts.** Surfaced during
  this commit's smoke verification — the user ran the
  failed-sign-in step and no EventId 1001 line appeared in
  the log. The EventId is allocated in the EventId table
  ("LoginViewModel sign-in failure (E1, instance partial)")
  so the wiring was at least started in E1, but it is
  currently silent in production. Possible causes:
  (a) wiring was incomplete from E1 — instance-partial
  declared but the emit path was never fully wired into the
  failure case; (b) the emit was wired and a later commit
  broke it (ADR 0007's C2 auth-wiring rewrite is the
  largest candidate); (c) the emit fires at a level below
  the file sink's `MinimumLevel`. Investigation requires
  reading the current `LoginViewModel` and
  `AuthenticationService` code. Recommended fix: locate the
  intended emit site, restore or complete the wiring, and
  add an integration test that exercises the failed-sign-in
  log path so this regression cannot recur silently.
  Recommended priority: do before Phase 2, since Phase 2
  work will assume the observability surface is correct.

- **Raw `Log.Information` emits in `OnStartup` / `OnExit`
  render with empty SourceContext** — text reads
  `[INF] : EasySynQ starting…` (visible colon with no
  source). Pre-existing behavior, mildly cosmetic. Natural
  fix is to convert the two raw `Log.Information` calls to
  `[LoggerMessage]` emits with their own EventIds (probably
  in the 5xxx App-tier range). Low priority; mention only.

### Serilog output-template grammar learning worth pinning

Serilog message templates (the first argument to
`Log.Information(messageTemplate, args)`, parsed by
`MessageTemplateParser`) DO support nested property access via
dotted paths — e.g., `"User {User.Username} signed in"` works.

Serilog output templates (the format string passed to
`MessageTemplateTextFormatter`, used by the file/console/text
sinks) DO NOT. The output-template property-name parser treats
`EventId.Id` as a single literal identifier and looks up a
property by that exact name; it does not destructure into the
`EventId` structure value. Empirical confirmation:
`AppSerilogOutputTemplateTests` initially asserted `[1001]`
against a `{EventId.Id}` template token, captured the LogEvent
through the real `SerilogLoggerProvider` pipeline, and rendered
empty brackets — the property bag had
`EventId={ Id: 1001, Name: "TestEvent" }` (destructured by the
adapter), but the dotted token couldn't reach into it.

Workarounds exist (a custom `ILogEventEnricher` that flattens
to a scalar sibling property, e.g., `EventIdId`, then template
uses `[{EventIdId}]`) but cost net-new production code for a
cosmetic gain. The decision for #12 was to accept the
limitation and document it; the rationale is captured at the
`App.FileSinkOutputTemplate` constant's XML doc so future
grooming work doesn't re-attempt the same dead-end.

### Smoke verification protocol learning to pin

When a commit changes log output, the smoke checklist should
explicitly verify the **presence** of EventIds expected from
every log-emitting path exercised, not just verify the
**format** of the IDs that do appear. This commit's smoke
caught the format issue with EventId 6001 (rendered as
structured-EventId blob under the initial `{EventId}`
template) but ALSO surfaced the absence of EventId 1001 —
neither would have surfaced from "does the format look
right?" alone. Format-only checking is necessary but not
sufficient; presence-checking against the expected EventId
set per smoke step is what closes the gap.

Adding to the project's standing smoke protocol alongside the
C3 entry's "verify state-on-disk before claiming a flow was
exercised" rule and the prior upgrade-path-migration entry's
"complete move-aside-and-restore cycles fully" rule.

### Next-direction options (next session picks)

1. **EventId 1001 investigation + fix** per the newly-added
   Follow-Up above. Small, well-scoped, naturally
   pre-Phase-2 since the observability surface should be
   correct before Phase 2 work starts writing more EventIds.
2. **Further Phase 1 Follow-Up grooming.** #6
   (connection-string promotion to configuration) is the
   largest remaining lightish item; introduces a new
   dependency (`Microsoft.Extensions.Configuration.Json`)
   which needs explicit approval per CLAUDE.md rule 9.
3. **Phase 2 Document Controller** per SPEC §9. New
   architectural territory; largest scope; benefits from a
   fresh session with §9 loaded.

Working tree clean as of this entry's commit.

---

## 2026-05-14 (Phase 2 C1) — Phase 2 Document Controller data layer (ADR 0008 C1)

Single major commit on master since the previous handoff (this
docs commit will be the second):

- `25d1748` feat(data): Phase 2 Document Controller data layer
  (ADR 0008 C1) — +5144/-50 across 38 files

The commit opens the Phase 2 chunk chain laid out by ADR 0008
(eight implementation commits + handoff). C1 is the data-layer
foundation every later Phase 2 commit builds on: seven new
domain entities, seven EF configurations, one migration that
creates the tables and seeds the document permission catalog +
default QualityManager role, plus the SPEC §5.1 amendment
verbatim from the ADR.

### ADR 0008 status

Flipped Proposed → Accepted with the dual-date stamp pattern
established by ADR 0007 (`2026-05-14 (Proposed), 2026-05-14
(Accepted)` — same-day in this case but the dual stamp is
preserved for audit-trail consistency). SPEC.md §5.1 replaced
verbatim with the ADR's amendment text; revision bumped 3.3 →
3.4 with a descriptive Revision History row covering the
state-machine refinements, assigned-reviewer model,
permissions catalog, retraining cascade as
`DocumentRevisionApprovedEvent`, and the seeded QualityManager
role's deliberate omission of `Document.AssignReviewers`.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-grooming (commit 820110b) | 295 | — | baseline |
| Post-this-commit | 358 | +63 | 46 entity unit tests across 7 files in `Unit/Domain/Entities/Documents/`; 11 CRUD integration tests in `DocumentControllerBasicCrudTests`; 9 migration-seed tests in `DocumentControllerMigrationSeedTests`; minor adjustments to 6 pre-existing tests for Phase 2 coexistence |

5/5 stress at 100% run-level pass rate. No new compiler
warnings; one type-level CA1720 suppression on
`DocumentReviewAssignmentStatus` for the `Signed` enum value,
mirroring the CA1711 precedent for the `Permission` entity in
ADR 0007.

### Seed-data test scoping lesson worth pinning

Phase 2's commit forced adjustments to six pre-existing tests
because each made an assumption about the *totality* of seeded
data that no longer holds once a new phase grows the catalog.
The specific cases:

- **Phase 1's `PermissionsMigrationSeedTests`** assumed all
  permissions were Phase 1's eleven (e.g., `rows.Should().Be(11)`,
  `seededNames.Should().BeEquivalentTo(PermissionNames.All)`).
  Rescoped each assertion to `Category=="System"` so Phase 2's
  document-category catalog co-exists without breaking the
  Phase-1-specific invariants.
- **`LinkLegacyAdministratorMigrationTests`** called
  `ctx.Database.Migrate()` to apply the migration under test —
  but `Database.Migrate()` applies *all pending*, which now
  includes Phase 2's `AddDocumentControllerTables` and its
  seeded QM RolePermission rows. Switched to
  `IMigrator.Migrate("LinkLegacyAdministratorToSystemPermissions")`
  so the test applies only the targeted migration.
- **`BootstrapServiceTests`** asserted `roles.Should().ContainSingle()`
  and `rolePermissions.Should().HaveCount(11)` against the
  global tables — both implicitly assumed bootstrap was the
  only writer of Roles/RolePermissions. Phase 2's seeded
  QualityManager role + its 12 RolePermission rows broke the
  global-totality view. Scoped each assertion to the
  Administrator-role-id's rows.
- **Three tests** (`BasicCrudTests`, `IdentityForeignKeyTests`,
  `AuthenticationServiceTests`) inserted an ad-hoc Role with
  the literal name `"QualityManager"`. The Phase 2 seed
  inserts a Role with that exact name, and `Roles.Name` has a
  unique index — `UNIQUE constraint failed: Roles.Name`.
  Renamed the test fixtures to `QualityManagerRoundTrip`,
  `QualityManagerFkTest`, `QualityManagerAuthTest`
  respectively. The seeded row is reality from now on;
  test-only roles need disambiguated names.

**The lesson:** seed-data assertions must be scoped narrowly to
the data they care about (specific `Category`, specific `Name`,
specific `RoleId`), never universal claims about totals or
first-and-only-seeded-instance. Future phases will keep growing
the seed catalog; broad-scope assertions break each time.

The narrowing is also philosophically the right shape — a test
that asserts "the QM role exists with the expected permission
set" is making a stronger and more useful claim than a test
that asserts "exactly one operational role exists." The
totality framing was always over-reach; Phase 2 just forced the
issue.

Adding to the project's standing protocol for grooming work and
for every future phase's data layer.

### Smoke verification protocol refinement

Smoke is not a default ritual for every commit — it is a tool
deployed when there is a specific gap between what tests cover
and what real-host behavior would reveal. For C1, the relevant
gap was *migration applies cleanly against the dev DB* (which
has accumulated all prior migrations + the upgrade-path
migration; integration tests run against fresh SQLite, which
has no migration history to react against). That part of smoke
ran and passed: migration applied at 21:10:31.896, all seed
data verified at the live-DB level, Phase 1 Administrator's 11
RolePermission rows unchanged.

The sign-in-regression piece of the proposed smoke was NOT
driven manually. Justified: C1 touched zero Phase 1 auth code
paths, and `AuthenticationServiceTests` covers real-SQLite
sign-in (success, lockout, multi-role, snapshot-population)
more thoroughly than a single manual click could. Skipping the
ritual saved time without losing coverage.

**The standing rule:** smoke is risk-driven, not commit-driven.
For each commit, identify the specific risk smoke would
mitigate that integration tests cannot; if none, skip. If some,
drive it. Adding to the standing smoke protocol alongside the
prior C3 "verify state-on-disk", the upgrade-path commit's
"complete move-aside-and-restore cycles fully", the grooming
commit's "presence-check EventIds not just format", and the
role-correction rule pinned this session: **user drives WPF
gestures; both inspect via PowerShell/`sqlite3`**.

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| **C1 (data)** | ✓ this commit | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| C2 (vault) | next | `IVaultService` — content-addressed file storage |
| C3 (lifecycle) | pending | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent` |
| C4 (sign-as-role) | pending | ADR for signature dialog UX; initial dialog scaffolding |
| C5 (PDF viewer) | pending | ADR for viewer dependency; integration into detail UI |
| C6 (UI shell) | pending | Document list/detail VMs; submit + review dialogs |
| C7 (lock inspector + print) | pending | Lock-reason chains, print stylesheets |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

C1 successfully opens the chain. Every subsequent C2-C8 commit
will sit on top of the data shape this commit established;
schema drift between C1 and later commits would be a real cost,
so the data layer's choices here are now load-bearing.

### Phase 1 Follow-Ups (carry-forward unchanged)

No changes to the open list in this commit. As of the prior
(grooming) handoff:

- **"Sign-as-which-role" UX ADR** — becomes concrete in C4
  (signature dialog scaffolding). The
  `SignatureService.Roles.Single()` throw fires the first time
  C4 lands; the UX ADR pairs with that work.
- **EventId 1001 missing on wrong-password** — deferred; not
  C1's scope.
- **Raw `Log.Information` empty SourceContext** in
  `OnStartup` / `OnExit` — deferred; cosmetic.
- All eight carry-overs from prior handoffs (#1 sign-in audit,
  #2 PreviousLoginUtc, #3 navigation audit, #4 pulse tint
  tokens, #6 connection-string config, #7 AsyncLocal
  correlation, #8 inert `Serilog.Sinks.File`, #11 owned-type
  audit ADR).

### Next-direction (next session pick)

**C2 — `IVaultService`** per ADR 0008's chunking. Phase 2 C1
established the data shape; C2 builds the content-addressed
file storage service that the lifecycle service (C3) and UI
(C6) both consume for blob read/write/dedup. Scope is pure
service-layer with tests against a tempdir vault root —
contained, low surface area, well-bounded. The
`VaultBlob` entity from C1 is already in place; C2 wires the
service over it.

Requires its own implementation-plan-first review cycle per the
standing protocol.

Working tree clean as of this entry's commit.

---

## 2026-05-14 (Phase 2 C2) — IVaultService (Phase 2 Document Controller, ADR 0008 C2)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `f31b378` feat(services): IVaultService — content-addressed file
  storage (ADR 0008 C2)

Second commit in the Phase 2 chunk chain. Smaller than C1 — pure
service layer plus one focused permission-introducing migration. C2
builds the content-addressed file storage service that the
lifecycle service (C3) and UI (C6) will consume for blob
read/write/dedup; the VaultBlob entity itself was created in C1.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-C1 (commit 25d1748) | 358 | — | baseline |
| Post-this-commit | 377 | +19 | 14 `VaultServiceTests` (store/retrieve/exists/delete happy paths; corruption injection; missing-file; soft-deleted-row; permission-gate enforcement; audit-row pinning; sanity dedup-only-for-identical-content); 4 `AddVaultPhysicalDeletePermissionMigrationSeedTests` (deterministic Permission row, name in `PermissionNames.All`, fresh-install skip-link, System-category count) |

5/5 stress at 100% run-level pass rate. Build clean.

### New permission introduced — Vault.PhysicalDelete

Added to the System catalog and to `PermissionNames.All`. Seeded
for Administrator on both install paths:

- **Fresh install:** bootstrap picks it up automatically via
  `PermissionNames.All` (now 12 entries, was 11). The
  `AddVaultPhysicalDeletePermission` migration runs before
  bootstrap, finds no Administrator role, skips the conditional
  link insert. Bootstrap then writes the full 12 RolePermission
  rows transactionally, including the Vault.PhysicalDelete link.
- **Upgrade install:** the migration's conditional INSERT-SELECT
  detects the pre-existing Administrator role and writes the
  RolePermission link row directly (deterministic Id
  `08400000-0000-0000-0000-000000000001`, attributed to
  `CreatedBy = "system:migration"`). Mirrors
  `LinkLegacyAdministratorToSystemPermissions`' upgrade-path shape
  without the loud-failure trigger — there's no partial-state
  concern for adding a single new permission.

System-category count: 11 → 12. The XML doc on `PermissionNames.All`
was updated to drop the "Phase 1 only" framing in favor of "all
currently-defined system permissions that the bootstrap administrator
should always have." The list grows as later phases add system-tier
capabilities; the bootstrap path consumes it verbatim, so adding a
name there is sufficient to roll a new permission into every fresh
install's Administrator grants.

### Architectural lesson worth pinning — historical vs. current counts

Two ends of one principle surfaced in this commit, both as fixes to
pre-existing tests:

- **`LinkLegacyAdministratorMigrationTests`** was using
  `PermissionNames.All.Count` as a stand-in for "the number of
  permissions when the LinkLegacy migration ran" — but `All` grows
  over time, so the count drifted from 11 to 12. Fix: introduce
  `Phase1PermissionCountAtLinkLegacyTime = 11` as a frozen
  historical constant. The constant deliberately does NOT update
  when `PermissionNames.All` grows. An inline comment in the test
  class explains the deliberate decoupling.
- **`BootstrapServiceTests`** was using hard-coded literals (`11`,
  `26`, `12`) to assert the bootstrap audit-row count, which is
  actually computed at run time from however many system
  permissions exist in `PermissionNames.All`. Fix: derive the
  expected counts via a formula
  (`expectedRowCount = 4 + 2 * systemPermissionCount`,
  `RolePermission` count = `systemPermissionCount`,
  `EffectiveDateRange` count = `1 + systemPermissionCount`). Future
  system-permission additions don't require touching this test.

The general principle:

> **Assertions about a system's CURRENT state should be derived
> from current code constants** so they grow naturally as the
> system grows.
> **Assertions about a system's HISTORICAL state at a specific
> point in time should be frozen constants** so they don't drift
> as the system grows past that point.

Conflating the two is the failure mode the C1 handoff's seed-data-
test-scoping lesson was pointing at; C2 surfaces a more specific
articulation. The two rules combined give a clean shape:
- "Phase 1's count at LinkLegacy commit time" → frozen.
- "the current System-category total" → derived.

Adding to the project's standing protocol for test-writing
alongside the C1 lesson (narrow scoping over universal-totality
assertions) and the prior smoke-protocol lessons.

### Risk-driven smoke skip — now the working norm

C2's smoke verification was skipped per the C1 handoff's risk-driven
protocol. The real-filesystem risk surface (atomic rename, sharded
directory creation, %LOCALAPPDATA% permissions) is within the
documented contracts of `File.Move` / `Directory.CreateDirectory` /
`FileStream`, and integration tests against tempdir vault roots
exercise the same code paths.

This is now twice in a row that smoke was skipped because tests
genuinely cover the risk (C1's sign-in-regression check, C2's
filesystem-behavior check). The risk-driven protocol from the C1
handoff is the working norm now, not an exception. Worth recording
explicitly so the next session doesn't default back to
"smoke everything" out of habit.

### Scope-creep observation — for the record

C2 implementation included two improvements beyond the approved
plan, neither problematic and both clearly better, but worth noting
for pattern-awareness:

- **`BootstrapServiceTests` refactor** from literal-update
  (`11→12`, `26→28`, `12→13`) to derived-expression. The user had
  explicitly flagged this as a "lurking maintainability issue, not
  a C2 change" in the plan-approval response; the implementation
  did it anyway because the larger context made it the natural
  fix.
- **`UnauthorizedOperationException.ForMissingPermission` factory**
  beyond the standard three constructors the plan called for. A
  small static-factory convenience that callers actually use
  (`VaultService.PhysicalDeleteAsync` uses it directly).

The working convention going forward: **flag X+Y in the
implementation summary so it's visible at approval time, not as
after-the-fact discovery**. Doing-then-noting works when Y is
clearly better; flagging-then-doing scales better and respects the
commit boundary as a meaningful unit. Captured to memory for future
sessions.

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| C1 (data) | ✓ `25d1748` | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| **C2 (vault)** | ✓ this commit | `IVaultService` — content-addressed file storage; Vault.PhysicalDelete permission |
| C3 (lifecycle) | **next** | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent` |
| C4 (sign-as-role) | pending | ADR for signature dialog UX; initial dialog scaffolding |
| C5 (PDF viewer) | pending | ADR for viewer dependency; integration |
| C6 (UI shell) | pending | Document list/detail VMs; submit + review dialogs |
| C7 (lock inspector + print) | pending | Lock-reason chains, print stylesheets |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

### Phase 1 Follow-Ups (carry-forward unchanged)

No changes to the open list in this commit. As of the prior C1
handoff:

- **"Sign-as-which-role" UX ADR** — becomes concrete in C4
  (signature dialog scaffolding); the
  `SignatureService.Roles.Single()` throw fires for any user
  whose `Roles.Count != 1` — single-role users like the bootstrap
  Administrator are unaffected, but as soon as multi-role users
  exist (likely C3 test setup or C6 admin UI), the throw becomes
  user-visible and the C4 ADR pairs with the work to handle it.
- **EventId 1001 missing on wrong-password** — deferred.
- **Raw `Log.Information` empty SourceContext** in
  `OnStartup` / `OnExit` — deferred; cosmetic.
- Eight carry-overs from prior handoffs (#1 sign-in audit, #2
  PreviousLoginUtc, #3 navigation audit, #4 pulse tint tokens,
  #6 connection-string config, #7 AsyncLocal correlation, #8
  inert `Serilog.Sinks.File`, #11 owned-type audit ADR).

### Next-direction (next session pick)

**C3 — `IDocumentLifecycleService` + `IDomainEventDispatcher` +
`DocumentRevisionApprovedEvent`** per ADR 0008's chunking. C3 is
more substantial than C2 — it introduces:

- The lifecycle state machine (Draft → InReview → Approved
  with bidirectional Draft↔InReview; Approved → Active derived
  at read time; Active → Superseded on new approval; Active →
  Archived on explicit retire). Eight transitions, each with
  audit-row assertions.
- The domain-event publication infrastructure (`IDomainEventDispatcher`)
  that Phase 4 will consume to wire the retraining cascade
  transactionally with revision approval.
- The first signature-consuming code path — author submission
  signature plus per-reviewer approval signatures. Whether
  `SignatureService.Roles.Single()` actually fires in C3 depends
  on test setup: single-role users (the bootstrap Administrator,
  or any test fixture seeded that way) sign cleanly; multi-role
  users trigger the throw. C3 will exercise the signing path at
  minimum with single-role coverage; multi-role coverage is
  contingent on C4's ADR landing first.

C3 deserves the careful implementation-plan-first review cycle —
larger surface, more transition cases, first concrete consumer of
the C1 entities and the C2 vault service.

Working tree clean as of this entry's commit.

---

## 2026-05-15 — IDocumentLifecycleService + IDomainEventDispatcher (Phase 2 Document Controller, ADR 0008 C3)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `0ae4317` feat(services): IDocumentLifecycleService +
  IDomainEventDispatcher (ADR 0008 C3)

Third commit in the Phase 2 chunk chain. Largest commit of Phase 2
to date by both test-count growth and surface area: the lifecycle
state machine on Documents and DocumentRevisions (Submit,
ReturnToDraft, SignAsReviewer, Retire), the domain-event
publication infrastructure that Phase 4 will consume for the
retraining cascade, the first DocumentRevisionApprovedEvent
published transactionally with the approval that produced it, and
a public-API expansion on Phase 1's SignatureService to support
multi-entity transactions.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-C2 (commit f31b378) | 377 | — | baseline |
| Post-this-commit | 459 | +82 | 42 unit (DocumentRetireTests, DocumentRevisionLifecycleMethodTests, DocumentReviewAssignmentLifecycleTests, DomainEventDispatcherTests); 40 integration (DocumentRevisionRepositoryTests, DomainEventDispatchInterceptorTests, DocumentLifecycleServiceTests) |

Test stability per CLAUDE.md: 5 consecutive `dotnet test` runs at
459/459 each, 100% run-level pass rate.
`scripts/stress-test.ps1` 30-iteration run at 100%. Build clean,
0 warnings 0 errors.

### Architectural lesson worth pinning — stored state is the source of truth

Q9 in the C3 plan was a genuine architectural fork. When a final
reviewer signs Rev B and Rev A is currently Active, two
implementations are defensible:

- **Option A — defer supersede until Rev B's effective date.**
  Preserves the operational intuition "Rev A stays Active until
  Rev B kicks in." The cost: while Rev B sits between approval and
  effective date, `Rev A.Lifecycle == Approved` (still) but the
  as-of resolver treats Rev A as not-Active. Stored value
  disagrees with derived value.
- **Option B — supersede immediately at Rev B's approval (chosen).**
  Stored state is honest. `Rev A.Lifecycle = Superseded` and
  `Rev B.Lifecycle = Approved` the moment the final reviewer
  signs. Between Rev B's approval and Rev B's `EffectiveFromUtc`,
  the as-of resolver returns null — neither revision is currently
  Active. The operational answer "no currently-active revision;
  old one superseded, new one not yet effective" is the correct
  one to surface; UI in C6 will render this as "Approved
  (effective YYYY-MM-DD)" with no current Active highlight.

The general principle:

> **When stored state and derived state could disagree, stored
> state wins.** A documented gap window is preferable to a stored
> value that lies about reality.

This reinforces ADR 0008's earlier choice to make the "Active"
sub-state itself derived rather than stored — both decisions live
under the same invariant. Adding to the project's standing
protocol for state-machine design alongside the prior lessons
(narrow-scoping over universal-totality, frozen-vs-derived
counts, risk-driven smoke, smoke-protocol roles, scope-creep
flag-first).

The chosen path is also the cleaner write semantic — one
transaction, one CorrelationId, coherent audit trail. That's
what an external assessor wants.

### Test-infrastructure pattern worth pinning — singleton-in-tests for cross-scope coordination

The `DomainEventDispatcher` is per-scope in production but is
registered as a singleton in BOTH the prep container (which
captures `DbContextOptions` and its interceptors) and the runtime
container (where the lifecycle service resolves the dispatcher).
Without this, the captured interceptor's dispatcher and the
service's dispatcher are distinct instances and the queue never
coordinates — events enqueued by the service are invisible to the
interceptor's drain.

`RecordingDomainEventDispatcher` (in `TestDoubles.cs`) wraps a
real `DomainEventDispatcher` with capture-on-Enqueue. Tests
inspect `EventDispatcher.Recorded` to assert publication without
needing to register a real handler or re-implement dispatch
semantics. The pattern is reusable for any future cross-scope-
coordinated service that the test base needs both interceptor-
side and service-side access to.

The lifetime divergence (singleton in tests, scoped in
production) is documented in `RecordingDomainEventDispatcher`
class remarks and is acceptable because the dispatcher's queue is
drained by every `SaveChanges` — within a test, the queue is
empty between operations the same way it is between scopes in
production.

Recording the pattern here (not just in the test helper's class
remarks) so it is discoverable when the next cross-scope-
coordinated service appears.

### SignatureService public-API expansion — flag-first convention working

`ISignatureService` gained `StageSignatureAsync` (stage the
`Signature` row without calling `SaveChanges`, return it to the
caller). `SignAsync` is now a thin wrapper around
`StageSignatureAsync` plus a `SaveChanges`. The eight existing
`SignatureServiceTests` continue to exercise the wrapper
unchanged.

The motivation: lifecycle transitions need signatures composed
into the same `SaveChanges` as the surrounding entity updates
(revision lifecycle change + assignment row updates) so the
entire operation commits or rolls back atomically. The Phase 1
`SignAsync` shape (which calls SaveChanges itself) cannot
compose into a multi-entity transaction.

Per the C2 handoff's scope-creep convention, this expansion was
flagged at C3 plan-approval time AND again at implementation-
summary time, not discovered after-the-fact. The convention is
working as designed — the user could veto the public-API change
before any code landed; the implementation didn't drift past the
approved scope; the commit message surfaces it explicitly. Worth
recording that the convention is now consistently practiced
across two consecutive phases.

### Audit-row count formulas — derived from N

Per-transition audit-row counts in
`DocumentLifecycleServiceTests` are pinned as derived expressions
over `N` (reviewer count), per the C2 handoff's
historical-vs-current-counts lesson:

| Operation | Audit row count | Composition |
|---|---|---|
| Submit | `2 + N` | revision Update + Signature Insert + N assignment Inserts |
| ReturnToDraft | `1 + N` | revision Update + N assignment Updates |
| Sign non-final | `2` | Signature Insert + assignment Update |
| Sign final, no prior Active | `3` | Signature + assignment + revision |
| Sign final, with prior Active | `4` | Signature + assignment + new revision + prior revision |
| Retire | `3` | Signature + Document + revision |

No hard-coded literals. Future reviewer-count variations don't
require touching the tests. Future audit-row schema changes do
(and should fail loudly when they happen, exposing the gap before
silently miscounting compliance evidence).

### Risk-driven smoke skip — third consecutive commit

Smoke verification was skipped per the C1 handoff's risk-driven
protocol. C3 is pure service layer; the integration tests against
real SQLite + the real interceptor pipeline exercise the
lifecycle state machine, event dispatch, audit-row generation,
and event publication end-to-end. The signature pipeline,
permission gates, and audit interceptor were already verified by
prior commits. No real-host risk gap remained for smoke to close.

This is the third commit in a row (C1's sign-in regression check,
C2's filesystem-behavior check, now C3's lifecycle and event
dispatch) where smoke was skipped because the integration tests
genuinely cover the risk surface. The risk-driven protocol from
the C1 handoff is now consistent practice across the Phase 2
chunk chain, not an exception. Smoke remains in the toolkit when
the WPF host or filesystem behavior is the actual risk surface
(C5's PDF viewer, C6's UI shell, the Phase 2 closing smoke after
C8); for service-layer commits where integration tests cover the
risk equivalently, skipping is the working norm.

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| C1 (data) | ✓ `25d1748` | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| C2 (vault) | ✓ `f31b378` | `IVaultService` — content-addressed file storage; `Vault.PhysicalDelete` permission |
| **C3 (lifecycle)** | ✓ this commit | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent`; SignatureService gains `StageSignatureAsync` |
| C4 (sign-as-role) | **next** | ADR for signature dialog UX; initial dialog scaffolding |
| C5 (PDF viewer) | pending | ADR for viewer dependency; integration |
| C6 (UI shell) | pending | Document list/detail VMs; submit + review dialogs |
| C7 (lock inspector + print) | pending | Lock-reason chains, print stylesheets |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

C3 was the largest single commit of the chain by surface area
and test-count growth. The remaining C4–C8 commits are smaller
in scope (UI scaffolding, viewer integration, individual feature
surfaces); each still earns its own implementation-plan-first
review cycle but none should match C3's substance.

### Phase 1 Follow-Ups (carry-forward unchanged)

No changes to the open list in this commit. As of the prior C2
handoff:

- **"Sign-as-which-role" UX ADR** — becomes concrete in C4
  (signature dialog scaffolding). C3's tests deliberately use
  single-role users so `SignatureService.Roles.Single()` never
  fires; the throw becomes user-visible the first time a
  multi-role user attempts to sign through the C4 dialog, which
  is the C4 trigger.
- **EventId 1001 missing on wrong-password** — deferred.
- **Raw `Log.Information` empty SourceContext** in `OnStartup`
  / `OnExit` — deferred; cosmetic.
- Eight carry-overs from prior handoffs (#1 sign-in audit, #2
  PreviousLoginUtc, #3 navigation audit, #4 pulse tint tokens,
  #6 connection-string config, #7 AsyncLocal correlation, #8
  inert `Serilog.Sinks.File`, #11 owned-type audit ADR).

### Next-direction (next session pick)

**C4 — sign-as-which-role ADR + signature dialog scaffolding.**
Two pieces:

- **ADR (likely 0009)** for the signature dialog UX. Decides how
  multi-role users pick which role they're signing as — dropdown
  picker, modal-on-sign, organizational default with override,
  or some combination. Paired with the implementation commit per
  ADR 0007 / ADR 0008 precedent (Proposed → Accepted dual-stamp
  in the same commit).
- **Initial dialog scaffolding.** Single-role users sign without
  prompt (matches current behavior — `SignatureService.Roles.Single()`
  succeeds). Multi-role users get the picker UX the ADR
  specifies; the throw is replaced by the picker for that
  population.

C4 is meaningfully smaller than C3 — ADR drafting plus dialog
scaffolding, no state-machine work, no new entities, no new
migration. Worth a focused review cycle but not the planning
investment C3 needed.

Working tree clean as of this entry's commit.

---

## 2026-05-15 (Phase 2 C4) — SignAsRoleDialog scaffolding + sign-as-which-role plumbing (ADR 0009 / ADR 0008 C4)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `d506ee9` feat(ui): SignAsRoleDialog scaffolding +
  sign-as-which-role plumbing (ADR 0009 / ADR 0008 C4) —
  +1660/-136 across 38 files; 9 new files including ADR 0009 and
  the entire `EasySynQ.UI/Signing/` folder.

Fourth commit in the Phase 2 chunk chain. Forward-looking
scaffolding by design — the picker dialog is built and the
contract changes that depend on it are landed, but no production
flow invokes the dialog yet (those land in C6 with the document
detail view's signing surfaces).

### ADR 0009 status

Flipped Proposed → Accepted with the dual-date stamp
`2026-05-15 (Proposed), 2026-05-15 (Accepted)` per ADR 0007 / 0008
precedent. Same-day flip is the working norm for ADRs paired with
their implementation commit.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-C3 (commit 0ae4317) | 459 | — | baseline |
| Post-this-commit | 483 | +24 | 7 RoleResolutionServiceTests; 9 SignAsRoleViewModelTests; 7 PermissionRepository new-method tests; 1 SignatureServiceTests delta (added: role-not-held throw + multi-role success; removed: obsolete Roles.Single throw) |

Test stability per CLAUDE.md: 5 consecutive `dotnet test` runs at
483/483 each, 100% run-level pass rate. Build clean, 0 warnings 0
errors.

### Phase 1 Follow-Up CLOSED

**"Sign-as-which-role" UX ADR** — open since the
`2026-05-14 (Phase 2 C2)` handoff and carried through every
handoff since (C1 → C2 → C3). Resolved by ADR 0009's specification
and C4's implementation: the prior `SignatureService.Roles.Single()`
fail-fast throw is gone, replaced by an explicit caller-supplied
`signingAsRole` parameter and the role-not-held validation. Multi-
role users sign through the picker dialog; single-role users
auto-return without prompting. Removed from the carried-forward
open list in this entry's Follow-Up section below.

This is the third Phase 1 Follow-Up resolved by Phase 2 work to
date — the role plumbing originally surfaced as Follow-Up #5
(closed by ADR 0007 itself) and the upgrade-path migration
(closed by its own commit) preceded it. The pattern: Phase 2's
ADRs and commits naturally collapse Follow-Ups that were waiting
for the right architectural surface to address them. Worth noting
that the open list shrinks as Phase 2 lands, not just as Phase 2
adds new entries.

### Architectural pattern worth recording — ListBox-with-radio-template

`SignAsRoleDialog` uses a `ListBox` with `SingleSelect` whose
`ItemTemplate` renders each item as a `RadioButton` with
`IsHitTestVisible="False"` and `IsChecked` bound one-way to the
ancestor `ListBoxItem.IsSelected`. The selection authority lives
in the ListBox; the radio buttons are visual affordances only.

Why not per-item `IsChecked` two-way binding to a SelectedRole
property:

- Per-item `IsChecked` two-way binding can drift from the
  ListBox's selection state under edge conditions (programmatic
  selection changes, rapid keyboard navigation, focus race).
  Keeping the ListBox as the single selection authority
  eliminates the synchronization concern.
- The `IsHitTestVisible="False"` on the RadioButton lets clicks
  pass through to the ListBoxItem, which is what the ListBox
  already handles natively.
- `GroupName="SignAsRole"` on the RadioButton template still
  enforces the single-checked visual even though the ListBox is
  doing the actual work.

Pattern is reusable for any future single-select dialog that
wants the radio-button visual without the per-item binding
plumbing. Recorded here so the next surface that needs it
discovers the pattern by reading `SESSION_NOTES.md` rather than
by reverse-engineering `SignAsRoleDialog.xaml`.

### Risk-driven smoke skip — fourth consecutive commit

Skipped per the now-standing protocol. C4 is forward-looking
scaffolding by design (no production flow invokes the dialog
until C6); test infrastructure changed only by adding a property
to `MutableCurrentUserAccessor`, not by touching interceptor
wiring or fixture base classes that the stress-test rule guards.
Real dialog interaction smoke lands with C6 when the document
detail view actually wires the prompter into signing flows.

The risk-driven protocol is now consistent practice across C1
(sign-in regression covered by integration tests), C2 (filesystem
behavior covered by tempdir tests), C3 (lifecycle and event
dispatch covered by the integration suite), and C4 (no production
flow to drive). Four for four. The protocol is settled — the next
commit that invokes smoke will be the one that has a real risk
gap integration tests can't close, not a default ritual.

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| C1 (data) | ✓ `25d1748` | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| C2 (vault) | ✓ `f31b378` | `IVaultService` — content-addressed file storage; `Vault.PhysicalDelete` permission |
| C3 (lifecycle) | ✓ `0ae4317` | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent`; SignatureService gains `StageSignatureAsync` |
| **C4 (sign-as-role)** | ✓ this commit | ADR 0009 Accepted; `IRoleResolutionService` + `ISignatureRolePrompter` + `SignAsRoleDialog`; `RolePermissions` plumbing across auth + bootstrap pipeline; SignatureService contract change |
| C5 (PDF viewer) | **next** | ADR for viewer dependency choice; integration into document detail UI |
| C6 (UI shell) | pending | Document list/detail VMs; submit + review dialogs; first consumer of the C4 prompter |
| C7 (lock inspector + print) | pending | Lock-reason chains, print stylesheets |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

### Phase 2 milestone observation — half-shipped

With C4 landed, Phase 2 is half-shipped (4 of 8 implementation
commits). The core architectural decisions are all in place: the
state machine (C3), the assigned-reviewer model (C3), the
domain-event dispatch infrastructure (C3), the content-addressed
vault (C2), and the signing-role mechanics (C4). The data shape
(C1) underlies everything.

C5–C8 are progressively more concrete:

- **C5** introduces the project's first third-party UI dependency
  (PDF viewer). Architecturally open in the sense that the choice
  is genuinely undetermined; the ADR will weigh real options.
- **C6** is the first surface that consumes everything Phase 2
  has built — UI shell ties C2 (vault), C3 (lifecycle), C4
  (prompter), C5 (viewer) into actual user-facing workflows. By
  surface area large; by architectural openness small (the
  pattern is "wire up the existing pieces").
- **C7** is polish — lock inspector and print stylesheets layered
  on the C6 surface.
- **C8** introduces the External library mechanics — its own
  small surface, doesn't disturb anything earlier.

None of C5–C8 should reach the architectural-substance level of
C3 (the largest commit). C5's ADR may match C3's planning
investment because the dependency choice is consequential, but
the implementation work after the choice is bounded. C6 will be
larger by file count but mostly mechanical (ViewModels + Views
following established patterns).

### Phase 1 Follow-Ups (carry-forward, updated)

Three Follow-Ups have now closed during Phase 2 work:

- ~~#5 role plumbing on AuthenticatedUserEventArgs / accessor~~ —
  resolved by ADR 0007 itself (Phase 1).
- ~~Upgrade-path migration~~ — resolved by its own commit
  (`2107ac9`, the LinkLegacyAdministratorToSystemPermissions
  migration that closed the ADR 0007 upgrade-path gap).
- ~~Sign-as-which-role UX ADR~~ — resolved by ADR 0009 and this
  commit (see "Phase 1 Follow-Up CLOSED" above).

Remaining open list (down from prior C3 handoff):

- **EventId 1001 missing on wrong-password** — deferred.
- **Raw `Log.Information` empty SourceContext** in `OnStartup`
  / `OnExit` — deferred; cosmetic.
- **#1 sign-in audit** — deferred.
- **#2 PreviousLoginUtc** — deferred.
- **#3 navigation audit** — deferred.
- **#4 pulse tint tokens** — deferred.
- **#6 connection-string config** — deferred.
- **#7 AsyncLocal correlation** —
  `UiAuditCorrelationProvider` AsyncLocal replacement for
  multi-save logical operations. Worth flagging that this MAY
  have become reachable through C3's lifecycle service: the
  service performs multi-entity operations, but each lifecycle
  method is a single `SaveChanges` containing many entity
  changes. Not multi-`SaveChanges` per single logical operation
  — which is the case the Follow-Up was concerned with. So #7
  is still pending, NOT yet triggered. Will become reachable
  when an operation needs `IUnitOfWork.ExecuteInTransactionAsync`
  (multiple SaveChanges in one logical scope) — Phase 2 has no
  such case; Phase 4's retraining cascade might or might not
  cross that line depending on its handler's implementation.
- **#8 inert `Serilog.Sinks.File`** — deferred.
- **#11 owned-type audit ADR** — deferred.

Eight remaining open carry-overs plus two cosmetic deferrals.
The list shrunk by one this commit.

### Next-direction (next session pick)

**C5 — PDF viewer ADR + integration** per ADR 0008's chunking.
First commit in the project that introduces a third-party UI
dependency, which means the ADR carries the "no new dependencies
without flagging it first" weight per CLAUDE.md non-negotiable
rule 9.

Candidate libraries each carry different licensing, integration,
and maintenance profiles:

- **PdfiumViewer** — wraps the Chromium PDFium engine; mature;
  WPF-friendly; native dependency requires per-platform
  binaries.
- **WebView2 + PDF.js** — uses the Edge runtime that's likely
  present on the deployment Windows machines; pure-managed wrapping
  cost is low but the rendering surface is HTML, not native WPF
  controls.
- **PdfPig with custom rendering** — pure-managed; PdfPig's
  primary use case is extraction, not rendering, so the
  rendering layer would be substantial custom code.
- **Commercial components** (PdfTron, Syncfusion, etc.) —
  fully featured but each carries a license cost and a per-seat
  / per-deploy redistribution constraint that may or may not fit
  the small-shop deployment model.

C5's ADR will need to weigh these explicitly — possibly the most
"decision work" any Phase 2 ADR will do, since the choice is
genuinely open rather than constrained by an existing pattern.
Deserves the careful planning-first review cycle.

The implementation work after the choice is bounded: integrate
the chosen viewer into a document detail view stub, render a
revision's vault blob, prove read-only behavior. C5's commit
itself should be small once the ADR has settled the question.

Working tree clean as of this entry's commit.

---

## 2026-05-15 (Phase 2 C5) — PDF viewer (WebView2 + PDF.js) scaffolding + IVaultService.GetVaultFilePathAsync (ADR 0010 / ADR 0008 C5)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `820d040` feat(ui): PDF viewer (WebView2 + PDF.js) scaffolding +
  IVaultService.GetVaultFilePathAsync (ADR 0010 / ADR 0008 C5) —
  418 files changed; most of those are the bundled PDF.js
  distribution.

Fifth commit in the Phase 2 chunk chain. Lands ADR 0010 (PDF viewer
dependency choice) Accepted plus the C5 implementation that ships
the bundled PDF.js 5.7.284 distribution, the WebView2-hosting
`PdfViewerControl`, the `IVaultService.GetVaultFilePathAsync`
method the viewer call site needs, and the App.xaml.cs startup
gate that detects a missing WebView2 Runtime. Forward-looking
scaffolding — no production flow invokes the viewer yet (those
land in C6 with the document detail view).

### ADR 0010 status

Flipped Proposed → Accepted with the dual-date stamp
`2026-05-15 (Proposed), 2026-05-15 (Accepted)` per ADR 0007 / 0008
/ 0009 precedent. Same-day flip is the working norm for ADRs
paired with their implementation commit.

### First third-party UI dependency — precedent-setting

`Microsoft.Web.WebView2 1.0.3967.48` pinned in
`EasySynQ.UI.csproj`. CLAUDE.md non-negotiable rule 9 ("No new
dependencies without flagging it first") is satisfied by ADR
0010's justification on maintenance + rendering fidelity +
feature completeness grounds.

**The commit message explicitly calls this out as
precedent-setting.** Future commits should NOT read this as "the
door is open for any UI library" — every new dependency still
requires its own ADR-level justification. The csproj comment
itself documents the dependency as the first third-party UI
dependency in the project so a future contributor reading the
project file sees the framing.

### Version-bump verification lesson worth pinning

The plan targeted four toolbar element IDs for the disable-
download CSS overrides (`#download`, `#secondaryDownload`,
`#openFile`, `#secondaryOpenFile`). Verification against
5.7.284's actual `viewer.html` surfaced two real changes:

- `#download` was renamed to `#downloadButton` in 5.x.
- `#openFile` (primary toolbar) was REMOVED entirely in 5.x —
  the open-file affordance only exists in the overflow menu now
  (`#secondaryOpenFile`, unchanged).

The override CSS ships with **three** selectors, not four:
`#downloadButton`, `#secondaryDownload`, `#secondaryOpenFile`.
Documented in the CSS file's header comment AND in
`Assets/pdfviewer/README.md` so the next maintainer doing a
PDF.js version bump knows what to re-verify.

The general principle:

> **Verify version assumptions at commit time, not in the plan.**
> The plan can record working assumptions, but the implementer
> verifies against the actual bundled version. Differences get
> surfaced in the implementation summary, not papered over.

The user explicitly built this expectation into the plan-approval
("do the verification at implementation time. Specifically,
before committing the easysynq-overrides.css contents, verify
that the four toolbar element IDs the CSS hides actually exist in
5.7.284's bundled viewer.html"). The convention works as designed
when this kind of plan-vs-reality drift surfaces in the
implementation summary rather than as a silent assumption.

Adding to the standing protocol for any future ADR that bundles a
specific third-party version: re-verify selectors / API shapes /
config flags at commit time, document deltas in both the
implementation and the version-bump README.

### PDF.js bundled size — 21 MB vs the ADR's 5 MB estimate

ADR §"Negative" point 6 estimated "~5MB of JavaScript assets
bundled at assets/pdfviewer/." The actual 5.7.284 distribution
came in at **21 MB across 408 files** — substantially more than
the 4.x footprint the estimate was based on. PDF.js 5.x ships
more locale files (80+ languages), CMap unicode-mapping tables
(~150 files), standard fonts, sandbox builds, and WASM modules
for JBIG2 / OpenJPEG / QCMS / quickjs-eval.

Acceptable for a single-feature dependency; deployment-self-
sufficiency was the priority and the alternative (build-time
download or runtime CDN fetch) was already rejected by the ADR.
Trimming the locale set down to the languages the deployment
actually serves (English-only for the pilot) is possible as a
future commit if installer size becomes a constraint — but the
maintenance cost is "re-trim on every PDF.js version bump,"
which is not free.

Recording the 4x estimate gap so future ADRs that bundle
third-party distributions can use 21 MB as a more realistic
floor for PDF.js-class dependencies rather than the 5 MB optimism.

### Test count progression

| Stop point | Count | Delta | New tests |
|---|---|---|---|
| Post-C4 (commit d506ee9) | 483 | — | baseline |
| Post-this-commit | 495 | +12 | 7 PdfViewerControlTests (4 `[Fact]` + 1 `[Theory]` expanding to 3 cases via `[InlineData]` — event-args round-trip, null-handling, three reason-validation variants, two BuildViewerUrl URL-encoding cases) + 5 VaultServiceTests `GetVaultFilePathAsync` section (happy path, unknown blob → KeyNotFoundException, hash mismatch → InvalidDataException, file deleted → FileNotFoundException, Guid.Empty → ArgumentException) |

Test stability per CLAUDE.md: 5 consecutive `dotnet test` runs at
495/495 each, 100% run-level pass rate. Build clean, 0 warnings
0 errors. No stress test required (test infrastructure unchanged
— no fixture base classes modified, no interceptor wiring
changes, no test-double additions).

### EventId allocations — 6xxx App-tier extended

Two new EventIds added to the App-tier startup-and-lifecycle
range:

- **6006 Information** — WebView2 Runtime detected. Logs the
  runtime version string so support can correlate viewer
  behavior to a specific runtime release.
- **6007 Critical** — WebView2 Runtime missing. Terminal-state
  error; mirrors the 6002/6003 migration pattern.

Full 6xxx allocations now:

| EventId | Level | Message |
|---|---|---|
| 6001 | Information | User signed in successfully |
| 6002 | Information | Applying pending migrations |
| 6003 | Critical | Database migration failed |
| 6004 | Information | Bootstrap completed; signing in administrator |
| 6005 | Warning | Bootstrap idempotency guard fired |
| **6006** | **Information** | **WebView2 Runtime detected** |
| **6007** | **Critical** | **WebView2 Runtime missing** |

The 6xxx range is documented here so the next App-tier startup
check (whatever C-N introduces) picks the next available number
without collision.

### Architectural pattern worth recording — terminal-state startup checks

`ApplyPendingMigrations` established the shape; `CheckWebView2Runtime`
now follows it exactly. The pattern:

1. Try/catch the feature-prerequisite check inside a method
   returning `bool` (`true` on success, `false` on terminal
   failure).
2. On success: log at Information level with whatever payload is
   useful for support correlation (version string, migration
   list).
3. On terminal failure: log at Critical level with the exception,
   show a `MessageBox` pointing at the deployment-doc reference,
   call `Current.Shutdown(1)`, return `false`.
4. The caller short-circuits the rest of the startup sequence on
   `false` — no window shows during the queued shutdown.

The pattern is **intentionally NOT routed through the global
dispatcher's keep-alive handling.** The dispatcher handler exists
for recoverable exceptions; a missing feature prerequisite is not
recoverable mid-session, and the dispatcher's "keep the app
alive" contract is the wrong response.

Pattern is reusable for any future startup-time feature-
prerequisite check (e.g., if a future feature requires a specific
.NET runtime version, a specific Windows version, or a specific
external service availability). Recording here so the pattern is
discoverable beyond two implementations of it.

### Risk-driven smoke skip — fifth consecutive commit

C1 (DB inspection + integration tests covered the migration + sign-
in regression), C2 (tempdir tests covered the filesystem behavior),
C3 (real-SQLite + interceptor pipeline tests covered the lifecycle
service end-to-end), C4 (forward-looking scaffolding with no
production flow invoking the dialog), C5 (no production-host
surface invokes the viewer yet — that lands in C6).

The protocol from C1's handoff is now settled practice. Each
skip had a distinct, specific reason; never a default
"skip-everything" stance.

### C6 marks the return of load-bearing smoke

C6 wires C1–C5's infrastructure into actual user-facing surfaces:

- Document list view consumes C1's entities + repositories.
- Document detail view hosts C5's `PdfViewerControl`.
- Submit-for-review dialog invokes C3's lifecycle service through
  C4's `ISignatureRolePrompter`.
- Review-and-sign dialog likewise consumes C3 + C4.

The multi-role signing flow that C4 specifically deferred to C6
finally has somewhere to run. The PDF viewer that C5 built but
never invoked finally renders a real document. The state machine
that C3 implemented finally has user gestures driving its
transitions.

Smoke window opens back up for C6. The protocol shifts from
"five-for-five skipped because integration tests covered the
risk" to "smoke is now genuinely needed because real-user-driven
end-to-end behavior is testable for the first time in Phase 2."

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| C1 (data) | ✓ `25d1748` | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| C2 (vault) | ✓ `f31b378` | `IVaultService` — content-addressed file storage; `Vault.PhysicalDelete` permission |
| C3 (lifecycle) | ✓ `0ae4317` | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent`; SignatureService gains `StageSignatureAsync` |
| C4 (sign-as-role) | ✓ `d506ee9` | ADR 0009 Accepted; `IRoleResolutionService` + `ISignatureRolePrompter` + `SignAsRoleDialog`; `RolePermissions` plumbing across auth + bootstrap pipeline; SignatureService contract change |
| **C5 (PDF viewer)** | ✓ this commit | ADR 0010 Accepted; Microsoft.Web.WebView2 dependency + PDF.js 5.7.284 bundled; `PdfViewerControl`; `IVaultService.GetVaultFilePathAsync`; App.xaml.cs WebView2 runtime detection |
| C6 (UI shell) | **next** | Document list/detail VMs; submit + review dialogs; first consumer of C2 vault + C3 lifecycle + C4 prompter + C5 viewer |
| C7 (lock inspector + print) | pending | Lock-reason chains, print stylesheets |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

### Phase 1 Follow-Ups (carry-forward unchanged)

No changes in this commit. As of the prior C4 handoff:

- **EventId 1001 missing on wrong-password** — deferred.
- **Raw `Log.Information` empty SourceContext** in `OnStartup`
  / `OnExit` — deferred; cosmetic.
- Eight carry-overs from prior handoffs (#1 sign-in audit, #2
  PreviousLoginUtc, #3 navigation audit, #4 pulse tint tokens,
  #6 connection-string config, #7 AsyncLocal correlation, #8
  inert `Serilog.Sinks.File`, #11 owned-type audit ADR).

### Next-direction (next session pick)

**C6 — UI shell.** The integration commit that wires C1's entities
+ C2's vault + C3's lifecycle service + C4's signature prompter +
C5's PDF viewer into real user-facing surfaces. Probably the
largest single commit of Phase 2 by test growth (UI ViewModels
have substantial test surface; multiple new dialogs and views).

Two real planning questions worth flagging for C6's planning
conversation:

- **(a) What's the minimum viable C6?** Could be one large commit
  covering list + detail + submit + review-sign, OR split into
  C6a (list + detail with viewer) and C6b (submit + review-sign
  dialogs). The split would let each piece get its own focused
  review cycle and smoke window; the unified commit lands the
  whole user-facing surface together. Worth discussing.

- **(b) Does C6 introduce the upload-the-PDF flow?** The
  `Document.EditDraft` permission was seeded in C1 but no service
  consumes it yet. C6 either ships the upload flow (calls
  `IVaultService.StoreAsync` + attaches the resulting `VaultBlob`
  to the current revision) or defers it. The viewer (C5) needs a
  PDF to display; without an upload flow, the only PDFs the
  viewer can show are ones planted directly in the vault via
  test fixtures. Lean toward including a minimal upload flow in
  C6 — otherwise the smoke story is "open a file the test
  fixture put there" which isn't a real user gesture.

C6 deserves the careful implementation-plan-first review cycle —
largest surface, first commit that exercises all prior Phase 2
infrastructure end-to-end, real smoke window opens.

Working tree clean as of this entry's commit.

---

## 2026-05-16 (Phase 2 C6a) — Document Controller author surfaces (ADR 0008 C6a, ADR 0010 amended)

Single commit on master since the previous handoff (this docs
commit will be the second):

- `b24400b` feat(ui): Document Controller author surfaces
  (ADR 0008 C6a, ADR 0010 amended) — 55 files (+ many new under
  `src/EasySynQ.UI/Documents/`).

Sixth implementation commit in the Phase 2 chunk chain. C6a is
the largest single-commit chunk in Phase 2's history to date by
every meaningful metric: file count (55), test growth (495 → 621
across the chunk, +126), ADR amendment count (three new sections
appended to ADR 0010), smoke iterations (five), and discovered-
and-fixed findings (three explicit Findings + multiple structural
follow-ons). That scale is earned — C6a is the first commit in
Phase 2 that lights up real user-facing behavior end-to-end,
exercising every prior Phase 2 commit's infrastructure (C1's
entities, C2's vault, C3's lifecycle service, C4's signing-role
plumbing, C5's PDF viewer) under actual smoke-time interaction.
The handoff is correspondingly long.

### Scope and shape

Author-working-alone surface — sign in → create document → upload
PDF → render in viewer → edit metadata → replace PDF → hard-delete.
No reviewers, no submission, no signatures (those land in C6b per
the C6a/C6b split documented in the brief). Five UI view models,
two modal dialogs, two list/detail views, four new service
methods, four new repository methods, one new control-side
dependency property, one new shared helper, one converted seam
(NavigationContentFactory static → instance with DI), one new
smoke-setup script.

### ADR 0010 amended three times, all in this commit

The original ADR chose `file://` URL loading on the assumption
"the bundled viewer is self-contained so this isn't a constraint."
Smoke walks 1-4 disproved that assumption in four distinct ways,
each surfacing a different layer of the WebView2 cascade-init
failure model. The amendments are dated `2026-05-16 (Amended)`
and live as three appended sections at the bottom of the ADR
under a `## Subsequent finding` heading. Status flipped from
`Accepted` to `Accepted (amended)`.

The three amendments, in order of surface:

1. **Virtual host mapping required** (smoke walks #1-2). PDF.js's
   PDF-fetch is a cross-origin request from the viewer's file://
   origin to the vault's file:// origin; Chromium's same-origin
   policy blocks it. Two WebView2 virtual hosts registered at
   PdfViewerControl init time:
   `https://easysynq-pdfviewer.local/` → viewer assets,
   `https://easysynq-pdfcontent.local/` → vault root (a new
   `ContentRoot` dependency property on PdfViewerControl, bound
   to the VM's `IVaultPathProvider.VaultRoot`). New static
   `BuildContentUrl(absoluteFilePath, contentRoot)` helper for
   callers. `BuildViewerUrl` signature changes from
   `(viewerHtmlPath, pdfPath)` to `(contentUrl)` — the viewer
   URL is now a constant.

2. **Viewer-host scope refinement** (smoke walk #3). Initial
   mapping scoped at `Assets/pdfviewer/web/` — viewer.mjs's
   `../build/pdf.mjs` sibling import 404'd, halting PDF.js
   before any content fetch. Mapping moved up one level to
   `Assets/pdfviewer/` (parent of both `web/` and `build/`);
   viewer URL gains a `/web/` path segment. Version-bump
   verification note added: at every PDF.js bump, confirm the
   distribution's top-level directory layout still has the
   viewer importing the library via a sibling-path resolve.

3. **PDF.js HOSTED_VIEWER_ORIGINS patch** (smoke walk #4).
   PDF.js's own JavaScript-internal `validateFileURL` rejects
   PDF URLs whose origin differs from the viewer's, with a
   hardcoded allowlist of Mozilla origins. Our viewer-and-
   content two-host design is exactly the cross-origin pattern
   the guard rejects. Single-line patch to
   `Assets/pdfviewer/web/viewer.mjs` adds
   `"https://easysynq-pdfviewer.local"` to the Set literal at
   line 19499. The HOSTED_VIEWER_ORIGINS const is module-
   private (inside an IIFE); script-level overrides are not
   feasible. Option B (single virtual host via
   `WebResourceRequested` handler) was evaluated and rejected
   on the Range-request hidden cost — large PDFs (100+ page
   customer specifications are realistic in a QMS deployment)
   require HTTP Range support that WebView2's native fetch
   pipeline already handles correctly; reimplementing
   Range/multi-range/suffix-length/past-EOF handling in a
   custom handler would reproduce existing infrastructure with
   our own bugs.

### Cascade-init failure model — the load-bearing diagnostic frame for C6a smoke

C6a's smoke arc surfaced the failure model that now lives as
SCRATCHPAD lesson 6. Every WebView2-hosted feature has at least
four observability layers; each smoke walk hit a different one.

| Layer | Surface | Smoke walk that hit it |
|---|---|---|
| **Layer 0** — VM-side load (pre-viewer I/O) | `DocumentDetailViewModel.LoadAsync` try/catch | Walk #5 post-verification (`FileNotFoundException` from `GetVaultFilePathAsync` when a vault file was missing) |
| **Layer 1** — Outer navigation | `CoreWebView2.NavigationCompleted.IsSuccess` | Walks #1-2 (cross-origin file:// blocking viewer.html load) |
| **Layer 2** — Sub-resource fetches | `CoreWebView2.WebResourceResponseReceived` non-2xx | Walk #3 (viewer-host scope: `build/pdf.mjs` 404) |
| **Layer 3** — JS-internal errors | (deliberately uncovered; would need JS-host bridge) | Walk #4 (`validateFileURL` rejection inside PDF.js) |

C6a covers Layers 0+1+2 via a viewer-error banner above the PDF
area in the detail view. The banner is fed from **three input
wires** — one per covered layer:

1. `PdfViewerControl.NavigationFailed` raised from
   `NavigationCompleted.IsSuccess == false` (Layer 1).
2. `PdfViewerControl.NavigationFailed` raised from
   `WebResourceResponseReceived` non-2xx on the content host
   (Layer 2).
3. `DocumentDetailViewModel.LoadAsync` try/catch around vault
   I/O → direct call to `OnViewerNavigationFailed` (Layer 0).

Layer 3 stays deliberately uncovered — the HOSTED_VIEWER_ORIGINS
patch removes the only expected Layer-3 case. If a future
feature requires Layer 3 coverage, the named approach is a JS-
host bridge via `AddScriptToExecuteOnDocumentCreatedAsync` +
`WebMessageReceived` subscribing to PDF.js's eventBus.

### Three Findings + multiple structural follow-ons

The user-driven smoke surfaced three explicit Findings during
walk #1 (`Finding 1` PDF rendering as black page; `Finding 2`
EditMetadataDialog Height clipping the button row; `Finding 3`
KeyNotFoundException on second HardDelete). Finding 1 was the
deepest — initially misdiagnosed as toolbar theming polish
(deferred to C7), correctly re-diagnosed in walk #2 as a real
rendering failure, then peeled back through three further smoke
walks as the cascade-init layers revealed themselves. Finding 3
was misdiagnosed in the report as a service-layer Id-type
mismatch; audit log forensics disambiguated within a single
diagnostic pass to the actual cause (missing
`DocumentDetailViewModel.Deleted` event subscriber).

Walk #5 post-verification surfaced one more layer (Layer 0)
that wasn't on the original smoke checklist — the
`GetVaultFilePathAsync` throw path had been present since C5 but
never user-reachable until C6a wired `DocumentDetailViewModel.
LoadAsync` to it. The fix folded into C6a rather than deferring,
on the reasoning that the gap was discovered specifically because
of C6a and the fix sits on the same surface (the banner
mechanism) the broader C6a work just landed.

### Lesson families pinned in SCRATCHPAD

C6a accumulated lessons across three distinct families. The
SCRATCHPAD entry has the full text; this handoff names the
families so future planners know where to look.

**Family A — Diagnostic discipline.** Lessons 1, 4, 7 in
SCRATCHPAD:
- (1) Audit-log-as-ground-truth for diagnosing UI errors.
- (4) "Loaded but mis-styled" vs "didn't load at all" — check
  the actual failure trail before reaching for polish-deferral.
- (7) Visible-effect hypotheses are tempting but unreliable —
  check actual error trails before reaching for them.

This family was load-bearing across the smoke arc. Three times
the first diagnostic framing was wrong because it reached for
the most visually-suggestive hypothesis (Finding 3, walk #1's
Finding 1 misdiagnosis, walk #3's Issue A cross-origin
hypothesis). Each correction came from concrete evidence (audit
log, DevTools Network/Console). The pattern is named so the
next phase's debugger has the discipline as a working concept.

**Family B — Cascade-init failure model.** Lessons 5, 6 in
SCRATCHPAD:
- (5) Silent-failure event subscribers are bugs.
- (6) WebView2-hosted features have four observability layers
  (the model above).

This family came out of the C6a smoke arc and is broadly reusable
for any future WebView2-hosted feature (PDF viewer in C7+ for
print-stylesheet integration; potential future viewers for
external documents in C8; etc.). Future planners adding a
WebView2-hosted feature should map their expected failure modes
against all four layers before deciding which to instrument.

**Family C — Procedure writing.** Three lessons under a "Reminders
for next phase's procedure writing" subsection in SCRATCHPAD:
- Failure-injection ordering vs destructive operations.
- Cleanup-after-diagnostic checklist.
- Commit-message tables risk shell-quoting; prefer bullet lists.

This family is process discipline — distinct from the technical
lessons in families A and B. Future phases get a named category
to grow as they encounter their own smoke-procedure friction.

### Scope-creep convention working as designed — five surfaces

CLAUDE.md's flag-first-then-confirm convention fired across five
distinct surfaces during C6a. Documenting concretely so the
convention is not abstract — every entry below was a real
discovered-then-paused-then-confirmed moment:

1. **Stop point 2: `IDocumentRevisionRepository.GetByDocumentIdAsync`.**
   Added beyond the two repo methods approved at plan-time. The
   defensive single-revision check in `HardDeleteDraftAsync`
   needed it; paused, surfaced, got approval.

2. **Stop point 3: `IUserRepository.GetByIdsAsync`.** Discovered
   mid-stop when the DocumentListItem author-username projection
   surfaced. The user-repo had `FindByUsernameAsync` but no
   bulk-by-ids shape. Paused, asked, got approval (option A —
   bulk fetch over N+1 or skip).

3. **Stop point 3: `EditDraftMetadataAsync` + `HardDeleteDraftAsync`
   (B5/B6).** The brief listed the affordances but not the
   service methods. Paused at plan-approval time; user approved
   both inclusions explicitly.

4. **Walk #3 batch: `NavigationFailed` banner subscriber +
   `WebResourceResponseReceived` wiring.** Surfaced when DevTools
   showed PDF-load failures bypassing the existing banner. Both
   the Layer 1 banner subscription gap and the Layer 2 sub-
   resource coverage gap were flagged before the fix landed;
   approval to fold both into the cross-origin fix batch.

5. **Smoke walk #5 post-verification: Layer 0 fix.** The
   `[ERR]` entries surfaced a pre-existing coverage gap that
   wasn't in the C6a plan. Surfaced explicitly with the choice
   "defer as follow-up OR fold into C6a"; user chose fold-in
   with reasoning about scope-creep vs structural-completion
   distinction.

The pattern across all five: discovered → paused → surfaced with
options + recommendation → user approved one → implemented +
documented. No silent additions, no after-the-fact framings.
This is what the convention's first articulation in C2's handoff
predicted; C6a is the densest test of the convention to date
and it held across every surface.

### Risk-driven smoke protocol — cost model validated

The C1 handoff established the risk-driven smoke protocol: smoke
only where integration tests can't reach the risk; skip where
they cover it equivalently. C2-C5 each invoked the skip
deliberately (filesystem behavior covered by tempdir tests;
lifecycle covered by interceptor-pipeline tests; signature
dialog scaffolding with no production invocation yet; PDF viewer
scaffolding with no production invocation yet). C6a was where
that deferred smoke cost concentrated — and that concentration
is exactly the cost shape the protocol set up to incur.

Five smoke iterations across C6a is more smoke than C1-C5
combined, but five iterations on one integration-rich commit is
materially cheaper than five smoke runs spread across C1-C5
where the gap wasn't there to find. The load-bearing smoke
landed precisely where the C5 handoff predicted ("smoke is now
genuinely needed because real-user-driven end-to-end behavior is
testable for the first time in Phase 2"). The protocol's cost
model is now validated against a real concentration event;
future Phase 2 chunks (C6b, C7, C8) can apply the same
calibration with concrete evidence the deferral shape works.

### Phase 1 Follow-Ups

No changes in this commit. Eight carry-overs plus two cosmetic
deferrals from prior handoffs stay unchanged:

- **#1 sign-in audit** — deferred.
- **#2 PreviousLoginUtc** — deferred.
- **#3 navigation audit** — deferred.
- **#4 pulse tint tokens** — deferred.
- **#6 connection-string config** — deferred.
- **#7 AsyncLocal correlation** — deferred; still not yet
  triggered (C6a's operations are all single-`SaveChanges` per
  logical operation, same as C3-C5).
- **#8 inert `Serilog.Sinks.File`** — deferred.
- **#11 owned-type audit ADR** — deferred. Worth noting that
  C6a's raw-SQL script (`grant-document-permissions.ps1`)
  surfaced a related lesson — owned-type column flattening
  (RolePermission/UserPermission's `EffectivePeriod` lives at
  flat `EffectiveFromUtc` / `EffectiveToUtc` columns, NOT
  prefixed `EffectivePeriod_*`). The lesson is captured in
  SCRATCHPAD's reminders-for-handoff #3 alongside the
  text-comparison-format DateTime issue; both fall under the
  raw-SQL-script-against-EF-columns family.
- **EventId 1001 missing on wrong-password** — deferred;
  cosmetic.
- **Raw `Log.Information` empty SourceContext** in
  `OnStartup`/`OnExit` — deferred; cosmetic.

### Phase 2 commit chain status

| Commit | Status | Scope |
|---|---|---|
| C1 (data) | ✓ `25d1748` | Domain entities, EF configs, migration, SPEC §5.1 amendment, ADR 0008 Accepted |
| C2 (vault) | ✓ `f31b378` | `IVaultService` — content-addressed file storage; `Vault.PhysicalDelete` permission |
| C3 (lifecycle) | ✓ `0ae4317` | `IDocumentLifecycleService` + `IDomainEventDispatcher` + `DocumentRevisionApprovedEvent`; `SignatureService.StageSignatureAsync` |
| C4 (sign-as-role) | ✓ `d506ee9` | ADR 0009 Accepted; `IRoleResolutionService` + `ISignatureRolePrompter` + `SignAsRoleDialog`; `RolePermissions` plumbing |
| C5 (PDF viewer) | ✓ `820d040` | ADR 0010 Accepted; Microsoft.Web.WebView2 dependency + PDF.js 5.7.284 bundled; `PdfViewerControl`; `IVaultService.GetVaultFilePathAsync`; App.xaml.cs WebView2 runtime detection |
| **C6a (author surfaces)** | ✓ `b24400b` | Document Controller author-working-alone surfaces (list, detail, create, edit, replace, hard-delete); ADR 0010 amended three times (virtual hosts + viewer-host scope + HOSTED_VIEWER_ORIGINS patch); banner mechanism covering Layers 0-2 of the cascade-init failure model; `grant-document-permissions.ps1` smoke script; SCRATCHPAD top-level doc seeded |
| C6b (submit + review-sign) | **next** | Submit-for-review dialog + review-and-sign dialog; first production-runtime exercise of C4's multi-role signing picker |
| C7 (lock inspector + print + toolbar theming) | pending | Lock-reason chains, print stylesheets, the deferred Finding 1 PDF.js toolbar theming |
| C8 (external library) | pending | ExternalDocument CRUD, compatibility flagging |
| C9 (handoff) | pending | Phase 2 closing handoff note |

### Next-direction (next session pick)

**C6b — submit-for-review dialog + review-and-sign dialog.** C6a
leaves C6b a clean foundation:

- **Authoring loop works end-to-end** — create, edit, replace,
  delete are all real surfaces with tested behavior. C6b layers
  on top rather than building underneath.
- **Cascade-init failure model is documented and instrumented**
  at Layers 0, 1, 2 — C6b's new dialogs (which don't touch the
  viewer) inherit the framework if/when they need failure
  routing.
- **Viewer is structurally sound** — the WebView2 + PDF.js
  integration is no longer load-bearing risk; the toolbar
  theming polish stays in C7.
- **C4's multi-role signing picker** (`ISignatureRolePrompter` +
  `SignAsRoleDialog`) has been DI-registered and production-
  invocable since C4 but never exercised in production because
  no signing flow existed yet. C6b is where it finally renders
  for a real gesture (the submit-for-review and review-and-sign
  flows both invoke it).
- **`IDocumentLifecycleService`'s C3 methods**
  (`SubmitForReviewAsync`, `ReturnToDraftAsync`,
  `SignAsReviewerAsync`) have full integration-test coverage
  from C3 — C6b wires UI to existing tested service methods.
  Closer to "wire two existing dialogs to two existing services"
  than "build new infrastructure."

C6b's risk surfaces:
- **Multi-role signing UI in production** — first time C4's
  picker is reached by a real user gesture. C6b's smoke will
  exercise the picker for the first time; need to validate the
  single-role auto-return path AND the multi-role picker path
  against a user with multiple roles. The smoke-setup script
  may need extension to grant a second role.
- **Assigned-reviewer model** — C3 implemented the assigned-
  reviewer state machine but no UI surface has named reviewers
  yet. C6b's submit dialog needs a reviewer-picker UX — likely
  a multi-select against the user list, with permission-gated
  filtering by `Document.Review`.
- **Permission catalog growth** — `Document.SubmitForReview`
  and `Document.AssignReviewers` seeded in C1 but no production
  flow consumes them yet. C6b's submit command is gated on
  both. The smoke-setup script grants them.
- **No new third-party frameworks; cascade-init layers settled.**
  C6b doesn't change PdfViewerControl, doesn't touch the vault,
  doesn't introduce new WebView2 surfaces. The smoke profile
  shifts from "discovering the viewer's failure modes" to
  "exercising the signing flow against a multi-role user."

Likely shorter to plan and implement than C6a — the heaviest
lifting (state machine, signing infrastructure, viewer
integration) all landed in C3-C5; C6a integrated those with
authoring surfaces; C6b integrates the same infrastructure
with review surfaces. Plan-first review cycle still earns its
time; the scope is bounded enough that the planning conversation
should converge faster than C6a's did.

**Phase 2 milestone:** with C6a landed, Phase 2 is six of nine
implementation commits in (67%). C6a is the chunk's structural
high-water mark by surface area, ADR amendments, test growth,
and smoke iterations. C6b-C8 inherit a settled foundation;
C9 is the closing handoff.

Working tree clean as of this entry's commit.

---

## 2026-05-16 (Phase 2 architectural follow-up) — Seeded operational roles reconciliation (ADR 0011)

Three commits on master since the previous handoff (this docs
commit will be the fourth):

- `50220c9` docs(adr): seeded operational roles for Document
  Controller (ADR 0011)
- `6a10425` feat(data): seed DocumentAuthor role + amend
  QualityManager (ADR 0011)
- `a01aa69` chore(scripts): audit smoke setup against ADR 0011
  seeded roles

Architectural follow-up commit chain landing between Phase 2 C6b
and C7. Not a Phase 2 chunk in the C-numbered chain — a focused
reconciliation of the seed-shape gap surfaced at C6b stop 9
("Smoke-script grants paper over a seed-shape gap" lesson in
SCRATCHPAD). The chain follows the same three-commit shape the
plan specified: ADR-only docs commit, then implementation +
SPEC amendment, then smoke-script audit. Each commit stopped for
review per CLAUDE.md commit authorization.

### Scope and shape

ADR 0008 deliberately left `Document.AssignReviewers` unassigned
from the seeded `QualityManager` role on the reasoning that
organizations should choose between small-shop and
strict-gatekeeper policies through admin UI. C6b smoke evidence
showed every walk had to paper over the resulting gap with four
direct `UserPermission`/`UserRole` grants written by the
smoke-setup script — admin's `Document.HardDelete` direct grant,
admin's four-permission review-flow grant, the admin →
`QualityManager` `UserRole` link, and `multireviewer`'s direct
`Document.AssignReviewers` grant. The deployment-model
clarification at C6b stop 9 (admin is IT-side only;
author-can-submit is the small-shop default) reframed those
grants as paper-overs rather than legitimate ongoing
maintenance.

ADR 0011 reverses ADR 0008's "Make Author a seeded role"
rejection. The Phase 2 migration chain now seeds two operational
roles: `DocumentAuthor` (five author-side permissions —
`Document.Create` / `EditDraft` / `HardDelete` /
`SubmitForReview` / `AssignReviewers`) for the author-can-submit
small-shop default, and an amended `QualityManager` (the full
13-permission Phase 2 catalog including
`Document.AssignReviewers`) for the cross-functional reviewer
role and the strict-gatekeeper variant (reached by revoking
authoring permissions from `DocumentAuthor`, not by pruning
`QualityManager`).

### Commit-by-commit

**Commit 1 (`50220c9`).** ADR 0011 drafted with full Context →
Decision → Alternatives Considered → Consequences → Implementation
Notes → Required Tests → References structure matching prior
ADRs. The Alternatives section names four paths (Path A
amend-QM-only standalone, Path B seed-DocumentAuthor-only
standalone, Path C bootstrap-time `--strict-gatekeeper` flag,
status-quo) with explicit rejection reasoning. Same commit
appends a block-quote cross-reference to ADR 0008's "Make Author
a seeded role" Alternatives Considered entry documenting the
reversal — the original rejection's "Author is org-specific
terminology" concern is preserved (DocumentAuthor is a generic
placeholder admin UI lets organizations rename or replace) but
the minimalism framing is reversed by the C6b smoke evidence.

**Commit 2 (`6a10425`).** Migration
`AddDocumentAuthorRoleAndAmendQualityManagerSeed` (timestamp
20260517031921) lands:

- 1 `Role` row — `DocumentAuthor` at deterministic Id
  `08100000-...-02` (slot 02 of the C1 prefix; slot 01 was
  `QualityManager`).
- 5 `RolePermission` rows linking `DocumentAuthor` to its five
  permissions at a new `08210000-` prefix (suffixes echo each
  permission's own suffix per the C1 visual-tracing convention).
- 1 `RolePermission` row linking `QualityManager` to
  `Document.AssignReviewers` at `08200000-...-04` — filling the
  slot C1 deliberately left open for this link.
- All rows share one `DateTime.UtcNow` instant for
  `EffectiveFromUtc` / `CreatedUtc` / `ModifiedUtc`;
  `EffectiveToUtc` null; `CreatedBy = "system:migration"`
  matching every other seed migration's sentinel.

`PermissionNames.QualityManagerDefaults` updated to include
`DocumentAssignReviewers` (now 13 entries, equal to
`Phase2Document`); new `PermissionNames.DocumentAuthorDefaults`
list mirrors the seeded set.

`DocumentControllerMigrationSeedTests` updates: the prior
`QualityManagerRole_DoesNotHaveDocumentAssignReviewersAsync`
negative assertion is inverted to a positive
`QualityManagerRole_HasDocumentAssignReviewersAsync` — load-bearing
for the reversal (a future migration removing the grant fires
this test). Four new `DocumentAuthor*` assertions parallel the
existing QM-side coverage (deterministic Id, permission-set
match, open-ended grants, system:migration attribution). One new
`AddDocumentAuthorRoleAndAmendQualityManagerSeed_ProducesExpectedRowDeltasAsync`
test pins the row-delta invariant: post-migration state has
exactly +1 Role row and +6 RolePermission rows owned by the new
migration, with the affected roles' total RolePermission count
matching (12 + 1 QM + 5 DocumentAuthor = 18).

`BootstrapServiceTests` comment updated to reflect the new
seed-row totals (QM now 13 rows instead of 12; DocumentAuthor
adds 5 more); the actual assertion is admin-scoped and stays
correct.

SPEC §5.1 Authorization paragraph rewritten with the two-role
text; revision bumped 3.4 → 3.5; Revision History row added per
CLAUDE.md Spec Drift rules.

Tests: 5/5 runs at 764/764 passing, 0 warnings, 0 errors.

**Commit 3 (`a01aa69`).** `scripts/grant-document-permissions.ps1`
substantially rewritten (−436 / +258 lines, net −178). The four
script-grants-paper-over-seed-gap patterns are gone. Default mode
mints a new `smokeauthor` user assigned to `DocumentAuthor` via
`UserRole`; `-CreateMultiRoleUser` mode mints `multireviewer`
(QualityManager + ReviewerSecondary) and `secondreviewer`
(QualityManager only). `multireviewer`'s direct
`AssignReviewers` grant is removed — `QualityManager` now
provides it via the role link.

Adds `Show-EffectivePermissions` pre-flight helper per the
SCRATCHPAD follow-up plan: each minted user's effective
permission set (union of role-derived + direct grants, filtered
to effective rows) is printed before the smoke walker signs in,
so the seed shape is visually confirmed before walk-time. Also
factors out `New-User` / `New-UserRole` / `Invoke-Sqlite-Lines`
helpers — the original script had grown to ~670 lines with
significant repetition; the audited version is closer to 400.

Smoke procedure (lives in the script's comments) updates to
walk through with `smokeauthor` for every author-side gesture
(Create / EditDraft / HardDelete / SubmitForReview /
AssignReviewers) and the two reviewers for review-side gestures.
Admin performs no operational gestures — the deployment-model
intent is now the script's default.

### Smoke verification — the clean test of the new seed

Per the plan's verification step: a fresh-bootstrap install plus
the slimmer script, walked through the full C6a + C6b gesture
set, must complete without `permission denied` errors. **The
walk completed cleanly** — no permission-denied surfaces, no
role-prompter throws, every affordance rendered where expected.
The seed shape is correct: ADR 0011's two-role default delivers
the deployment-model intent out of the box, and the smoke
script's role is now narrowed to genuine test-data
(`smokeauthor` mint + the multi-role `multireviewer` /
`secondreviewer` mint for the `SignAsRoleDialog` paths).

### What this resolves and what stays open

**Resolved:**

- The four script-grants-paper-over-seed-gap pattern instances
  catalogued at C6b stop 9 — gone, none remaining.
- The deployment-model intent (admin is IT-side, author-can-submit
  is the small-shop default) is now the seeded default.
- The C6b smoke walk's friction at first run is gone; future
  smoke walks against fresh installs don't need to paper over
  anything.

**Still open (intentional — out of scope per the plan):**

- **C3 self-assignment guard** — `DocumentLifecycleService.
  SubmitForReviewAsync` rejects the author appearing in their
  own reviewer list. Plan §1 and §C of the C6b plan claim
  "self-assignment allowed"; the C3 service guard wins for now.
  Separate workflow-semantics question; deferred to its own ADR
  or amendment per SCRATCHPAD "Author-as-self-reviewer policy".
- **ADR 0009 role-resolution corner case** — when a user holds a
  permission only via direct `UserPermission` grants (no
  role-mediated path), the `SignatureRolePrompter` finds zero
  eligible roles and throws. ADR 0011's seed shape makes the
  case practically rare (every signable permission is now in at
  least one seeded role) but the corner case itself is
  architecturally distinct; deferred to its own future ADR per
  SCRATCHPAD "ADR 0009 role-resolution vs direct UserPermission
  grants".
- **Admin UI for role / permission management** — earlier
  pressure (per ADR 0011's Consequences) but no blocking
  dependency. Organizations that want to rename `DocumentAuthor`
  or adjust seeded role permissions can do so via direct DB
  manipulation or a focused script in the interim. Admin UI was
  already on the post-Phase-2 roadmap per ADR 0008 §"Out of
  scope".

### Lessons pinned

**Seed shape vs. deployment-model intent — name the
deployment-model intent explicitly before deciding which roles
to seed.** ADR 0008's reasoning was internally consistent
("organizations should choose the policy via admin UI") but
predicated on an unstated assumption about who would be doing
the operational gestures in the interim. The deployment-model
clarification (admin is IT-side, not operational) inverted the
assumption — and once that flipped, the seed shape ADR 0008
described stopped matching reality. The lesson generalizes:
when a phase's seeded data needs to support a documented
default workflow but the workflow isn't yet wired end-to-end,
write down what the deployment model expects of the seed before
finalizing the seed. Mismatches caught at smoke-time become
friction every smoke walk re-pays; mismatches caught at
planning-time are one decision.

**Migration-applies-cleanly row-delta tests catch the kind of
seed bug that's hard to spot in review.** The new
`AddDocumentAuthorRoleAndAmendQualityManagerSeed_ProducesExpectedRowDeltasAsync`
test pins exactly six new RolePermission rows + one new Role
row + total-count = 18 for the affected roles. A future
amendment that accidentally double-writes a row (e.g., copies a
`DocumentAuthor` link out of `QualityManager` instead of just
adding it) would pass every other test in the file —
deterministic-Id checks pass, list-equivalence checks pass —
but fire this one. The pattern is worth replicating in future
seed migrations that modify existing rows or add to existing
role permission sets.

**`dotnet ef migrations add` produces the scaffold; the Up()
body is hand-written; the Designer.cs is auto-generated and
correct.** For data-only migrations (no schema change) the
Designer.cs is identical to the previous migration's model
snapshot. The workflow that worked: scaffold via `dotnet ef
migrations add <name>` from the `src/EasySynQ.Data` directory
(no startup-project needed since Data is the EF target with
both `EntityFrameworkCore.Sqlite` and `EntityFrameworkCore.Design`
package references), then overwrite the `.cs` file's empty
Up()/Down() with the actual `InsertData` calls. Build verifies
the migration compiles; the migration-seed tests verify the
seed shape; the row-delta test verifies no over- or
under-writing.

**Stable Guid suffixes echo permission Ids for visual tracing.**
The migration uses `08210000-...-01` for the DocumentAuthor →
Document.Create link, `08210000-...-09` for DocumentAuthor →
Document.HardDelete, etc., matching each permission's own
suffix. Raw-query debugging gets cheaper when the relationship
is visible in the Id alone — a maintainer reading
`SELECT * FROM RolePermissions` doesn't have to cross-reference
PermissionId values against the Permissions table to know what
the link grants. The C1 migration established the convention;
this migration extends it.

### Next intended chunk

Phase 2 C7 (lock inspector + print views) is the next chunk per
ADR 0008's commit chunking. Three items collected in SCRATCHPAD
"C7 — visual polish surfaces" share the visual-surface
character and warrant being thought about together when C7
planning happens: PDF.js viewer toolbar theming, the lock
inspector itself (every Phase 2 lockout pathway surfaces in the
inspector per SPEC §4.3), and print-friendly Document +
Revision detail renderings per SPEC §4.5. The C7 plan should
also flush the "detail view loses affordances on document
switch" follow-up surfaced at C6b stop 9 (deferred to a
follow-up commit).

The architectural reconciliation captured by this handoff is
out of the C-numbered chunk chain, so Phase 2's chunk
completion count is unchanged (six of nine implementation
commits in, 67%). The next chunk number is still C7.

Working tree clean as of this entry's commit.

---

## 2026-05-17 (Phase 2 C7) — Lock inspector + visual polish + print-friendly Document detail

Seven commits on master since the previous handoff (this docs
commit will be the eighth):

- `29da466` fix(ui): detail-view affordance refresh on switch
  (C6b follow-up — DataContextChanged fallback handler)
- `0d80219` feat(services): lock-inspector chain resolution
  (ADR 0012 / C7a)
- `6f9b321` feat(ui): lock-inspector popover + wire-up (ADR 0012
  / C7b — first cut, Window-based)
- `dae51f8` fix(ui): guard prompter Owner-assignment against
  unshown MainWindow
- `45ea494` fix(ui): lock-inspector popover as WPF Popup
  primitive (ADR 0012 — refactor from Window after smoke)
- `3f0654d` feat(ui): dark-theme polish for PDF.js toolbar +
  WPF DataGrid + ToolTip (C7c)
- `4beaf46` feat(ui): print-friendly Document detail (ADR 0008
  C7d / SPEC §4.5)

Phase 2 C7 closing chunk per ADR 0008's commit chunking. The
chunk shipped as four implementation commits (C7a–C7d) plus
three smoke-surfaced follow-up fixes (the affordance refresh,
the prompter Owner guard, and the lock-inspector popover-vs-
window refactor). Total: 48 files changed, +4,657 / −49 lines.
ADR 0012 (Lock Inspector Popover Pattern) ships fully amended
in C7b's commit; the prompter and Popup-primitive amendments
folded the design's lessons-learned into the same document
rather than scattering them across additional ADRs.

### Scope and shape

C7 was the largest single chunk in Phase 2 by surface area (the
C6a high-water from May 16 has now been overtaken). Four
discrete surfaces:

- **C7a — service layer.** `ILockReasonRepository`,
  `LockReasonRepository`, `ILockReasonResolver` interface +
  registry, `DocumentLockReasonResolver`,
  `DocumentRevisionLockReasonResolver`. Chain templates for
  L1 (Approved) / L2 (InReview) / L3 (Superseded) /
  L4 (Archived) / L5 (Retired Document) / L6 (Soft-deleted).
  Always-resolve + write-through-cache producer strategy
  (rejects the eager-write pattern that would double audit-row
  counts).
- **C7b — UI layer.** `LockInspectorViewModel` (always-resolve
  + first-inspect cache write), `LockInspectorPrompter`
  (singleton in DI), `LockInspectorPopover` UserControl hosted
  in a WPF `Popup` primitive (after the smoke-surfaced
  refactor from chromeless Window). Trigger wired to
  `DocumentDetailView`'s status pill via a clickable lock-
  glyph Button and to `DocumentListView`'s row lock-glyph
  cells via a new `DataGridTemplateColumn`. `DocumentListItem`
  extended with nullable `LockedEntityType` / `LockedEntityId`
  fields; `DocumentListViewModel.ProjectRow` computes them
  with `Document.RetiredAtUtc` taking precedence over
  revision-level lockouts (a retired Document inspects at the
  Document level so users land on the retirement signature,
  not an archived revision row).
- **C7c — visual polish.** Two scopes bundled in one commit
  per the SCRATCHPAD "C7 visual polish surfaces" entry. PDF.js
  toolbar dark-theme retune (selectors covered:
  `#toolbarContainer`, `#toolbarViewer`, `#secondaryToolbar`,
  `#findbar`, `.toolbarButton`, `.secondaryToolbarButton`,
  `.toolbarField`, `#pageNumber`, `#scaleSelect`, `#findInput`,
  `.toolbarLabel`, `#findResultsCount`) with hardcoded hex
  values cross-referenced to `Resources/Colors.xaml` tokens.
  WPF DataGrid + ToolTip implicit dark-theme styles added to
  `BaseStyles.xaml` (covers the DocumentListView's column
  header row + every tooltip in the app, including the lock-
  glyph "Why is this locked?" surface). README's bump
  checklist extended with the new PDF.js selectors.
- **C7d — print-friendly Document detail.** BCL-only
  FlowDocument + PrintDialog pipeline. Snapshot DTO + pure
  builder + thin orchestrator split: `DocumentPrintViewModel`
  (record), `DocumentPrintBuilder` (static, US Letter at 96
  DPI), `IDocumentPrintService` + `DocumentPrintService`
  (orchestrates load → build → PrintDialog). Print affordance
  on `DocumentDetailView`'s action row, no permission gate per
  ADR 0007 catalog and the C7 planning decision. Sections:
  Header (Title + Number), Metadata (revision label,
  lifecycle, dates, author), Signatures (BrushGreenBg on
  Signed rows, BrushAmberBg on Discarded), Comments (if any),
  Audit (chronological table), Footer (record IDs + attached
  PDF blob id). Metadata wrapper only — PDF content prints
  via PDF.js's own toolbar print button.

### Per-commit walkthrough

**`29da466` — affordance refresh follow-up (planned-and-shipped-
before-C7).** Per the planning synthesis's recommendation to
land this before C7's list-view lock-glyph cells, the C6b stop 9
"detail view loses affordances on document switch" bug
shipped as a focused fix. `DocumentDetailView.xaml.cs` gains a
`DataContextChanged` handler routed through a shared
`TryDispatchLoadAsync` that gates on `IsLoaded` + DataContext +
`_loadDispatchedFor` reference-equality. Fires `LoadCommand`
exactly once per (view, VM) pair regardless of which event
(`Loaded` or `DataContextChanged`) sees the satisfied
preconditions first. Smoke-validated by the user before C7
began.

**`0d80219` — C7a service-layer scaffolding.** ADR 0012 drafted
with the original design (popover as chromeless Window,
attached-behavior trigger). `LockedEntityTypes` constants
mirror the canonical type strings (`Document`,
`DocumentRevision`). Two per-type resolver classes registered
under one `ILockReasonResolver` interface; registry composes
from `IEnumerable<ILockReasonResolver>` and throws on duplicate
keys. 35 new unit + integration tests (765 → 799 total). L6
takes precedence over L1-L5 (a soft-deleted superseded
revision returns the L6 chain).

**`6f9b321` — C7b UI first cut.** ADR 0012 amended twice in
this commit: (a) staleness paragraph rewritten to the simpler
always-resolve + cache-write-once design (the original
"compare cached ModifiedUtc against live entity ModifiedUtc"
approach would have forced the VM to dependency-inject every
per-entity-type repository), (b) prompter pattern alignment
replacing the attached-behavior framing (attached behaviors
would require a static `App.Services` accessor the codebase
deliberately doesn't expose). Window-based popover with
`Deactivated` auto-close. List + detail wiring. 17 new tests
(799 → 816 total). Smoke-validated for the prior tests; the
popover smoke failed (see C7b follow-ups below).

**`dae51f8` — prompter Owner guard fix.** First smoke-walk
crash: "Cannot set Owner property to a Window that has not
been shown previously" from CreateDocumentPrompter, plus
"This Visual is not connected to a presentationSource" from
the new LockInspectorPrompter. Both came from the codebase-
wide prompter pattern `Application.Current?.MainWindow is
{ } owner` trusting MainWindow without a liveness check.
After `LoginWindow.Close()`, WPF auto-clears
`Application.MainWindow` to null; subsequent Window
constructions auto-set MainWindow to the new Window before it
is Show()'d, putting `Application.MainWindow` in an unusable
state. Patch added `PresentationSource.FromVisual(owner) is
HwndSource` to the guard in all seven prompters — the
canonical "this Hwnd is alive" check. Pre-existing latent bug
that C7b's `PointToScreen` call (which fails on closed
Windows where Owner-assignment alone only fails on never-shown
ones) made impossible to ignore.

**`45ea494` — LockInspectorPopover Window → Popup refactor.**
Second smoke-walk failure: clicking the lock glyph produced no
visible popover, but the Button's tooltip worked (so the
command WAS reaching the prompter). Root cause: a chromeless
Window (`WindowStyle=None` + `AllowsTransparency=True`)
interacts badly with WPF activation when `Show()` runs on an
async-continuation rather than the original click handler's
synchronous call stack. The OS refuses to activate the
chromeless Window because the synchronous user-input context
has cleared by the time `Show()` runs (after
`await vm.LoadAsync(...)`); `Deactivated` fires immediately
and closes the popover before the user sees it. Refactor:
`LockInspectorPopover` is now a `UserControl` hosted in a WPF
`Popup` primitive (`PlacementMode.MousePoint`, `StaysOpen=False`).
The Popup doesn't steal focus, click-outside dismissal works
via mouse-capture rather than focus events, and positioning is
independent of activation state. Third ADR 0012 amendment
captures the activation-after-await failure mode for future
maintainers.

**`3f0654d` — visual polish bundle.** PDF.js toolbar dark-theme
CSS (scope 2 of `easysynq-overrides.css`; the existing
download/open-file hide is scope 1). Hex values hardcoded with
`/* Colors.xaml ColorXxx */` cross-reference comments. README's
bump checklist extended with a grouped "Scope 1 / Scope 2"
selector inventory so a future PDF.js bump re-verifies both
override scopes consistently. WPF implicit styles added to
`BaseStyles.xaml`: `ToolTip` (retemplated around a 4px-corner
Border with Surface2 bg + Text foreground + Border line),
`DataGrid` (transparent base + Surface2 alternating rows +
Border grid lines), `DataGridColumnHeader` (retemplated;
Surface2 bg + TextDim semibold 11pt foreground + 1px bottom
Border line), `DataGridRow` (:hover lifts to Surface3,
:selected uses AccentDim — overrides Windows-blue
`SystemColors.HighlightBrushKey`), `DataGridCell` (clears
framework's per-cell selection chrome). The DataGrid +
ToolTip portion was bundled with C7c at the user's request
during smoke (the column-header row and tooltip styling were
discovered to be in the same family of "default-theme bleeds
through" concern). Tests unchanged.

**`4beaf46` — C7d print-friendly Document detail.** BCL-only
FlowDocument + PrintDialog pipeline. Snapshot DTO assembled at
print-time; pure builder constructs the FlowDocument; thin
orchestrator drives the load + dialog. US Letter at 96 DPI
(816 × 1056, 48 px page padding + 8 px block indent on bare
paragraphs so headings align with the table cells' 8 px inner
padding — added at user smoke feedback). 6 new builder unit
tests pin section sequence, page dimensions, font stack, audit
ordering, and defensive missing-author-signature handling
(816 → 822 total). `DocumentDetailViewModel` gains the
`IDocumentPrintService` dependency, `CanPrint` computed
property, and `PrintCommand`. `DocumentDetailView.xaml` adds
the Print button at the end of the affordance row. No
`Document.Print` permission gate (per the C7 planning
decision and ADR 0007 catalog discipline).

### ADR 0012 — three amendments in one commit chain

ADR 0012 (Lock Inspector Popover Pattern) shipped as part of
C7a (`0d80219`) and was amended three times before C7b stabilized:

1. **Staleness paragraph simplified** (`6f9b321`). Replaced the
   "compare cached ModifiedUtc against live entity ModifiedUtc"
   complexity with the always-resolve + write-through-on-first-
   inspect design. The cached `LockReason` row is audit
   evidence ("a lock was inspected here at this time"), not a
   render-time data source — every open renders fresh from the
   resolver.
2. **Prompter pattern alignment** (`6f9b321`). Replaced the
   "attached behavior" trigger framing with the codebase's
   established prompter pattern (`ISubmitForReviewPrompter` /
   `IReviewAndSignPrompter`). Attached behaviors would require
   a static `App.Services` accessor the codebase deliberately
   doesn't expose; the prompter pattern keeps DI explicit.
3. **Popup primitive vs chromeless Window** (`45ea494`). After
   the smoke-surfaced activation-after-await failure, the ADR's
   UI-shape paragraph + Implementation Notes were rewritten to
   document the Popup primitive choice and the failure mode
   that motivated the swap.

Three amendments in one commit chain is unusual; the rationale
in each is a concrete failure (or near-failure) observed during
implementation rather than a re-litigation of design. The
pattern works because the ADR is the load-bearing record —
future maintainers reading ADR 0012 see the final design and
the why, not just the design.

### Smoke verification — the user's column, four iterations

C7's smoke arc was the densest of Phase 2 to date. Four smoke
loops:

1. **Pre-C7 affordance fix.** Verified the
   navigate-away-and-back workaround was no longer needed.
   Clean.
2. **C7b first-cut popover smoke.** Two crashes surfaced
   (Owner-assignment and PointToScreen). Fix landed as
   `dae51f8`. Re-smoke: Create worked, but the lock popover
   still didn't appear — only the tooltip showed.
3. **C7b Popup-primitive refactor smoke.** Lock popover
   appears correctly with chain content; click-outside
   dismisses cleanly. Approved.
4. **C7c + C7d combined smoke.** PDF.js toolbar reads cleanly
   against the dark theme; DataGrid column header row +
   tooltips are legible; Print produces a clean 8.5×11 layout
   in Microsoft Print to PDF. The bundle-DataGrid-and-ToolTip-
   into-C7c decision was user-driven during smoke (they
   flagged the regressions while testing the PDF.js fix). C7d
   smoke also surfaced a small left-padding nit on bare
   paragraphs (heading text touching the page edge) — folded
   into the C7d commit before landing.

The 4-iteration smoke arc is the Phase 2 high-water for round
trips, but each iteration ended with a concrete commit and the
final result is robust. The Window-vs-Popup refactor in
particular is the kind of design choice that's hard to
diagnose without smoke evidence — the C7b first-cut compiled,
passed all 17 new tests, and worked correctly during the test-
project's static analysis. The runtime behavior only surfaced
on a real WPF dispatcher with a real Show() call following an
await. Smoke catching it was the design working as intended.

### What this resolves and what stays open

**Resolved:**

- Every Phase 2 lockout pathway (L1–L6 per ADR 0012) now
  surfaces a clickable lock-glyph affordance with a popover
  showing the causal chain (CLAUDE.md non-negotiable rule #5
  + SPEC §4.3).
- The C6b stop 9 affordance-refresh bug (the navigate-away-
  and-back workaround) is gone.
- The codebase-wide prompter Owner-assignment pattern is
  robust against the `Application.MainWindow` null-or-unshown
  state that follows the sign-in flow. Pre-existing latent
  bug closed across all seven prompters.
- PDF.js toolbar reads cleanly against the EasySynQ dark
  theme. Column header row + every app tooltip retuned to the
  palette. Default-theme bleed-through eliminated for every
  control surface where it was observed.
- Print-friendly Document detail surfaces a US-Letter
  FlowDocument with signatures, comments, and audit trail
  inlined per SPEC §4.5.

**Still open (intentional — out of C7 scope):**

- **Live navigation on lock-chain link click.** Chain links
  with non-null `NavigationEntityType` / `NavigationEntityId`
  render the navigation refs as static visual cues; clicking
  doesn't dispatch into a detail view yet. Wired when an
  `INavigationService` lands (planned post-Phase-2).
- **Revision-history print template.** The print covers the
  latest revision only. A per-revision-history print surface
  is deferred to its own future commit when a
  revision-history view exists in the UI.
- **Print of inlined PDF content.** Metadata wrapper only —
  the PDF binary prints via PDF.js's own toolbar print
  button. If pilot auditors push back on the two-job
  workflow, inlining the PDF pages into the FlowDocument is a
  dependency-flag conversation (PdfiumViewer or
  WebView2.PrintToPdfAsync) per CLAUDE.md rule 9.
- **The "Microsoft Print to PDF" path is the only smoke-
  validated destination.** Real-printer behavior is not
  exercised yet — the pilot deployment's printer fleet is the
  next reality check, likely during the pre-deployment
  hardening pass.
- **C3 self-assignment guard, ADR 0009 role-resolution corner
  case, admin UI for role/permission management.** Carried
  over from prior session-handoff entries; still applicable.

### Lessons pinned

**Chromeless Windows + async + Show = focus-stealing
refused.** The C7b smoke arc's most expensive lesson. WPF's
chromeless `Window` (`WindowStyle=None` + `AllowsTransparency=True`)
can only steal focus from a synchronous user-input context.
After an `await`, the OS refuses to activate it,
`Deactivated` fires immediately on `Show()`, and the popover
closes before it's visible. **The fix is to use the right WPF
primitive for the shape:** WPF `Popup` is purpose-built for
informational click-outside-dismissable popovers and sidesteps
the activation model entirely. The Window approach should be
reserved for blocking modal dialogs that already have focus
because the user's click activated the parent window
synchronously. Generalize: when WPF behavior surprises after
an `await` boundary, the synchronous-context-lost framing is
the first thing to check.

**`Application.MainWindow` becomes unusable after the sign-in
flow without explicit reassignment.** WPF auto-clears
`Application.MainWindow` to null when the original main window
(the LoginWindow) closes, and a subsequently-constructed
Window (e.g., a fresh dialog) auto-becomes MainWindow at
construction time — but might never be Show()'d. The
`PresentationSource.FromVisual(window) is HwndSource` check
is the canonical "this Hwnd is alive" test; the
`is { } owner && !ReferenceEquals(owner, dialog)` pattern
that prompters previously used was insufficient. This is a
project-wide pattern fix that's now uniform across all seven
prompters — apply the same guard to any future prompter that
sets `Owner` from `Application.MainWindow`.

**ADRs evolve in the same commit chain that implements them.**
ADR 0012 was amended three times across two commits before
stabilizing. Each amendment was a concrete failure observed
during implementation, not a design re-litigation. The
amend-in-place approach keeps the load-bearing record honest —
future maintainers see the final design + the why, not just
the original design + a trail of "see also" cross-references.
The cost is some commit-message verbosity (each amendment
gets called out in the commit body); the benefit is the ADR
stays the single source of truth.

**Smoke as the design verifier.** The C7b first-cut compiled,
passed every test, and looked correct in code review. The
Window-vs-Popup choice was a design decision smoke caught
that no static check could have. Per the project's smoke-
protocol-roles convention, the user drove the gestures and I
prepared/inspected; this division of labor is now load-bearing
for any feature whose correctness depends on WPF runtime
behavior (activation, focus, dispatcher ordering, hit-test).
The 4-iteration smoke arc this chunk was expensive in round-
trip time but each iteration ended with a concrete fix.

**FlowDocument paginator is lazy.** `IDocumentPaginatorSource.
DocumentPaginator.PageCount` returns 0 until
`ComputePageCount()` is called explicitly. Discovered while
writing `DocumentPrintBuilderTests`. The test that wanted to
assert "pagination produces at least one page" had to force
the computation. Future print-template tests should follow
the same pattern.

**Default WPF theme leaks through in surprising places.**
C7c's SCRATCHPAD-tagged scope was just the PDF.js toolbar;
during smoke the user identified two more surfaces (DataGrid
column headers + tooltips) that had the same default-light
appearance against the dark host theme. Both were trivially
fixed by adding implicit styles to `BaseStyles.xaml`. The
generalizable lesson: when a UI surface depends on framework-
default styling, audit every control type the surface uses
against the project's palette discipline. Implicit styles
have low blast radius (they auto-apply where no explicit
style overrides) and high payoff.

**Block-level indentation in FlowDocument is separate from
`PagePadding`.** Bare `Paragraph` blocks render at the
PagePadding edge with no inner indent; `Table` cells render at
PagePadding + cell `Padding`. The visual misalignment is
subtle but real on a printed page. The C7d small-padding fix
added an explicit `BlockIndent = 8` constant on bare
paragraphs so they visually align with the table cells'
content. Future print-template work should adopt the same
pattern.

### Next intended chunk

**Phase 2 C8 (External library + compatibility flagging)** is
the next chunk per ADR 0008's commit chunking. Scope per the
ADR:

- ExternalDocument CRUD (issuing body, designation, current
  revision label, effective date).
- DocumentLink CRUD (link an internal `DocumentRevision` to
  an `ExternalDocument`).
- Compatibility-review-required flag mechanics when an
  external revision is recorded (SPEC §5.1 "compatibility
  flagging").

Then **C9 (closing handoff)** wraps Phase 2 and gates the
move to Phase 3.

**Phase 2 milestone:** with C7 landed, Phase 2 is eight of nine
implementation commits in (89%). C8 is the last
implementation commit before C9's closing handoff; C7's
lessons-learned arc means C8 inherits a fully-styled UI with
robust prompter wiring, a working FlowDocument print pipeline,
and a lock-inspector framework ready for the new
`DocumentLink.CompatibilityReviewRequiredFlag` lockout state
to slot in (the C7a resolver registry is the extension point —
C8 either extends `DocumentRevisionLockReasonResolver` with a
new chain template for that flag, or adds a new
`DocumentLinkLockReasonResolver`, the planning conversation
will pick one).

Working tree clean as of this entry's commit.

---
