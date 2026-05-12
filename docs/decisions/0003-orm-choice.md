# ADR 0003 — ORM Choice: EF Core for EasySynQ

**Status:** Accepted
**Date:** 2026-05-11
**Supersedes:** None
**Amends:** ADR 0001 (which deferred this decision)

---

## Context

ADR 0001 left the ORM choice open: "Entity Framework Core OR Dapper. Pick one at Phase 1 and stay consistent." This ADR closes that gap before Phase 1 implementation begins.

The decision is not a generic "EF vs Dapper" comparison. It is "for the specific workloads EasySynQ produces, which is the better fit?" The relevant workloads are:

- **Audit-log writes on every CUD.** SPEC §3.4 requires before/after JSON snapshots for every insert/update/delete on compliance-critical entities, written in the same transaction as the operational write. ADR 0002 reinforces this for the hard-delete case.
- **Effective-dated reads.** SPEC §3.7 requires every query against configuration-bearing entities to default to "as-of the event timestamp," not "as-of now." Multiple entity types share this temporal pattern (Part Master tolerances, calibration schedules, PM intervals, quality objective targets, recipe parameters).
- **Lock-chain traversal.** SPEC §4.3 requires building a navigable causal chain on demand for any lockout. The chain spans 3–6 entity hops typically (`Job → NCR → Reading → Part Master Rev → Approval Signature`).
- **Repository interface for Local Service Mode swap.** SPEC §3.1 and §3.2 require the data layer to sit behind a mode-agnostic interface so the Shared File Mode → Local Service Mode swap is configuration-driven, not a rewrite.
- **Schema versioning across shipped releases.** MSIX/ClickOnce installs require ordered, scripted schema migrations between versions.
- **Concurrency profile.** 4–8 users, inspection readings entered at human speed, a handful of chart imports per shift. Not OLTP-extreme; correctness-bound, not throughput-bound.

## Decision

**Adopt EF Core 10 as the single ORM.** No Dapper.

The repository interface (`IEasySynQRepository<T>` and friends) sits between the Services layer and EF Core's `DbContext`. The interface stays ORM-agnostic; the EF Core implementation lives inside `EasySynQ.Data`.

## Why EF Core wins for *these* workloads

1. **Audit-log integration comes free.** EF Core's `SaveChangesInterceptor` surfaces the change tracker's before/after entity state for every entity touched in a transaction. We serialize that to JSON and write one `AuditLogEntry` row per modified entity, in the same transaction. With Dapper we would hand-write `SELECT old FROM ... WHERE id = @id` before every UPDATE, then build the before snapshot manually. Dozens of code paths, one chance to forget — and forgetting is a compliance failure, not just a bug.

2. **Effective-dating becomes declarative.** EF Core's `HasQueryFilter` registers a global filter on every effective-dated entity:
   ```csharp
   modelBuilder.Entity<PartMasterRevision>()
       .HasQueryFilter(e => e.EffectiveFromUtc <= _asOf
                         && (e.EffectiveToUtc == null || e.EffectiveToUtc > _asOf));
   ```
   The `_asOf` is injected per-DbContext-scope by a `TemporalResolver` service that reads the active event timestamp. Forgetting an "as-of" becomes structurally impossible. With Dapper we would repeat this WHERE clause in every query — and the first one we miss silently re-grades a historical job against current rules.

3. **Lock-chain traversal is composable.** Building the §4.3 causal chain means walking navigation properties: `Job.NCRs`, `NCR.SourceReading`, `Reading.PartMasterRevision`, `PartMasterRevision.ApprovingSignature`. EF Core's `Include`/`ThenInclude` with projection to a chain DTO is the natural shape. Dapper would require hand-written multi-mapping joins or N+1 round-trips.

4. **Migrations come in the box.** EF Core's migration tooling generates ordered `_xxxxxx_Description.cs` files, supports up/down, and integrates with the build. SQLite has known limitations around schema alteration (no ALTER COLUMN, no DROP COLUMN pre-3.35) but EF Core 10 handles those with table-rebuild scripts. Dapper would need DbUp or FluentMigrator — another dependency, another learning surface, another tooling story to teach to a customer that has no IT staff.

5. **Standard-field discipline scales.** Every signable entity has `CreatedBy`, `CreatedUtc`, `ModifiedBy`, `ModifiedUtc`, `RowVersion`, `IsDeleted`, `LockedAtUtc`. EF Core's `SaveChangesInterceptor` sets these consistently regardless of how the entity was modified. With Dapper we would rely on every call site remembering to set them.

