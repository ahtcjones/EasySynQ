# ADR 0004 — Foreign Key Policy

**Status:** Accepted
**Date:** 2026-05-12
**Supersedes:** None

---

## Context

EF Core makes it trivial to add a foreign-key constraint between any two entities — declare a navigation property, call `HasOne(...).WithMany(...).HasForeignKey(...)`, and the migration emits a FOREIGN KEY clause. The question is not *how* but *when*.

Two SPEC-driven pressures push against blanket FKs across the EasySynQ schema:

1. **Plugin-style industry modules (SPEC §12).** The base ISO 9001 product must support optional modules — AS9100, AMS 2750, IATF 16949, CQI-9. Modules add entities that reference base entities (e.g., AMS 2750 adds pyrometry fields linked to assets); they may also augment workflows. The base product must **never** reference module entities. If we declared a hard FK from a base entity to a module-specific entity, disabling the module would either drop the FK (loss of integrity) or block the disable (locking the product to a specific module set). SPEC §12 explicitly states: "The base product must never reference module-specific code; modules reference base interfaces." A FK is a base-schema reference to module-schema, which violates that rule.

2. **Phase-by-phase delivery (SPEC §9 deployment roadmap).** Phase 1 defines `User`, `Role`, `AuditLogEntry`. Phase 7 defines `Job`. If we declared a FK from `Job.OperatorId → Users.Id`, then `Job` schema depends on `User` schema — any User schema change ripples to Job, and the Job module cannot be developed independently of the Identity module. The spec assumes each phase ships meaningful schema additions without revisiting earlier phases' tables.

Aggregate roots (DDD lingo) provide a natural partition. Inside an aggregate, FKs are appropriate: the data is co-owned, co-evolved, and co-deployed. Across aggregates, soft references (a `Guid` field on the referencing entity that holds the target id without a DB-level constraint) preserve independence.

SPEC §5's module specifications confirm that each numbered module (5.1 Document Controller, 5.2 Asset & Equipment Management, 5.3 Production Control, ...) is its own aggregate-shaped concern. They reference one another — Production references Documents, Assets, Material Lots; NCR references Jobs — but the spec treats those as cross-module references resolved at the service layer, not at the schema level.

## Decision

**Foreign keys are allowed**:

- **Within an aggregate.** Owned types (`OwnsOne` / `OwnsMany`) and tightly-bound child entities of a single aggregate root. The owned `LockReasonLink` collection on `LockReason` is the canonical example — although ours is mapped as JSON-in-column rather than as a child table, the principle is the same.
- **Between tightly-coupled identity entities** within the same bounded context — specifically `UserRole.UserId → Users.Id` and `UserRole.RoleId → Roles.Id`. Identity is foundational infrastructure; the User, Role, and UserRole entities are co-evolved, ship together as part of Phase 1, and never become optional. The integrity guarantee a FK provides is worth more than the modest flexibility cost.

**Foreign keys are forbidden**:

- **Across aggregate roots from different bounded contexts.** Examples (all to be implemented later, listed here so the rule is concrete):
  - `Job.OperatorId → Users.Id` — Operations → Identity. Soft reference.
  - `Job.PartMasterId → PartMasters.Id` — Operations → Documents. Soft reference.
  - `AuditLogEntry.UserId → Users.Id` — audit is intentionally self-contained per SPEC §3.4; the log spans every entity that has ever existed, including soft-deleted users and entities authored under since-disabled modules. A FK would shrink the log's domain.
  - `NCR.SourceJobId → Jobs.Id` — cross-aggregate (Quality → Operations). Soft reference.
  - Anything Module 5.x adds that points at a base entity, when treating modules as plugins (SPEC §12). The module's own internal entities may FK to one another; their references to base must be soft.

The forbidden cases use **soft references**: a `Guid` field on the referencing entity holding the target id without a DB-level constraint. Referential integrity is enforced at the service layer (the repository that persists the referencing entity validates target presence), not at the data layer. Cross-aggregate joins use explicit query composition, not navigation properties.

## What changes for `UserRole`

`UserRole.User` and `UserRole.Role` navigation properties are added with FK constraints. `OnDelete(DeleteBehavior.Restrict)` is configured so:

- **Hard-deleting** a `User` or `Role` is blocked while any `UserRole` references it. Forces orphan handling to be explicit (the service layer must first reassign or delete the UserRoles).
- **Soft-deleting** (flipping `IsDeleted = true`) does not remove the row, so the FK continues to validate against table presence. A new `UserRole` against a soft-deleted user *succeeds at the schema level* — the service layer is the right place to refuse it on business grounds.

