# ADR 0012 — Lock Inspector Popover Pattern and Lazy Chain Resolution

**Status:** Accepted
**Date:** 2026-05-17 (Proposed), 2026-05-17 (Accepted)
**Supersedes:** None
**Related:** SPEC §4.3 (The "Why Is This Locked?" Inspector); SPEC §3.5 (Soft-delete boundary, source of several lockout states); ADR 0007 (permission catalog — the inspector itself has no permission gate); ADR 0008 (Phase 2 scope — every Phase 2 lockout pathway must surface in the inspector per C7); ADR 0011 (the deployment-model framing the inspector adopts — admin is IT-side, not operational, so the inspector serves operational users who need to know why an entity is locked).

---

## Context

SPEC §4.3 specifies a "Why Is This Locked?" inspector: every red banner, lock icon, or OOS indicator in the UI must be clickable and surface the causal chain explaining the lock. Phase 1 introduced the data shape — the `LockReason` entity (`src/EasySynQ.Domain/Entities/Audit/LockReason.cs`) carries `LockedEntityType` + `LockedEntityId` + an ordered list of `LockReasonLink` value objects, persisted as JSON-in-column via `OwnsMany(...).ToJson()`. Chain validation is enforced by `LockReason`'s constructor: at least one link, exactly one terminal link in the last position, every non-terminal link has a non-null `Because` connector. Eighty-seven domain unit tests pin those invariants.

What Phase 1 did not introduce: a repository surface to read/write `LockReason` rows, a producer that constructs chains from live entity state, and a UI control to render them. Phase 2 C7 is where those land. ADR 0012 captures the design choices for the producer + UI shape so the C7a + C7b commits implement against a written contract rather than against ambient assumptions.

Three choices in this design are non-obvious and worth recording explicitly:

1. **The UI is a popover, not a drawer or a modal.** `docs/UI_PROTOTYPE.html` contains no inspector affordance to mirror — this is a new pattern. The drawer surface is reserved for Pulse (SPEC §4.2); the modal surface is reserved for blocking actions (signing, confirmations). The inspector belongs in neither.

