# ADR 0002 — Hard-Delete Audit-Log Behavior

**Status:** Accepted
**Date:** 2026-05-11
**Supersedes:** None
**Amends:** `docs/SPEC.md` §3.4, §3.5 (clarified in Revision 3)

---

## Context

SPEC §3.4 requires the global `AuditLog` to capture every insert, update, **and delete** on compliance-critical entities, with before/after JSON snapshots, and explicitly states the audit log is append-only.

SPEC §3.5 refines the deletion policy: a record may be **hard-deleted** by its original author *before* it carries a signature or is referenced by a signed record. After either condition is met, only soft-delete-with-reason is permitted. The §3.5 design goal is to let authors clean up typos and abandoned drafts without polluting compliance records.

These two clauses are in apparent tension when an author hard-deletes a draft. Three behaviors were on the table:

1. **Pure hard delete.** Remove the operational row; write nothing to the audit log.
2. **Hard delete in the operational sense, retained as audit evidence.** Remove the operational row; write a single audit row capturing the pre-delete state.
3. **No hard delete ever.** Convert all deletes to soft-delete-with-reason regardless of signature/reference state.

Rev 2 of the spec did not pick one. Choosing (1) leaves the database open to "create-then-delete to mask activity" — directly undermining the compliance posture the rest of the system is built to defend. Choosing (3) defeats the §3.5 design goal of letting authors clean up genuine draft noise. Choosing (2) preserves both the ergonomic value and the compliance guarantee.

## Decision

When a draft record is hard-deleted:

- The operational row is **removed** from its table.
- A single audit-log entry is written, with action type `HardDelete`.
- The audit entry's `before` field carries the **full final-state JSON snapshot** of the entity at the moment of deletion (every field, including standard audit fields such as `CreatedUtc`, `ModifiedUtc`, `RowVersion`).
- The audit entry's `after` field is `null`.
- The audit entry carries `UserId`, `UtcTimestamp`, entity type, and entity id, matching the schema used for every other AuditLog entry.

The phrase "hard delete" in this codebase therefore means: **gone from the operational table, retained as a permanent event in the append-only audit log.** Nothing about compliance-critical activity is ever silently lost.

`HardDelete` is only permitted on records still in the draft window — not signed, and not referenced by any signed record. The repository layer enforces this; calls outside that window must throw and produce no state change (no operational delete, no audit-log row).

## Alternatives Considered

### Pure hard delete (no audit row)
- **Why considered:** Simpler. Drafts genuinely vanish.
- **Why rejected:** Opens a "create + delete to scrub activity" path against the very compliance posture the system is supposed to defend. The cost saved (one audit row per draft deletion) is trivial — drafts are typo recovery, not bulk data.

### Soft-delete everything; never permit hard delete
- **Why considered:** Maximally conservative. Single deletion mode is easier to reason about.
- **Why rejected:** Defeats §3.5's design goal of letting authors clean up typos before signatures or references attach. Operational tables would accumulate noise from every abandoned draft indefinitely, which degrades both UI list density and query plans. The draft/compliance-bound boundary is the right place to flip behavior; a blanket "no hard deletes" treats every typo as compliance evidence.

### Hard delete writes a tombstone row in the operational table
- **Why considered:** Some systems use tombstones for replication semantics or for query consistency.
- **Why rejected:** SQLite + WAL on a single shared file (or Local Service Mode with a single owner) has no replication need that benefits. A tombstone in the operational table contradicts what "hard delete" means and would force every list query to add an `IsDeleted = 0` filter, re-introducing the noise we set out to avoid. The audit log is the right place for the permanent event record.

## Consequences

### Positive
- Tamper-evidence is preserved end-to-end: there is no class of compliance-critical CRUD activity that leaves no trail.
- Authors retain the ergonomic value of being able to clean up draft mistakes without permanently polluting compliance records.
- The `AuditLog` schema is unchanged — `before` and `after` were already designed to carry JSON snapshots; `HardDelete` simply uses them with `after = null`.
- Forensic queries against the `AuditLog` can reconstruct any draft that ever existed, including ones that were deleted. The "war room" and customer-export packages remain trustworthy because every state transition (including deletions) is reconstructable.

### Negative (and accepted)
- **Storage cost:** one `AuditLog` row per draft deletion. With expected volume (low — drafts are typo recovery) this is negligible relative to inspection-reading and chart-import volume.
- **Apparent contradiction:** "we don't have hard deletes; we just delete the row but keep the event" reads as semantic sleight of hand. We accept this trade-off because the alternative interpretations (genuinely lose the event, or never delete the row) both produce worse outcomes. The spec's Revision 3 wording makes the meaning explicit.
- This ADR does not protect against an attacker with direct AuditLog write/delete access. The spec already requires the `AuditLog` to be append-only and bypass-resistant at the data layer (§3.4); this ADR depends on that invariant and does not introduce a new attack surface.

## Implementation Notes

- The repository interface for compliance-critical entities exposes `HardDeleteAsync(entityId, userId)` **only** for records still in the draft window. Calling `HardDeleteAsync` on a signed-or-referenced record must throw a domain exception, with no state change.
- The `HardDelete` audit row must be written in the **same transaction** as the operational row removal. A failed audit-row write rolls back the deletion. Tests must verify atomicity (audit failure → operational row still present).
- The JSON snapshot serialization must be stable across schema migrations — i.e., serialize whatever fields exist at delete time, not against a frozen schema. Audit rows from before a migration may have a different shape than rows after; that is acceptable and expected. The `entityType` column is the discriminator.
- `before` snapshots may contain sensitive content (user-entered notes, draft signatures-in-progress). Access to historical `before` payloads must obey the same role-based authorization as access to the original record type.
- **Tests required:**
  - Hard-deleting a draft produces exactly one `AuditLog` row with `action = HardDelete`, non-null `before`, null `after`.
  - Attempting to hard-delete a signed record throws; no rows change.
  - Attempting to hard-delete a record referenced by a signed record throws; no rows change.
  - Transaction atomicity: induced audit-write failure rolls back the operational delete.
  - The `before` JSON can be deserialized back into the original entity shape (round-trip).

## References

- `docs/SPEC.md` §3.4 — Audit Trail
- `docs/SPEC.md` §3.5 — Deletion Policy (clarified in Rev 3)
- ADR 0001 — Stack Choice (establishes the append-only `AuditLog` as a foundational invariant)