6. **Repository-interface design isn't an ORM constraint.** Either ORM sits cleanly behind a repository interface. The "swap to Local Service Mode" story is about *where the data lives* (local file vs. gRPC to a service), not *what ORM accesses it*. EF Core does not lose us flexibility here.

## Why the typical "go Dapper" arguments don't apply

- **"Dapper is faster on hot paths."** The hot path is inspection-reading entry — a single INSERT per keystroke-locked save, against a SQLite WAL connection on a local network drive. EF Core's per-call overhead (microseconds) is dwarfed by SQLite write latency over SMB (milliseconds). We are not throughput-bound; we are correctness-bound.

- **"Dapper gives direct SQL control."** When EF Core's generated SQL is a problem (rare in this workload), `FromSqlRaw` / `ExecuteSqlRaw` give the same control without a second ORM. We can profile at Phase 11 (Hardening) and substitute raw SQL for the specific queries that need it, without bringing in Dapper.

- **"Dapper avoids change-tracking surprises."** Change tracking is the feature we *want* here — it is the mechanism that makes the audit-log story honest. Turning off tracking with `AsNoTracking()` for read-only queries gives us back any performance we cede.

## Alternatives Considered

### Dapper alone
- **Why considered:** Lighter, faster, more SQL-explicit.
- **Why rejected:** Loses the audit-log + effective-dating + standard-field automation we get for free with EF Core's interceptors and query filters. Adds a separate migrations-tool dependency. Saves nothing on a workload that is not throughput-bound.

### EF Core + Dapper hybrid (EF for writes, Dapper for read queries)
- **Why considered:** "Best of both worlds."
- **Why rejected:** Two ORMs means two mental models, two query languages, two sets of bugs. The audit-log interceptor only fires for EF Core writes — if a Dapper write happens, the audit trail has a hole. Premature optimization for a workload that does not need it.

### Postpone the choice
- **Why considered:** "Decide once we have profiling data."
- **Why rejected:** Phase 1 builds the auth + audit + signature foundation every later phase depends on. The audit interceptor *is* the foundation of compliance defensibility — postponing the ORM choice means postponing or rewriting that interceptor.

## Consequences

### Positive
- Audit-log discipline becomes a property of the data layer, not a call-site responsibility.
- Effective-dating becomes a query-filter property, not a query-author responsibility.
- Migrations ship with the product without an extra tooling layer.
- One ORM, one mental model, one set of conventions to test.

### Negative (and accepted)
- EF Core change tracking has nontrivial memory cost per `DbContext` scope. Mitigated with short-lived `DbContext`s (per UI operation) and `AsNoTracking()` on read-only paths.
- EF Core's SQLite provider has known limitations around schema alteration; migrations on existing data may require table rebuilds. Acceptable — migrations are infrequent and run at install time.
- Future maintainers need to know EF Core. Not a stretch in 2026.

## Implementation Notes

- **Audit interceptor:** Implemented as `AuditSaveChangesInterceptor : ISaveChangesInterceptor`. Reads `ChangeTracker.Entries()` before SaveChanges, captures `OriginalValues` and `CurrentValues` as JSON via `System.Text.Json`, emits one `AuditLogEntry` per touched entity. For deletes, the captured snapshot is the `before`; for `HardDelete` (per ADR 0002), `after` is `null`.
- **Effective-dating:** `TemporalResolver` is a scoped service. `DbContext` reads it during model-building filter setup. UI/Services set the resolver's `AsOf` to the event timestamp before opening a context for a historical query, defaulting to "now" otherwise.
- **Repository interface:** `IEasySynQRepository<T>` exposes `IQueryable<T>` for composition, but call sites are encouraged to use higher-level domain-specific repositories that return materialized DTOs.
- **Tests:** Integration tests use a temp SQLite file (not in-memory provider — its semantics differ from real SQLite enough to be misleading for transaction tests).

## References

- `docs/SPEC.md` §3.4 (Audit Trail), §3.5 (Deletion Policy), §3.7 (Effective Dating), §4.3 (Lock Inspector)
- ADR 0001 (defers ORM choice)
- ADR 0002 (hard-delete audit-log invariant — relies on SaveChanges-time interception)