2. **Chain production is lazy with a write-through cache, not eager at lock transition.** The naive reading of "every lockout state populates a LockReason chain" (SPEC §4.3, CLAUDE.md non-negotiable rule #5) is to write a `LockReason` row at every lock-transition site in the lifecycle service. That doubles the audit-row count of every Phase 2 lockout transition and bakes a UI-rendering concern into the lifecycle service.

3. **The producer is a per-entity-type resolver service, not a single resolver class.** Phase 2 surfaces lockouts on `Document` and `DocumentRevision`. Phase 3+ will surface lockouts on `Asset` (OOS calibration), `Job` (NCR-held), `Operator` (expired qualification), and others. The resolver shape needs to extend without phase boundaries forcing a god-class.

This ADR makes those three choices deliberate.

## Decision

### Popover-style inspector control, anchored to the click target

The inspector renders as a chromeless floating window styled to look and behave like a popover — `WindowStyle=None`, `AllowsTransparency=True`, `ShowInTaskbar=False`, `Topmost=True`, positioned at the click coordinates, and auto-closed on the `Deactivated` event (the user clicks away). The visual treatment matches the existing C6a viewer-error banner style at `DocumentDetailView.xaml:138-151` — `BrushSurface2` background, `BrushBorder` 1px, 6px corner radius. The header reads "Why is this locked?" using the existing `TextStyleSubheading` token. The body is an `ItemsControl` over the chain links; each link card renders the `Tag` chip in purple (per SPEC §4.4 — purple is the cross-cutting linkage accent, the right reuse here), the `Detail` text, and (when `NavigationEntityType` is non-null) a navigation indicator (live click-through deferred to a later commit when an `INavigationService` lands). Between non-terminal rows, the `Because` connector renders in muted text (`BrushTextDim`) as `↳ because <Because>`. The terminal row gets a small "root cause" affordance.

The trigger follows the existing **prompter pattern** in the codebase (`ISubmitForReviewPrompter`, `IReviewAndSignPrompter`). A new `ILockInspectorPrompter` (singleton service) wraps inspector construction and presentation — `OpenAsync(string lockedEntityType, string lockedEntityId, CancellationToken)`. View models that want to surface the inspector (`DocumentDetailViewModel`, `DocumentListViewModel`) inject the prompter and expose `RelayCommand`s that delegate to it. XAML wires the status pill and list-view lock-glyph cells as `Button`s with `Command` bound to those commands, matching how the existing affordance row at `DocumentDetailView.xaml:105-132` triggers `EditMetadataCommand` / `SubmitForReviewCommand` / etc. No attached behaviors are introduced — they would require a static service accessor (which the codebase deliberately does not expose) or DI plumbing the prompter pattern already provides.

Reasoning against the two alternatives:

- **Drawer.** SPEC §4.2 reserves the drawer pattern for Pulse (the system's "all currently-locked / OOS / red items" panel). The inspector is asking the inverse question: "what is the causal chain for *this specific* item?" A drawer pulled from the topbar to answer a per-row question puts the user's attention far from the affordance they clicked. Popover-at-click anchors the explanation to the cause.
- **Modal.** Modals block. The inspector is informational — the user opens it, reads, dismisses, and continues working. A modal would force a "dismiss me" interaction for every glance. Modals also obscure the parent context the chain is explaining ("which document was I looking at?"). Popover-stays-open-during-glance solves both.

The popover renders against the existing dark theme tokens (`Resources/Colors.xaml`); no new brushes are introduced. CLAUDE.md UI Conformance discipline applies — red lock glyph paired with the popover-open affordance, purple chip for linkage tags, no color-only signaling.

### Lazy resolution with write-through cache

A new `ILockReasonResolver` service produces `LockReason` instances from live entity state. The lifecycle service is unchanged — it does not write `LockReason` rows. When the inspector opens for `(LockedEntityType, LockedEntityId)`:

1. The inspector view-model first asks `ILockReasonRepository.GetByLockedEntityAsync(...)`. If a cached row exists, it is rendered immediately.
2. If no cached row exists, the VM invokes the type-keyed `ILockReasonResolver` for `LockedEntityType`. The resolver walks the live entity state and constructs a fresh `LockReason`. The VM persists the constructed row via `ILockReasonRepository.AddAsync(...)` + `SaveChangesAsync` (write-through), then renders it.
3. If the entity is not currently in a locked state, the resolver returns `null`. The VM surfaces "not locked" and does not persist a row.

The VM always invokes the resolver on each open and renders the fresh chain — same live state always yields the same rendered chain (the resolver is a pure function over entity state). The cache check (`GetByLockedEntityAsync`) is used only to decide whether to write through: first inspect persists a `LockReason` row; subsequent inspects skip the write. The cached row's role is audit evidence — "a lock was inspected here at this time" — not a render-time data source. This avoids the staleness-comparison complexity entirely (comparing cached `ModifiedUtc` against live entity `ModifiedUtc` requires the VM to dependency-inject every per-entity-type repository and couples C7b's UI to the resolver registry's extensibility model) and keeps the read path's behavior deterministic. The resolver's queries are themselves indexed (the C7a resolvers issue 1-2 lookups per open against the same indexes the inspector already uses); per-open cost is well below the cost of stale-chain confusion.

Reasoning against eager writes:

- Every Phase 2 lockout transition already writes audit rows. Adding a `LockReason` row insertion to every `SaveChangesAsync` scope at `DocumentLifecycleService` lines 160, 200, 291, 335, 548 would bloat the lifecycle service with rendering-shape concerns and double the audit-row count on every transition.
- The append-only audit log is the durable evidence per CLAUDE.md non-negotiable rule #6; `LockReason` rows are derived views over the audit log + live entity state. Treating them as a cache is the honest model.
- For Phase 2's pilot scale, the resolver-on-demand path adds at most 2–3 indexed queries per inspector open. Negligible.

The "every lockout state populates a LockReason chain" rule (CLAUDE.md non-negotiable #5) is satisfied by-construction at first-inspect: any inspector open on a locked entity produces a chain. The rule does not require the row to exist before someone asks for it.

### Per-entity-type resolver registry

`ILockReasonResolver` is keyed by `LockedEntityType` (the same string used in the `LockReason.LockedEntityType` column). DI registration is via a thin registry pattern:

```csharp
public interface ILockReasonResolver
{
    /// <summary>
    /// Returns the LockedEntityType this resolver handles (e.g.,
    /// "Document", "DocumentRevision", "Asset", "Job"). Used by the
    /// registry to dispatch (lockedEntityType, lockedEntityId)
    /// lookups to the correct resolver.
    /// </summary>
    string LockedEntityType { get; }

    Task<LockReason?> ResolveAsync(
        string lockedEntityId,
        CancellationToken cancellationToken);
}

public interface ILockReasonResolverRegistry
{
    ILockReasonResolver? GetResolver(string lockedEntityType);
}
```

`ILockReasonResolverRegistry` is a one-implementation interface backed by a `Dictionary<string, ILockReasonResolver>` keyed by each resolver's `LockedEntityType`. The registry is registered once at the host level; each phase registers its own resolver implementations into DI and the registry composes from `IEnumerable<ILockReasonResolver>`.

Phase 2 registers **two resolver classes**: `DocumentLockReasonResolver` for `"Document"` lockouts and `DocumentRevisionLockReasonResolver` for `"DocumentRevision"` lockouts. Each class is single-responsibility — one entity type, one set of chain templates — and registered independently as `ILockReasonResolver`. Chain-building helpers shared across both classes live as a thin internal static (`LockReasonChainHelpers` if non-trivial; inline if small). Phase 3+ resolvers (Asset / Job / Operator) slot in via additional registrations without disturbing C7a's code.

### Chain templates for Phase 2

C7a's `DocumentLockReasonResolver` implements six lockout states (L1–L6 from the C7 planning report). L7 (DocumentReviewAssignment in `Discarded` state) is not a primary lockout — the discarded status is rendered directly in the assignment row's status badge and does not need a lock-inspector trigger. The resolver's chain templates:

| Lock state | Trigger condition | Chain shape |
| --- | --- | --- |
| **L1: Approved** | `DocumentRevision.Lifecycle == Approved` | One-link terminal: `[DocumentRevision "<label>"]` — "Approved on `ApprovedAtUtc`; all reviewer signatures captured." |
| **L2: InReview** | `DocumentRevision.Lifecycle == InReview` | One-link terminal: `[DocumentRevision "<label>"]` — "In Review since `LockedAtUtc`; waiting on reviewer signatures." |
| **L3: Superseded** | `DocumentRevision.Lifecycle == Superseded` | Two-link: `[DocumentRevision "<label>"]` → because superseded by → `[DocumentRevision "<successor label>"]` (terminal; successor is the most recently-approved sibling revision). |
| **L4: Archived** | `DocumentRevision.Lifecycle == Archived` | Two-link: `[DocumentRevision "<label>"]` → because parent Document was retired → `[Document "<Number>"]` (terminal; "Retired on `RetiredAtUtc`"). |
| **L5: Retired Document** | `Document.RetiredAtUtc` non-null | One-link terminal: `[Document "<Number>"]` — "Retired on `RetiredAtUtc` by user `RetiredByUserId`." |
| **L6: Soft-deleted** | `Document.IsDeleted` or `DocumentRevision.IsDeleted` | One-link terminal: `[<EntityType> "<identifier>"]` — "Soft-deleted on `ModifiedUtc` by `ModifiedBy`." |

L6 takes precedence over L1–L5: a soft-deleted row's primary lock cause is the soft-delete itself, not the lifecycle state it had when deleted. Chain templates compose: a soft-deleted, superseded revision returns the L6 chain; the L3 framing is irrelevant once the row is administratively removed.

`Draft` lifecycle state is not a lockout — the revision is fully editable by its author. The resolver returns `null` for `DocumentRevision` rows in `Draft`, signaling "not locked."

The chain text uses the entity's natural display identifier (Document.Number, RevisionLabel) as the link `Id`; the `NavigationEntityType` / `NavigationEntityId` fields point at downstream entities (parent Document, successor revision) where C7b's popover offers click-through. Terminal links to non-navigable targets (signature ids, user ids) leave `NavigationEntityType` null — C7b's popover renders those rows as plain text, not hyperlinks.

### `LockedEntityType` string constants

A new `LockedEntityTypes` static class in `EasySynQ.Domain` mirrors the canonical strings used by `LockReason.LockedEntityType` and by `ILockReasonResolver.LockedEntityType`:

```csharp
public static class LockedEntityTypes
{
    public const string Document = "Document";
    public const string DocumentRevision = "DocumentRevision";
}
```

Phase 3+ adds entries here as new entity types are surfaced. Raw string literals are forbidden outside this class and the resolver's `LockedEntityType` property; enforcement is by code review.

### Inspector has no permission gate

Per SPEC §4.3 and the deployment-model framing of ADR 0011, anyone who can see the locked entity can ask why it is locked. No `Inspector.View` permission is added to the catalog. The popover surfaces what the user can already see through the normal UI — the lock state is visible in the lock glyph and status pill on the affordance the user clicked. Adding a permission gate to the inspector would invent a need (read-something-you-can-already-see) the spec does not describe.

## Alternatives Considered

### Eager write — `LockReason` row created at every lock-transition

Every `DocumentLifecycleService` transition that locks an entity writes a `LockReason` row in the same `SaveChangesAsync` scope as the state change. Pros: the row exists before anyone asks, no resolver service is needed, the §4.3 "every lockout state populates a LockReason chain" rule is satisfied at write time. Rejected because:

1. It doubles the audit-row count of every Phase 2 lockout transition (the `LockReason` row is itself an `AuditableEntity` with its own audit-log entry plus the chain links' JSON column changes).
2. It bakes a UI-rendering concern (chain construction) into the lifecycle service, which today is concerned only with state-machine semantics.
3. Eager rows go stale when the underlying state advances (an Approved revision becomes Superseded — the old chain is wrong). Eager writes would require eager updates on every subsequent transition, multiplying the surface area.

The resolver-on-demand path costs at most 2–3 indexed queries per inspector open — well below the cost the eager-write path imposes on every save.

### One resolver class for everything, branching on entity type internally

A single `LockReasonResolver` service with a giant `switch (lockedEntityType)` inside. Simpler DI registration. Rejected because each phase's resolver depends on phase-specific repositories — Phase 2's resolver depends on `IDocumentRepository` + `IDocumentRevisionRepository`; Phase 5's eventual `AssetLockReasonResolver` depends on `IAssetRepository`. A single class would either become a god-class with every phase's dependency injected, or accept `IServiceProvider` and service-locate at runtime — both anti-patterns. The per-type registry pattern keeps each resolver's dependencies localized.

### Strongly-typed `ILockReasonResolver<TEntity>` generic

`ILockReasonResolver<Document>`, `ILockReasonResolver<DocumentRevision>`, etc. Type-safe; no string keys. Rejected because the inspector consumes `(LockedEntityType, LockedEntityId)` as strings (from the `LockReason` column shape, from URL parameters, from click-through chain navigation) — the conversion from "DocumentRevision" string to `typeof(DocumentRevision)` would be the same registry the per-type service ends up being, with extra ceremony. The string-keyed registry is the honest shape for the consumer side.

### Inspector as a modal dialog

`Window`-based modal anchored center-screen. Predictable; matches the existing signing-dialog pattern. Rejected per the popover reasoning above — the inspector is informational and per-row; modals block and obscure the parent context. Modal would force a "dismiss me" interaction for every glance, which compounds the friction of multi-lockout situations (an auditor reviewing five locked rows would dismiss five modals).

### Inspector as a drawer pulled from the topbar

Slide-out drawer matching Pulse's pattern (SPEC §4.2). Rejected because the drawer surface is reserved for cross-cutting state (Pulse = "everything that's currently held"), not per-row explanation. A drawer pulled from the topbar to answer a per-row question puts the user's attention far from the affordance they clicked. The popover-at-click anchors the explanation to the cause.

### Inspector behind a `Inspector.View` permission

Add a permission and gate the inspector's visibility on it. Rejected — the lock state is already visible in the parent UI (the user clicked something to open the inspector). Gating the inspector would create a "you can see the lock glyph but not why" UX which contradicts SPEC §4.3's framing.

## Consequences

### Positive

- The popover pattern is anchored to the click target — the user's attention does not have to traverse the screen to read the cause of a lock they just hovered over.
- Lazy resolution keeps the lifecycle service unchanged. Phase 2's audit-row counts remain pinned to the existing values; no audit-row inflation per CLAUDE.md non-negotiable rule #6.
- The per-type resolver registry slots Phase 3+ resolvers in without touching C7a's code. Each phase owns its own resolver and registers it via DI.
- Write-through caching is honest: the first open builds and persists the chain; subsequent opens read from the cached row; staleness is detected by comparing live entity state.
- `LockedEntityTypes` constants make typos compile-time visible (same pattern as `PermissionNames` from ADR 0007).
- No new permission added to the catalog — the inspector follows what the user can already see.

### Negative (and accepted)

- **C7a introduces a new UI pattern not in `docs/UI_PROTOTYPE.html`.** The prototype is silent on the inspector affordance. C7b's popover is the project's first popover. Acceptable because the prototype is reference, not prescription (CLAUDE.md UI Conformance), and this ADR records the choice deliberately rather than letting it slip in as code.
- **Cached `LockReason` rows reflect first-inspect state, not current state.** A revision transitions Approved → Superseded after the inspector first cached an L1 chain; the cached row's `LockReason.Chain` JSON still says "Approved." This is acceptable because the cached row is audit evidence (records that someone inspected the lock at first-touch), not the render-time source — every open re-runs the resolver and renders the fresh chain. Auditors querying the `LockReason` table directly see "first inspected on X" snapshots; they cross-reference current state via the live entity. If a future pass wants the cached chain to track live state, the resolver's output can be diffed against the cached row and the row updated in place when they differ — but C7b ships without that complexity.
- **Two distinct `ILockReasonResolver` instances for `DocumentLockReasonResolver`.** The class handles both `"Document"` and `"DocumentRevision"` types; registering twice with different `LockedEntityType` values is slightly awkward but keeps the registry's contract uniform. Could be revisited if Phase 3+ introduces resolvers that legitimately handle multiple types under one class.
- **Soft-deleted rows must be reachable via `GetByIdIncludingDeletedAsync`.** The L6 chain requires the resolver to find a row that the default `GetByIdAsync` excludes. The resolver code accounts for this; future maintainers must remember to use the IncludingDeleted variants.

## Implementation Notes

- **`ILockReasonResolver`** in `src/EasySynQ.Services/LockReasons/` (new folder) with the `LockedEntityType` property and `ResolveAsync` method. XML doc comments per project convention.
- **`ILockReasonResolverRegistry`** and `LockReasonResolverRegistry` in the same folder. The registry composes from `IEnumerable<ILockReasonResolver>` at construction.
- **`DocumentLockReasonResolver`** in `src/EasySynQ.Services/Documents/`. Constructor takes `(string lockedEntityType, IDocumentRepository, IDocumentRevisionRepository)`. Internal dispatch on the type string to per-state chain-template methods.
- **`ILockReasonRepository`** in `src/EasySynQ.Services/Abstractions/`, extending `IRepository<LockReason, Guid>` with `GetByLockedEntityAsync(string lockedEntityType, string lockedEntityId, CancellationToken ct)`. Implementation in `src/EasySynQ.Data/Repositories/LockReasonRepository.cs` using the existing `(LockedEntityType, LockedEntityId)` index from `LockReasonConfiguration.cs:52`.
- **`LockedEntityTypes`** static class in `EasySynQ.Domain` with `Document` and `DocumentRevision` constants. Phase 3+ extends.
- **DI registration** in `EasySynQ.Data.Extensions.ServiceCollectionExtensions`. Two scoped `ILockReasonResolver` registrations (factory functions producing the `DocumentLockReasonResolver` keyed differently); one scoped `ILockReasonResolverRegistry`; one scoped `ILockReasonRepository`.
- **`LockInspectorPopover` (Window) + `LockInspectorViewModel` + `ILockInspectorPrompter`** ship in C7b, not C7a. C7a is service-layer-only.
- **No schema migration** — the existing `LockReason` table is unchanged.

## Required Tests

### Unit tests (no DB)

- `DocumentLockReasonResolverTests` — one test per chain template (L1–L6) using fixture entities. Verifies chain shape (link count, terminal position, `Because` connectors), tag set, navigation refs, and human text correctness against expected templates.
- `DocumentLockReasonResolverTests` — Draft revision returns `null` (not locked).
- `DocumentLockReasonResolverTests` — L6 (soft-deleted) takes precedence over L1–L5 (a soft-deleted superseded revision returns the L6 chain).
- `DocumentLockReasonResolverTests` — unknown id returns `null` (defensive).
- `LockReasonResolverRegistryTests` — `GetResolver(knownType)` returns the registered resolver; `GetResolver(unknownType)` returns `null`; multi-resolver registration dispatches to the correct one; duplicate-key registration throws `InvalidOperationException`.

### Integration tests (with DB)

- `LockReasonRepositoryTests` — round-trip a `LockReason` with a multi-link chain via the JSON-in-column path; `GetByLockedEntityAsync` returns the row; the unique-entity index supports the lookup.
- `LockReasonChainPopulationTests` — table-driven across every Phase 2 lifecycle transition that produces a lock (Submit, Approve, Supersede, Retire, soft-delete). Each path: run the lifecycle service to produce the state, invoke the resolver, assert the resulting chain matches the documented template. This is the regression net for SPEC rule #5 — if a future phase adds a lockout pathway and forgets the resolver path, this test fires.

### Manual smoke (C7b)

- Deferred to C7b — C7a is service-layer-only, no UI to click. The C7b commit's smoke walk exercises the popover end-to-end.

## References

- `docs/SPEC.md` §4.3 (Why Is This Locked? Inspector — the surface this ADR implements), §3.5 (Soft-delete boundary — L6 source), §4.2 (Pulse drawer — the inspector deliberately differs), §4.4 (Color discipline — purple chip for linkage tags), §5.1 (Document Controller — Phase 2 entities surfaced)
- `CLAUDE.md` non-negotiable rules #5 (lockout populates LockReason chain), #6 (audit log append-only — LockReason rows are derived views), Spec Drift rules
- ADR 0007 (permission model — `PermissionNames` constants pattern reused for `LockedEntityTypes`)
- ADR 0008 (Phase 2 scope — C7 row of the chunking table; "every Phase 2 lockout pathway must surface in the inspector")
- ADR 0011 (deployment-model framing — operational users need the inspector; admin does not perform operational gestures so does not encounter most lockouts)
- `src/EasySynQ.Domain/Entities/Audit/LockReason.cs` (the data shape this ADR's resolver targets)
- `src/EasySynQ.Data/Configurations/LockReasonConfiguration.cs` (the JSON-in-column persistence shape)
- `docs/SCRATCHPAD.md` "C7 (lock inspector + print views) — visual polish surfaces" (the planning seed this ADR realizes)
- `docs/SESSION_NOTES.md` 2026-05-16 (ADR 0011 architectural follow-up) — chunk boundary before C7
