# ADR 0008 — Phase 2 Document Controller Scope and Lifecycle Model

**Status:** Accepted
**Date:** 2026-05-14 (Proposed), 2026-05-14 (Accepted)
**Supersedes:** None
**Related:** ADR 0007 (permission model — Document permissions extend the catalog); SPEC §5.1 (amended by this ADR)

---

## Context

Phase 2 of the deployment roadmap (SPEC §9) is the Document Controller — "the brain" of the QMS, source of truth for all controlled documents. SPEC §5.1 sketches the module at a high level but is thin on operational mechanics that real-world QMS practice depends on. Reviewing the SPEC against the pilot deployment's actual document-approval practice (Lead Auditor-certified Production Manager who currently runs the QMS, with 15 years metallurgical experience and direct knowledge of how SOPs move through review and approval in a small-shop heat-treating environment) surfaced several gaps and refinements the SPEC needs before implementation begins.

Phase 2 is also the project's first feature to consume infrastructure that Phase 1 set up but never exercised end-to-end: the digital signature service, the permission-based authorization model from ADR 0007, the content-addressed vault (SPEC §3.6), and the soft-delete boundary (SPEC §3.5). It is also the first phase whose work depends on infrastructure that will not exist until later phases — specifically the retraining cascade that SPEC §5.1 says fires on revision approval but writes to operator-side entities that the Competency Matrix (Phase 4) introduces.

This ADR establishes Phase 2's scope, the refined lifecycle state machine, the assigned-reviewer model, the document permissions catalog, the cross-phase dependency strategy, the entities and the commit-chunking shape Phase 2 will be implemented under. SPEC §5.1 will be amended in the first implementing commit per CLAUDE.md Spec Drift rules. The ADR does not specify the PDF viewer dependency choice, the sign-as-which-role UX, or admin UI for role/permission management; those are deferred to their own ADRs paired with the work they enable.

## Decision

### Refined lifecycle state machine

SPEC §5.1 specifies `Draft → In Review → Approved (Active) → Superseded → Archived` as a linear lifecycle. Real-world practice requires two refinements and one split:

**Refinement 1 — In Review can return to Draft.** When reviewers identify issues that require author changes, the document returns to Draft so the author can edit. The SPEC's linear arrow does not accommodate this; reality requires the bidirectional `In Review ↔ Draft` edge.

**Refinement 2 — Approved is distinct from Active.** A document can be Approved (signed off, ready to use) but not yet Active (currently in effect). This supports "approved today, effective next quarter when training is complete" — a real workflow that the SPEC's collapsed `Approved (Active)` term cannot express. The transition from Approved to Active is automatic at `EffectiveFromUtc`.

**Split — the Active terminal split.** A document in Active state can leave Active via two distinct paths:
- **Superseded:** A new revision replaces the current one. The current Active revision moves to Superseded; the new revision becomes Active (immediately or at its own `EffectiveFromUtc`). Always pairs with a new revision.
- **Retired:** The document is withdrawn with no successor. The current Active revision moves to Archived; no new revision is created. Triggered by external changes (ISO/IATF clause removal) or internal decisions (the document is no longer relevant to operations).

Full refined state machine:

```
            ┌─────────┐
            │  Draft  │◄─────────────────┐
            └────┬────┘                  │
                 │ submit                │ return-for-edits
                 ▼                       │
            ┌─────────┐                  │
            │ In Review├──────────────┐  │
            └────┬────┘               │  │
                 │ all-approvers      └──┘
                 │ signed
                 ▼
            ┌──────────┐
            │ Approved │  (signed, not yet active)
            └────┬─────┘
                 │ effective-date reached
                 │ (or immediate if EffectiveFromUtc null/past)
                 ▼
            ┌────────┐
            │ Active │
            └────┬───┘
        ┌────────┴────────┐
        │                 │
  supersede           retire
        │                 │
        ▼                 ▼
  ┌────────────┐    ┌──────────┐
  │ Superseded │    │ Archived │
  └────────────┘    └──────────┘
        ▲                 ▲
        └─── (terminal) ──┘
```

`Draft` records may be hard-deleted by their author per SPEC §3.5 — they carry no signatures and no signed records reference them. `Draft → Deleted` is not a state transition; it is a hard-delete operation that writes an `EventId 6xxx` audit row per SPEC §3.5's deletion-policy invariant.