**No inverse navigations on `User` or `Role`.** A `User` does not expose `ICollection<UserRole>` and a `Role` does not expose `ICollection<UserRole>`. The relationship is owned by the `UserRole` side. Iterating "roles per user" is a service-layer query, not an aggregate-level property.

## What does NOT change

`AuditLogEntry.UserId`, `Signature.CreatedBy` (inherited string user id), `LockReason.LockedEntityId`, `Snapshot.*` references — all stay as soft `Guid` / string references. The audit log spans every entity type that ever existed, including module entities and hard-deleted entities; a FK would be an architectural mistake.

## Alternatives Considered

### Universal FKs everywhere
- **Why considered:** Maximum referential integrity. EF Core's defaults encourage this; navigation properties are ergonomic.
- **Why rejected:** Locks plugin modules (SPEC §12) to a specific base schema. Forces every cross-aggregate query to navigate FK chains. Cascade-delete behavior compounds the lock-in — soft-deleting a user could cascade through every dependent module's tables, defeating the §3.5 soft-delete invariant.

### No FKs anywhere
- **Why considered:** Maximum aggregate independence. Pure soft-reference architecture is conceptually clean.
- **Why rejected:** Loses the integrity guarantee where it's safe to have it (Identity, owned types). Pure soft-reference patterns put more burden on service-layer validation and the audit log to keep state coherent. Identity in particular benefits from DB-level integrity: a UserRole that references a nonexistent UserId is unambiguously a bug, not a state machine to model.

### FKs everywhere within the same bounded context, soft refs across
- **Why considered:** Middle ground sounds principled.
- **Why rejected:** "Bounded context" is fuzzy in this product. Identity and Audit are both "infrastructure" but Audit explicitly does not get FKs to anything (per SPEC §3.4). The line "tightly-coupled identity entities only" matches the actual delivery and module-extensibility constraints better than a bounded-context-wide rule.

## Consequences

### Positive

- **Phase-by-phase schema stability.** Phase 1's schema does not need revisiting when Phase 7's Job entity lands — Job will hold a soft `Guid OperatorId`, not a FK. No migration churn on earlier tables.
- **Module pluggability (SPEC §12).** AS9100, AMS 2750, and friends can declare their own entities with their own internal FKs and reference base entities via soft `Guid`s. Disabling a module hides UI but never breaks base schema integrity.
- **Audit log self-containment (SPEC §3.4).** The log can reference users, jobs, NCRs from any era, including from disabled modules. No FK ties make it brittle.
- **Identity correctness.** `UserRole` rows that reference nonexistent users or roles are impossible at the DB level. Catches bugs at insert time, not at first navigation.

### Negative (and accepted)

- **Soft references put referential integrity on the service layer.** Every persistence point that uses a soft reference must validate target existence. This is explicit work, not implicit. The audit-log integration tests required by ADR 0002 (every CUD writes a matching entry) overlap with this — the audit interceptor's `Before` snapshots capture target ids, so audit trails can prove soft-ref integrity was checked at the time of write.
- **Identity FK migration is one-way.** Reverting `UserRole`'s FKs to soft-reference style would require dropping constraints. Doable but tedious. We accept this for the integrity benefit.
- **The "tightly-coupled identity" line is ours to maintain.** Future entities that want FK inclusion must argue the case on the merits: co-evolution, co-delivery, infrastructural rather than domain-aggregate role. The default for any new entity-pair is soft reference; FKs require explicit justification.

## Implementation Notes

- The Phase 1 `UserRoleConfiguration` declares both FKs with `OnDelete.Restrict`. No inverse navigations are configured (`WithMany()` with no parameter).
- Indexes on `UserId` and `RoleId` already exist for query performance; the FK declaration reuses them rather than creating duplicates.
- Tests in `IdentityForeignKeyTests` verify: (a) insertion with nonexistent user/role fails at SaveChanges; (b) soft-deleting a user does not cascade to UserRoles; (c) the FK validates against table presence regardless of soft-delete state, so a new UserRole against a soft-deleted user still succeeds at the schema level.

## References

- `docs/SPEC.md` §3.4 — Audit Trail (intentionally self-contained)
- `docs/SPEC.md` §3.5 — Deletion Policy (soft-delete semantics that motivate `Restrict` over `Cascade`)
- `docs/SPEC.md` §5 — Module Specifications (each module is an aggregate boundary)
- `docs/SPEC.md` §9 — Deployment Roadmap (phase-by-phase delivery requires aggregate independence)
- `docs/SPEC.md` §12 — Industry Compliance Modules (plugin-style enable/disable; base must not reference modules)
- ADR 0001 — Stack Choice (chose EF Core; this ADR scopes how it's used)
- ADR 0003 — ORM Choice (chose EF Core single-ORM; this ADR pins one design rule)