Any state `In Review` or later is immutable-soft-delete-only per the same rule — those records have signatures (the author's submission signature is real, even before reviewer approval) or are referenced by signed records.

### Assigned-reviewer model

SPEC §5.1 says "Only Quality Manager (or Administrator) can transition In Review → Approved." This is the wrong shape for real practice. Real practice is that the author submits the document for review and names specific reviewers (typically 2–3, occasionally more) who will sign off. A QM-only model breaks down in shops where cross-functional documents require domain expertise the QM lacks — a Maintenance Manager authoring a furnace refractory inspection SOP needs Maintenance reviewers, not Quality reviewers.

The model:

- When a document is submitted for review (Draft → In Review), the author names a non-empty set of reviewers from the system's user list.
- Each named reviewer must sign off before the document advances to Approved.
- A new entity `DocumentReviewAssignment` holds the per-document, per-revision, per-reviewer state: `(Id, DocumentRevisionId, ReviewerUserId, AssignedAtUtc, AssignedByUserId, SignedAtUtc?, SignatureId?, Status)` where Status is one of `Pending`, `Signed`, `Discarded`.
- A reviewer's signature (when given) writes both a `Signature` entity (per SPEC §3.4 — `(UserId, UtcTimestamp, SHA-256 hash of signed payload, role-at-time-of-sign)`) and updates the `DocumentReviewAssignment` row with `SignedAtUtc` and `SignatureId`. The `Status` becomes `Signed`.
- When all `DocumentReviewAssignment` rows for the current revision are `Signed`, the document automatically advances Draft → In Review → Approved without further user action. The advance is part of the same transaction as the final reviewer signature.

The author also signs at submission time — the author's signature attests "I wrote this and submit it for review." This is a distinct `Signature` entity, separate from reviewer signatures. Documents in the Approved state therefore have `1 + N` signatures (author plus N reviewers).

Whether the author is permitted to assign reviewers is a permission, not a hardcoded rule: `Document.AssignReviewers` gates the submit-for-review action. Organizations can grant this permission broadly (authors assign their own reviewers — the small-shop default) or narrowly (only QM assigns reviewers — the strict-gatekeeper model). Both are valid; the permission model handles the policy disagreement without code changes.

### Signatures reset when In Review returns to Draft

If the document returns from In Review to Draft (author needs to make changes mid-review), all in-progress `DocumentReviewAssignment` rows for the current revision transition to `Discarded`. The corresponding `Signature` entities are *not* deleted — they are preserved for the audit log — but they no longer count toward approval, and the author's edit cycle resets the assignment list.

On the next submission, the author re-names reviewers (the same set, a different set, or a modified set as appropriate). Fresh `DocumentReviewAssignment` rows are written; the new set of approval signatures starts from zero.

This is the safe-by-default interpretation: signatures attest to *the document state at the time of signing*. If the document changes, the signature no longer attests to the current state, and the signer must re-sign the new state. The alternative ("partial signatures persist across edit cycles") would let a document advance to Approved with some signatures attesting to a stale version — a compliance hazard.

### Effective dating on DocumentRevision

`DocumentRevision` carries an `EffectiveFromUtc` (nullable). When null or in the past at the moment of Approval, the document transitions Approved → Active immediately as part of the approval transaction. When set to a future timestamp, the document remains in Approved state and auto-promotes to Active at `EffectiveFromUtc`.

The auto-promotion is handled at *read time* via an as-of resolver pattern (same shape as SPEC §3.7 effective-dated configuration values) — there is no background job. A query for "the currently-active revision of Document X" at instant T returns the revision whose Approved-state covers T (`ApprovedAtUtc <= T AND (EffectiveFromUtc IS NULL OR EffectiveFromUtc <= T) AND (no later revision is yet effective at T)`).

`EffectiveFromUtc` on DocumentRevision is permitted to be in the future (matching SPEC §3.7's pre-scheduled-configuration pattern). A revision approved today with `EffectiveFromUtc = next Monday` is in Approved state today and becomes Active automatically next Monday. The Active state is observable but not a stored value — it is computed at query time from `ApprovedAtUtc` + `EffectiveFromUtc` + presence-of-superseding-revision.

This avoids the "stored status field that lies because nobody updated it" failure mode. The lifecycle state (Draft / In Review / Approved / Superseded / Archived) is stored explicitly; the Active sub-state of Approved is derived.

### Supersede and Retire as distinct transitions

- **Supersede:** Triggered by approving a new revision of an existing document. The transition is atomic with the new revision's Approved state: in the same transaction, the new revision's lifecycle becomes Approved (or Active, if effective immediately), and the prior Active revision's lifecycle becomes Superseded. SPEC §5.1's "Approving a new revision automatically supersedes the prior active revision" pattern is preserved.

- **Retire:** Triggered explicitly by a user with the `Document.Retire` permission. The current Active revision's lifecycle becomes Archived. No new revision is created. The document as a whole is effectively withdrawn — future queries for "the active revision of Document X" return nothing, surfaced to the UI as "Retired on YYYY-MM-DD by U" rather than as a stale revision.

Both are signed transitions. Supersede inherits its signature from the new revision's approval. Retire requires an explicit Retirement signature (`(UserId, UtcTimestamp, SHA-256 hash of retirement payload, role-at-time-of-sign)`), captured at the moment of retire and stored alongside the document.

### Author lock during In Review

When a document is In Review, the author cannot edit it. The lock is enforced at the service layer (the edit operation throws if the document is not in Draft state) and surfaced in the UI by disabling edit affordances. This prevents the author from sneaking changes in mid-review that bypass reviewer scrutiny.

If the author needs to make changes, the path is: a reviewer (or the author, if their permissions allow it) explicitly returns the document to Draft using the `Document.ReturnForEdits` permission. This triggers the signature-reset above. The author can then edit and re-submit.

Reviewer markup or in-review commenting is a richer UX that is **deferred to a later commit**, possibly to Phase 7 or a Phase 2.5. For Phase 2, the simplest path is a comment box below the document viewer in the In Review state — reviewers post comments, the author sees them when the document returns to Draft. The comment box is a real entity (`DocumentReviewComment`) with `(Id, DocumentRevisionId, AuthorUserId, BodyText, CreatedAtUtc)`. Audit-trailed but not signed. This minimal-viable commenting ships in Phase 2.

### Document permissions catalog

Phase 2's migration adds the following entries to the Phase 1 `Permission` catalog. The `PermissionNames` constants class grows correspondingly:

| Permission | Description |
|---|---|
| `Document.Create` | Create a new Internal document (initial Draft revision). |
| `Document.EditDraft` | Edit a document while it is in Draft state. |
| `Document.SubmitForReview` | Move a Draft to In Review. |
| `Document.AssignReviewers` | Name the reviewer set when submitting (may be the same as SubmitForReview, but kept separate for the strict-QM-gatekeeper policy option). |
| `Document.Review` | Sign as a reviewer on a document where the user is in the assigned-reviewers list. |
| `Document.ReturnForEdits` | Return an In Review document to Draft. |
| `Document.Retire` | Retire an Active document (no new revision). |
| `Document.SoftDelete` | Soft-delete a document or revision (with reason) once it is past the hard-delete boundary. |
| `Document.HardDelete` | Hard-delete a Draft revision that has no signatures and is not referenced. |
| `Document.ViewArchived` | View Superseded and Archived revisions in the UI (not in the default Active-only list). |
| `ExternalDocument.Create` | Add a new External document reference (ASTM, AMS, customer spec). |
| `ExternalDocument.UpdateRevision` | Record a new revision of an External document. |
| `DocumentLink.Manage` | Create or remove links between Internal documents and External documents. |

These are seeded in the Phase 2 migration that creates the Document Controller tables. Migration-time inserts bypass the audit interceptor by design (per ADR 0007 precedent — catalog data is code-versioned, the migration is its audit trail).

### Seeded QualityManager role with default document permissions

Phase 2's migration also seeds a `QualityManager` role with the following permissions assigned to it via `RolePermission` rows:

- All Document and ExternalDocument permissions above (full operational authority over documents).
- `DocumentLink.Manage`.
- *Not* `Document.AssignReviewers` — granted instead to whichever role is appropriate per org policy. Default Phase 2 ships with the permission unassigned-by-default, so the small-shop convention (any user with `Document.Create` can submit and assign their own reviewers) is achieved by granting `Document.AssignReviewers` directly to the author role(s). Organizations that want strict-QM-gatekeeper grant it only to QualityManager.

Reasoning: the default Phase 2 install needs *some* operational role that QM users can be assigned to without requiring admin UI to first construct it. QualityManager is the role the SPEC names; it gets a sensible default permission set. Organizations modify it later via admin UI when that ships.

The `Author` role pattern (a generic operational role that holds `Document.Create`, `Document.EditDraft`, `Document.SubmitForReview`, `Document.AssignReviewers`) is *not* seeded by Phase 2's migration. Reasoning: "Author" is org-specific (it might be "Production Manager" or "Maintenance Manager" or just "Operator with elevated permissions") and the spec doesn't name it. Organizations create their own author-role-equivalent through admin UI when that ships, or grant the relevant permissions directly to individual users via `UserPermission` rows in the meantime.

### Retraining cascade as a domain event

SPEC §5.1's "Approving a revision also writes retraining-required records to all operators currently qualified on the prior revision" cannot ship in Phase 2 because Operator, OperatorQualification, and the related Competency Matrix entities do not exist until Phase 4.

The Phase 2 approval transaction publishes a `DocumentRevisionApprovedEvent` (or similar domain event) within its `IUnitOfWork.SaveChangesAsync` scope. In Phase 2, no handler is registered for this event — it publishes and is dropped. The publishing infrastructure (`IDomainEventDispatcher`, or similar) is real and tested.

In Phase 4, when Competency Matrix lands, a `DocumentRevisionApprovedEvent` handler is registered that reads the prior revision's qualified operators and writes the retraining-required rows. The handler runs within the same `SaveChangesAsync` transaction as the approval (per `MediatR`-style or `Cap`-style transactional event-dispatch patterns).

Phase 2 must therefore introduce:
- A minimal `IDomainEventDispatcher` interface and implementation that supports per-save event publication.
- Integration with `AuditSaveChangesInterceptor` so events are published in the same transaction as the audit rows they accompany.
- A `DomainEventBase` or similar marker for event types, and the `DocumentRevisionApprovedEvent` specifically.
- Tests that verify the event is published, but no handlers are registered (Phase 4 adds the handler and its tests).

This is non-trivial scope but well-bounded. The alternative (build Phase 4 entities in Phase 2, or defer the cascade entirely violating the SPEC's "transactionally" requirement) is worse.

### Out of scope for Phase 2

Deferred to future ADRs paired with the work they enable:

- **PDF viewer dependency choice.** The embedded PDF viewer required by SPEC §5.1 ("Embedded PDF rendering. Documents are read-only inside the app.") needs a real PDF rendering library. Candidates include PdfiumViewer, PdfPig (extraction-focused but renderable), WebView2 with PDF.js, or a commercial component. Each has dependency, licensing, and integration trade-offs. The choice is an ADR of its own (ADR 0009 or similar), paired with the commit that introduces the viewer. Phase 2's earlier commits proceed without the viewer; documents are stored, listed, and metadata-managed; the viewer ships in a later Phase 2 commit (likely C5 or C6 in the chunking below).

- **"Sign-as-which-role" UX.** When the first signature dialog ships in Phase 2 (C5), the SignatureService's `Roles.Single()` throw becomes user-visible for any user with multiple roles. The UX of "you have multiple roles; which one are you signing as?" is an open question (dropdown picker, modal-on-sign, organizational default with override) that wants its own ADR paired with the first signature-consuming commit. Phase 2's earlier commits proceed without the dialog; the throw remains the safe failure mode in the interim.

- **Owned-type audit shape (Phase 1 Follow-Up #11).** If Document or DocumentRevision uses owned types (e.g., `DocumentMetadata` as a value object) the question of audit-row shape for owned-type entries (currently: one audit row per `EntityEntry` including owned types) becomes load-bearing. The ADR is deferred until a concrete decision is needed; Phase 2's entity design avoids owned types where feasible (preferring discrete columns or scalar properties) to defer this decision.

- **Admin UI for role and permission management.** Phase 2's seeded `QualityManager` role and the per-user permission grants pattern are sufficient for testing and pilot deployment. Admin UI for creating new roles, modifying role permissions, and granting per-user permissions is a separate work surface, probably tackled between Phase 2 and Phase 3.

- **External document update-detection automation.** SPEC §5.1's "compatibility review required" flag when an external document is updated implies awareness of the update. In Phase 2, this awareness is manual — a user with `ExternalDocument.UpdateRevision` records the new revision, and the system flags all linked internal documents at that moment. Automatic detection (polling ASTM/AMS update feeds, etc.) is not in scope and may never be.

- **Reviewer markup, in-line annotation, redlining.** Phase 2 ships the minimal comment-box-below-viewer in the In Review state. Richer reviewer interaction (PDF markup, inline comments, redline diffs between revisions) is a richer UI surface deferred to Phase 7 or later.

### Phase 2 entity list

New domain entities (`EasySynQ.Domain`):

- `Document` — the abstract identity. Holds `Id`, `Number` (org-assigned, like "SOP-Q-001"), `Title`, `Library` (Internal | External), `RetiredAtUtc?`, `RetiredByUserId?`, `RetirementSignatureId?`, plus standard auditable fields.
- `DocumentRevision` — a specific revision. Holds `Id`, `DocumentId`, `RevisionLabel` (e.g., "Rev A", "Rev 2026-01"), `Lifecycle` (Draft | InReview | Approved | Superseded | Archived), `EffectiveFromUtc?`, `ApprovedAtUtc?`, `VaultBlobId?` (nullable until first file attached), `AuthorUserId`, `AuthorSignatureId?`, plus standard auditable fields.
- `ExternalDocument` — references to externally-issued specifications. Holds `Id`, `IssuingBody` (ASTM, AMS, etc.), `Designation`, `CurrentRevisionLabel`, `CurrentEffectiveDateUtc?`, plus standard auditable fields. No vault blob; metadata only.
- `DocumentLink` — link between an internal `DocumentRevision` and an `ExternalDocument`. Holds `Id`, `DocumentRevisionId`, `ExternalDocumentId`, `CompatibilityReviewRequiredFlag` (raised when the External updates; cleared by QM signoff or new internal revision).
- `DocumentReviewAssignment` — per-reviewer, per-revision review state. Holds `Id`, `DocumentRevisionId`, `ReviewerUserId`, `AssignedAtUtc`, `AssignedByUserId`, `Status` (Pending | Signed | Discarded), `SignedAtUtc?`, `SignatureId?`.
- `DocumentReviewComment` — reviewer comments in the In Review state. Holds `Id`, `DocumentRevisionId`, `AuthorUserId`, `BodyText`, `CreatedAtUtc`. Auditable but not signed.
- `VaultBlob` — content-addressed file storage metadata. Holds `Id`, `Sha256Hash` (string, 64-char hex), `FileSizeBytes`, `MimeType`, `OriginalFileName`, `StoredAtUtc`. The actual file lives on disk per SPEC §3.6 at `vault/<first-2-chars>/<full-hash>.<ext>`.

New domain event:

- `DocumentRevisionApprovedEvent` — published when a DocumentRevision transitions to Approved. Carries `DocumentRevisionId`, `DocumentId`, `PriorRevisionId?` (if superseding), `ApprovedAtUtc`. No-op in Phase 2; consumed in Phase 4.

### Commit chunking shape

Phase 2 is a multi-commit phase. Planned shape:

| Commit | Scope | Stops per CLAUDE.md |
|---|---|---|
| **C1 (data)** | Domain entities + EF configurations + migration that creates Document/DocumentRevision/ExternalDocument/DocumentLink/DocumentReviewAssignment/DocumentReviewComment/VaultBlob tables, seeds document permissions, seeds `QualityManager` role with default permission assignment. SPEC §5.1 amendment ships in this commit. | Entity unit tests + repository integration tests + migration-seed test green. |
| **C2 (vault)** | `IVaultService` for content-addressed file storage. Read, write (with SHA-256 compute and dedup), exists-check, retrieve. Pure service layer; no UI. | Vault tests against tempdir; round-trip + dedup verified. |
| **C3 (lifecycle)** | `IDocumentLifecycleService` implementing the state machine. Submit, return-for-edits, sign-as-reviewer, supersede, retire. Domain event publication infrastructure (`IDomainEventDispatcher`) + `DocumentRevisionApprovedEvent` publication. No handlers registered yet. | Service tests covering every state transition + audit-row pinning + event-publication assertion. |
| **C4 (sign-as-which-role ADR + dialog scaffolding)** | ADR for the signature dialog UX. Initial signature dialog implementation (single-role users only; multi-role users still throw). | ADR landed + dialog scaffolding tests + 5/5 stress. |
| **C5 (PDF viewer ADR + integration)** | ADR for the PDF viewer dependency choice. Embedded viewer integrated into the Document detail UI. | ADR landed + viewer integration + tests + smoke on a real PDF. |
| **C6 (UI shell)** | Document list view, Document detail view (with viewer from C5), submit-for-review dialog, review-and-sign dialog. Wires permission gates from C3 into the UI. | ViewModel tests + UI smoke per project protocol. |
| **C7 (lock inspector + print views)** | Lock-reason chain integration (every locked state surfaces in the lock inspector per SPEC §4.3); print-friendly Document and Revision detail renderings per SPEC §4.5. | Lock inspector tests + print-stylesheet validation. |
| **C8 (external library + compatibility flagging)** | ExternalDocument CRUD, DocumentLink CRUD, compatibility-review-required flag mechanics when an external revision is recorded. | External-document tests + link-management tests + flag-propagation tests. |
| **C9 (session-handoff)** | Session-handoff note. | — |

Realistically: 8 implementation commits + 1 handoff. C4 and C5 are partly ADR-paired commits and might land in slightly different order if the work surfaces dependencies that prefer one before the other. C6 depends on C4 and C5 having landed (the dialogs and viewer must exist for the shell to wire them). C7 and C8 can land in either order.

Phase 2 will span multiple working sessions. Each session covers 1–2 commits.

### SPEC §5.1 amendment

The amendment ships with C1. Proposed new §5.1 text:

```
### 5.1 Module: Document Controller ("The Brain")

**Purpose:** The system of record for all controlled documents.

**Sub-libraries:**
- **Internal Library** — SOPs, Work Instructions, Safety Manuals. Owned by EasySynQ; full revision control with the lifecycle below.
- **External Library** — ASTM, AMS, customer specifications. Imported; tracked by issuing body, designation, revision, effective date. **Referenced only — never authored or controlled by EasySynQ.**

**Lifecycle State Machine (refined per ADR 0008):**

`Draft → In Review → Approved → Active → (Superseded | Archived)`

with a return edge `In Review → Draft` when reviewers require author changes.

- **Draft:** Author edits freely. May be hard-deleted by author (no signatures yet).
- **In Review:** Author assigns reviewers (`Document.AssignReviewers` permission) and submits. Each named reviewer signs to advance the document. Author cannot edit. Reviewers can post comments. If returns to Draft, in-progress reviewer signatures discarded (preserved in audit log; no longer count toward approval).
- **Approved:** All reviewer signatures captured. Document signed off but not necessarily in effect yet.
- **Active:** Current effective revision. Derived at read time from `ApprovedAtUtc` + `EffectiveFromUtc` + presence of superseding revisions. A revision is Active when it is Approved AND its effective date has passed AND no later revision is yet effective.
- **Superseded:** A new revision has been approved and made Active in this revision's place.
- **Archived:** The document has been retired with no successor.

**Effective dating:** `DocumentRevision.EffectiveFromUtc` is nullable. When null or in the past at approval, the revision becomes Active immediately. When future, the revision stays Approved until the effective date, then auto-promotes to Active at read time.

**Approval mechanics:**
- Each document submission carries a set of `DocumentReviewAssignment` rows naming the reviewers.
- Each reviewer's sign-off writes a `Signature` entity per SPEC §3.4 (UserId, UtcTimestamp, SHA-256 of signed payload, role-at-time-of-sign) and updates the assignment row.
- When all assigned reviewers have signed, the document transitions In Review → Approved atomically in the same transaction as the final signature.
- The author also signs at submission time (separate `Signature` entity). An Approved document carries 1 + N signatures (author plus N reviewers).

**Supersede vs. Retire:**
- **Supersede:** Approving a new revision automatically supersedes the prior Active revision. Always pairs with a new revision.
- **Retire:** Explicit `Document.Retire` action with a Retirement signature. No new revision is created; the document is withdrawn.

**Vault Storage:** All Internal document files stored content-addressed (SPEC §3.6).

**Viewer:** Embedded PDF rendering. Documents are read-only inside the app.

**Retraining cascade:** Approving a new revision of an Internal document publishes a `DocumentRevisionApprovedEvent`. In Phase 2 no handler is registered. In Phase 4 (Competency Matrix), a handler writes retraining-required records to operators currently qualified on the prior revision, transactionally with the approval.

**Compatibility Flagging:** When an External document is updated to a new revision, all linked Internal documents receive a "Compatibility Review Required" flag until a user with the appropriate permission signs off or a new Internal revision is approved.

**Authorization:** Permission-based per ADR 0007. The Document permissions catalog (`Document.Create`, `Document.EditDraft`, `Document.SubmitForReview`, `Document.AssignReviewers`, `Document.Review`, `Document.ReturnForEdits`, `Document.Retire`, `Document.SoftDelete`, `Document.HardDelete`, `Document.ViewArchived`, plus `ExternalDocument.*` and `DocumentLink.Manage`) is seeded in Phase 2's migration. A default `QualityManager` role is seeded with all Document and ExternalDocument permissions assigned (organizations can modify via admin UI when it ships). `Document.AssignReviewers` is intentionally not assigned to QualityManager by default — organizations grant it to author roles for the small-shop default or restrict to QualityManager for the strict-gatekeeper model.

**Acceptance Criteria:**
- Approving Rev B of an SOP makes Rev A's `Lifecycle` become `Superseded`, and Rev A becomes unselectable in production workflows.
- Approving a new SOP revision publishes `DocumentRevisionApprovedEvent` in the same transaction. (Phase 4 wires the retraining-record write.)
- A user cannot edit a document in any state other than Draft — they must return-for-edits (with permission) or create a new revision.
- All lifecycle transitions recorded in the audit log with before/after states.
- Retiring a document writes a Retirement signature and moves the current revision to Archived; the document has no Active revision afterward.
- Returning a document from In Review to Draft discards all in-progress reviewer signatures (preserved in audit log; flagged as Discarded in `DocumentReviewAssignment`).
```

SPEC revision bumps from 3.3 to 3.4 with a row in the Revision History table describing the §5.1 amendment.

## Alternatives Considered

### Approve and Active as a single state (the SPEC's original shape)

Simpler — one state, one transition. Rejected because real workflows include "approved today, takes effect later." Collapsing the two states loses that distinction and forces orgs that need delayed-effective into workarounds (delay the approval transaction itself, which loses the audit "we approved this on Monday" record).

### Hardcode the QM role as the approval gate

Matches the SPEC's literal text. Rejected for the same reason ADR 0007 rejected role-based authorization: the model breaks down in real shops where domain-specific documents need domain-specific reviewers. The named-reviewer-list model fits real practice; gating it through the permission system means orgs can policy-tune as needed.

### Persist in-progress signatures across edit cycles

Author edits, prior signatures remain. Faster review cycle when edits are minor. Rejected — signatures attest to document state at signing. If the state changes, the attestation is stale. The compliance cost of "a signature in the system that doesn't actually attest to the document as-shipped" outweighs the convenience of skipping re-signature.

### Background job for Approved → Active auto-promotion

A scheduled job watches for Approved revisions whose EffectiveFromUtc has passed and updates their stored `Lifecycle` to Active. Rejected — introduces a scheduling dependency, requires backfill logic, has the "stored status that lies because the job didn't run" failure mode. Computing Active state at read time via as-of resolution is simpler, matches SPEC §3.7's pattern for other effective-dated values, and cannot lie.

### Defer the retraining cascade to Phase 4 entirely (no event in Phase 2)

Phase 2 ships without any cascade mechanism. Phase 4 adds both the operator entities and the cascade. Rejected because SPEC §5.1 explicitly says the cascade is *transactional* with approval. Wiring it up later means either (a) breaking the transactional guarantee or (b) extensively refactoring Phase 2's approval logic in Phase 4. The domain-event-with-deferred-handler pattern preserves the transaction and keeps the Phase 2 change-surface small.

### Build the Phase 4 Operator and OperatorQualification entities in Phase 2 so the cascade can write directly

Drags Phase 4 scope into Phase 2. Rejected — phase boundaries exist for a reason; pulling forward entities means pulling forward their migrations, their tests, their UI considerations. The domain event keeps Phase 2 self-contained.

### Ship Phase 2 without a seeded operational role

Auth checks exist but no role to assign. Pilot deployment must use direct per-user permission grants until admin UI ships. Rejected because it forces immediate friction (every test user requires manual permission setup) and obscures the spec's intent that QualityManager is *the* role for document approval in default deployments. The seeded role is a sensible default that admin UI can modify later.

### Make Author a seeded role

Mirror the QualityManager seeding for an "Author" role with submit-for-review permissions. Rejected because "Author" is org-specific terminology — different shops will name it differently, with different scope. Letting orgs construct their own author role through admin UI (or grant directly to individual users in the meantime) keeps Phase 2's seeded data minimal and avoids prescribing terminology the spec doesn't use.

## Consequences

### Positive

- The refined lifecycle accommodates real-world workflows (approved-but-not-yet-effective, return-from-review, retire-without-successor) that the SPEC's collapsed version cannot.
- The assigned-reviewer model matches small-shop practice and supports cross-functional document review without compromise.
- Active state computed at read time cannot lie; the stored Lifecycle plus EffectiveFromUtc are the source of truth.
- The permission catalog and seeded QualityManager role give a working out-of-the-box experience while preserving org-level policy flexibility.
- The domain-event pattern keeps Phase 2 self-contained while preserving the SPEC's transactional cascade guarantee.
- Phase 2's commit chunking gives multiple natural pause points for session breaks and review checkpoints.

### Negative (and accepted)

- **Phase 2 introduces non-trivial infrastructure not strictly required for documents** (domain event dispatcher). Justified because Phase 4 will consume it; building it now is cheaper than retrofitting Phase 2's approval logic later.
- **The state machine is denser than the SPEC's** (Draft, In Review with bidirectional Draft, Approved-distinct-from-Active, Active-as-computed, Superseded, Archived). More tests, more transitions to verify. Accepted as the cost of matching real practice.
- **Active state derivation requires every read path to know the as-of resolution rule.** Centralized in `IDocumentRepository` (or similar), with explicit `asOfUtc` parameter, mirroring ADR 0007's IUserRoleRepository pattern.
- **Multiple signatures per approved document** (1 author + N reviewers). Document detail UI must surface all of them, not just one. Audit trail grows accordingly.
- **`Document.AssignReviewers` not seeded to any role by default** means a fresh install with only QualityManager has no role that can submit-for-review. Organizations must either grant it to QualityManager (strict-gatekeeper model) or create an Author-equivalent role (small-shop default). This is friction at first-deployment time; documented in the user manual.
- **Phase 2 is large** — 8 implementation commits spanning multiple sessions. Discipline required to land each commit cleanly before moving to the next. The chunking helps but doesn't eliminate the risk of mid-phase fatigue or scope creep.

## Implementation Notes

- All Phase 2 entities are `AuditableEntity`-derived per existing pattern (CreatedBy/CreatedUtc/ModifiedBy/ModifiedUtc/RowVersion/IsDeleted standard fields).
- DocumentRevision's `Lifecycle` is an enum (`DocumentLifecycle`) backed by string in the DB (`Lifecycle TEXT NOT NULL`) — favors readability in raw queries over compact storage. EF Core configuration handles the conversion.
- `VaultBlob.Sha256Hash` is `string` (64-char hex), not `byte[]`. Trade-off: storage is 2x but query convenience and human-readability are worth it.
- The vault directory root is configurable but defaults to `%LOCALAPPDATA%\EasySynQ\vault\`. C2's `IVaultService` takes the root from config (when Follow-Up #6 — connection-string promotion — lands; otherwise from a constant analogous to `GetDatabasePath()`).
- `DocumentReviewComment.BodyText` is `string` with no length cap at the DB level (TEXT column); UI may enforce a soft cap for sensible display.
- The `DocumentLifecycle` state machine is enforced at the service-layer level, not via DB constraints. The service validates the current state allows the requested transition, throws if not. DB constraints would be redundant given the service is the only write path.
- Migration adds indexes on `DocumentRevision.DocumentId` (for "all revisions of doc X"), `DocumentRevision.Lifecycle` (for "all Active revisions"), `DocumentRevision.EffectiveFromUtc` (for as-of queries), `DocumentReviewAssignment.DocumentRevisionId` (for "all reviewers of this submission"), `DocumentLink.ExternalDocumentId` (for "all internal docs linked to this external"), `VaultBlob.Sha256Hash` (unique — content addressing requires hash uniqueness).
- The `Document.Number` field is org-assigned and unique within the org's deployment. Uniqueness is enforced by index. Format is not prescribed — orgs use whatever scheme matches their existing document numbering.
- Author signing at submission time is part of the Submit-for-Review transaction, not a separate user action. The submit dialog includes the signature affordance; submitting without signing is not possible.

## Required Tests

### Per-commit tests (each commit's stop point)

- C1: Entity unit tests for every new entity. Repository integration tests for basic CRUD on each. Migration applies cleanly to fresh SQLite; seeds the document permissions and the QualityManager role; the seeded role's permission set matches the documented default; idempotency.
- C2: VaultService tests for write-with-hash-compute, read-by-hash, exists-check, dedup-on-duplicate-content, fail-on-corrupt-hash-mismatch (defensive integrity check).
- C3: Lifecycle service tests covering every transition (Draft→InReview, InReview→Draft with signature reset, InReview→Approved on all-signed, Approved→Active immediate, Approved→Active deferred via EffectiveFromUtc, Active→Superseded on new approval, Active→Archived on retire). Each transition writes the expected audit rows. The DocumentRevisionApprovedEvent is published.
- C4: Signature dialog scaffolding tests (single-role user signs without prompt, multi-role user throws per existing behavior).
- C5: PDF viewer integration tests + manual smoke against a real PDF file.
- C6: ViewModel tests for each new VM (DocumentList, DocumentDetail, SubmitForReview, ReviewAndSign). Permission gates correctly enable/disable affordances. Smoke per project protocol.
- C7: Lock inspector tests (every locked state has a populated LockReason). Print-stylesheet validation per SPEC §4.5.
- C8: ExternalDocument CRUD tests. DocumentLink CRUD tests. Compatibility-flag propagation when ExternalDocument.UpdateRevision is called.

### Phase-wide smoke verification

After C8 lands, end-to-end smoke against real WPF host:
- Sign in as the seeded admin → admin grants Author-equivalent permissions to a test user OR admin grants Document.AssignReviewers to QualityManager.
- Test user creates a new Internal document (Draft).
- Test user submits for review with QualityManager (or another assigned reviewer) as the named reviewer.
- Test user (as the named reviewer) signs to approve.
- Document advances to Approved → Active.
- Test user creates a new revision; submits; approves; original revision becomes Superseded.
- Test user retires the document; revision becomes Archived.
- Audit log reviewed: every transition has matching audit rows with correlation IDs.

## References

- `docs/SPEC.md` §5.1 (Document Controller — amended by this ADR), §3.4 (Authorization), §3.5 (Deletion Policy), §3.6 (Document Vault), §3.7 (Effective Dating), §9 (Roadmap)
- ADR 0007 (permission model — Document permissions extend its catalog)
- ADR 0002 (audit-log invariant — Phase 2 entities all flow through the repository pipeline)
- `docs/SESSION_NOTES.md` 2026-05-14 (grooming) entry — Phase 2 next-direction option chosen
