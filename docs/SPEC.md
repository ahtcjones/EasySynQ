# EasySynQ — Comprehensive Coding Project Prompt
**A Quality Management System for ISO 9001:2015 Compliance**
**Revision 3.2 · May 2026**

---

## Revision History

| Rev | Date | Notes |
|---|---|---|
| 1 | Initial | Original specification |
| 2 | May 2026 | Refined after UI prototyping. Repositioned around ISO 9001:2015 as the base product with industry-specific compliance as optional modules. Added five new core modules (Risk Register, Management Review, Competency Matrix, Material & Lot Traceability, Supplier Management). Refined Production Control's QA Review into three discrete signature gates. Added effective-dating for configuration values. Introduced content-addressed Document Vault, tiered snapshot retention, the "Why is this locked?" inspector, reverse-pulse tiles, print-friendly views, the Customer Portal Export, and an optional Local Service Mode topology. Deferred deep-link filter URLs to v2. |
| 3 | May 2026 | Spec amendments after first review pass. Pinned hard-delete audit-log behavior — every hard delete writes a permanent `HardDelete` audit row with a full pre-delete snapshot (§3.5; see ADR 0002). Added a `RequiredSOPs` collection on Part Master as the single authoritative source for which controlled procedures apply to a job, with a compatibility-review cascade when an SOP revision is approved (§5.3, §5.9). Defined the valid evidence types for CAPA effectiveness verification (§5.4). Reserved the purple accent in the UI palette exclusively for cross-cutting linkage affordances — linked records, module surfaces, concession references — never for severity (§4.4). |
| 3.1 | May 2026 | License-driven dependency swap. Replaced FluentAssertions in §2 with **AwesomeAssertions** (a community fork of FluentAssertions 7 maintained under MIT) after FluentAssertions 8.x shifted to a commercial license in early 2025. API-compatible; no test code changes implied. |
| 3.2 | May 2026 | Clarified §3.7: `EffectiveFromUtc` may be in the future to support pre-scheduled configuration changes (e.g., a tolerance change that takes effect next Monday). The as-of resolver handles not-yet-active versions naturally; no new state required. |

---

## 0. How to Use This Prompt

You are an experienced full-stack desktop application developer. Build a production-grade Quality Management System named **EasySynQ**, designed to make ISO 9001:2015 compliance defensible by construction. Industry-specific compliance (AS9100, AMS 2750, IATF 16949, CQI-9, etc.) is delivered as **optional modules** that layer onto this core.

Treat this document as the **single source of truth** for scope, architecture, and behavior. When you encounter ambiguity, prefer:
1. Compliance defensibility over convenience.
2. Data integrity over UI flexibility.
3. Explicit, named workflows over implicit "smart" automation.
4. Long-term maintainability over short-term shortcuts.

Deliver code in incremental, reviewable phases corresponding to the Deployment Roadmap in Section 9. Do not begin a later phase until the prior phase is functional, tested, and persistable.

---

## 1. Project Identity & Domain Context

**Application Name:** EasySynQ
**Base Standard:** ISO 9001:2015
**Primary Users:** Quality Manager, Lab Technicians, Production Operators, Auditors (internal and external)
**Pilot Deployment:** Heat treating and metallurgy facility, Anniston, Alabama
**Operating Environment:** Small-to-mid-sized industrial facility, Windows-based workstations, shared network drive, no cloud dependency required.

The system must feel **rugged and unambiguous** — every action should produce a clear, signable, auditable record. Operators on the production floor must be able to use it with minimal training. Quality Managers must be able to prove compliance to a third-party auditor with zero scrambling.

The product is intentionally **domain-agnostic at its core**. Heat-treat-specific functionality lives in optional modules (Section 12) and must not contaminate the base modules.

---

## 2. Technology Stack & Foundational Constraints

| Layer | Technology | Notes |
|---|---|---|
| Language | C# (latest stable .NET LTS) | Strict nullability enabled |
| UI Framework | WPF (.NET) | MVVM pattern, no code-behind logic |
| Database | SQLite | Single shared file (see Section 3.1 for deployment topology) |
| ORM | Entity Framework Core or Dapper | Choose one and stay consistent |
| PDF Generation | QuestPDF | All certificates, exports, reports |
| Charts | LiveCharts2 or lightweight SVG equivalent | Re-evaluate at Phase 9 — most dashboard needs are simple enough that a smaller dependency is preferable if available |
| PDF Viewing | Embedded WPF rendering | PdfiumViewer, WebView2, or equivalent — rendering method is implementation detail; the constraint is **no external app opens** |
| Logging | Serilog with rolling file sink | Structured logging |
| Testing | xUnit + Moq + AwesomeAssertions | Unit + integration tests required. AwesomeAssertions is the MIT-licensed community fork of FluentAssertions 7; FluentAssertions 8+ went commercial. |
| Packaging | MSIX or ClickOnce | Centralized version pin |

**Hard rules:**
- **No cloud services.** All data lives on the customer's network.
- **No third-party identity providers.** Authentication is internal.
- **No external PDF viewers** required to operate the app.
- **SQLite WAL mode** must be enabled for concurrent reads.
- **All datetime values stored in UTC**, displayed in local time (America/Chicago for pilot).
- **All currency-free** — this is not an ERP, do not introduce pricing logic.

---

## 3. System Architecture

### 3.1 Deployment Topology

**Default — Shared File Mode:**
- WPF client installed on each workstation.
- Single `EasySynQ_Master.db` SQLite file on a mapped network drive (e.g., `Q:\EasySynQ\db\`).
- Adjacent **Document Vault** directory for PDF storage (see Section 3.6).
- Daily snapshot directory for ZIP archives (see Section 3.3).

**Optional — Local Service Mode (Plan B):**
SQLite over SMB is fragile under concurrent load and unreliable networks. If concurrent users exceed ~6 or network reliability becomes a concern, the architecture must support swapping to **Local Service Mode**: a small .NET service runs on one workstation (or dedicated box) that owns the SQLite file locally and serves WPF clients over gRPC or named pipes on the LAN. Same single-DB story, same "no cloud" promise, dramatically more reliable.

The Data layer must be designed behind a repository interface so this swap is a configuration change, not a rewrite. **This is a Phase 1 requirement** — the interface design must accommodate both modes from day one even if only Shared File Mode ships in v1.

### 3.2 Application Layers
1. **EasySynQ.Domain** — POCO entities, value objects, enums, domain services. No framework references.
2. **EasySynQ.Data** — EF Core (or Dapper) context, migrations, **repository interfaces** (mode-agnostic).
3. **EasySynQ.Services** — Business logic: workflow state machines, validation, signature service, snapshot service.
4. **EasySynQ.UI** — WPF, MVVM, view models, converters, navigation.
5. **EasySynQ.Tests** — Unit and integration tests.

### 3.3 Concurrency, Resilience & Snapshots
- Use SQLite WAL mode.
- Wrap all writes in transactions.
- Implement an **optimistic concurrency token** on every critical entity.
- On application start, verify integrity of `EasySynQ_Master.db` via `PRAGMA integrity_check;` and surface failures.

**Tiered snapshot retention** (replaces the original flat 90-day rule):
- **Daily snapshots** — retained 90 days.
- **Weekly snapshots** (Sunday) — retained 1 year.
- **Monthly snapshots** (1st of month) — retained indefinitely (configurable cap).

This accommodates the 3–7 year audit lookback typical for ISO 9001 and customer requirements without exploding disk usage. Each snapshot ZIPs the database file and the Document Vault and stores the package under `snapshots/{tier}/{yyyy-MM-dd}.zip`. Snapshot integrity is verified by SHA-256 of the ZIP recorded at creation; periodic restore drills are part of the test suite.

### 3.4 Security & Data Integrity

- **Authentication:** Local user accounts with salted+hashed passwords (PBKDF2 or Argon2id). No plaintext passwords.
- **Authorization:** Role-based — at minimum: Operator, Lab Tech, Quality Manager, Auditor (read-only), Administrator. Additional roles per module (e.g., Maintenance Tech).
- **Digital Signatures:** Identity-based. A "signature" is a record `(UserId, UTC Timestamp, SHA-256 hash of the signed payload, role-at-time-of-sign)`. Never use stored images of signatures.
- **Audit Trail:** A global `AuditLog` table capturing every insert/update/delete on compliance-critical entities. Captures user, UTC timestamp, entity type, entity id, action, and before/after JSON snapshots of changed fields. Audit log is append-only — never editable from the UI.

### 3.5 Deletion Policy (Refined)

The original spec's blanket "no hard deletes on compliance records" is correct but too broad. Refined boundary:

A record becomes **immutable-soft-delete-only** the moment either:
1. It carries any digital signature, OR
2. It is referenced by any signed record.

Before either condition is met, the original author may **hard-delete** the record as a draft. The UI must surface the transition explicitly: a "locked" indicator appears on the record when it first becomes signed-or-referenced, and the delete affordance changes from "Delete" to "Soft-Delete (with reason)."

This prevents the database accumulating noise from typos and abandoned drafts while keeping compliance integrity airtight.

**Audit-log behavior for hard delete:** Even a hard-deleted draft writes exactly one audit-log entry with action type `HardDelete`. The entry's `before` field captures the full final-state JSON snapshot of the entity at the moment of deletion; the `after` field is `null`. The operational row is removed; the event is preserved permanently in the append-only audit log. In this system, "hard delete" therefore means *gone from the operational table, retained as audit evidence* — never *silently lost*. The audit log is the cheapest insurance against "did someone scrub activity?" and that guarantee must hold for every CRUD path. The operational deletion and the audit-row write occur in the same transaction; a failed audit write rolls back the deletion. See ADR 0002 for the full decision record.

### 3.6 Document Vault (Content-Addressed Storage)

The Vault is a flat (or 2-character-sharded) directory of files **named by the SHA-256 of their content**. Example: `vault/7a/7a3b…4f91.pdf`. No "human-readable" folder structure on disk.

All human-readable paths (e.g., "Job J-2026-0847 chart") and folder organization live in the database. Renames and reorganizations are metadata changes only; the underlying file is never moved, renamed, or duplicated. Identical files (same hash) are deduplicated automatically.

This gives the system:
- Tamper-evident storage (filename = content hash).
- Free deduplication.
- Trivial reorganization (no filesystem migrations).
- Simpler vault portability for War Room exports.

### 3.7 Configuration & Effective Dating (NEW)

Every configuration value that affects compliance evaluation must carry an `EffectiveFromUtc` and `EffectiveToUtc` (nullable for currently-active values). Examples: hardness tolerances on a Part Master, calibration intervals on an asset, PM schedules, quality objective targets, recipe parameters.

**Historical records are evaluated against the configuration in effect at the time of the event, not the current configuration.**

This is the difference between "did this job pass under the rules in effect when it was processed?" (the only auditor-defensible question) and "does this job pass against current rules?" (an unanswerable category error).

`EffectiveFromUtc` is **permitted to be in the future**. Pre-scheduled configuration changes are a deliberate workflow — a Quality Manager may, for example, pre-create a tolerance change for a customer specification that takes effect next Monday, or pre-load an updated calibration interval that activates at the start of a new quarter. The as-of resolution handles this naturally: queries with an `asOf` timestamp earlier than a record's `EffectiveFromUtc` simply do not return the not-yet-active version. No `Scheduled` state or separate "draft configuration" entity is needed; the temporal axis is the only state required.

Implement once, as a generic temporal value-object pattern. Every effective-dated entity has a clear version history visible in its detail screen.

---

## 4. Cross-Cutting UI/UX Requirements

### 4.1 Shell

Single tabbed main window with a left-side **Navigation Tree** (collapsible) grouped into sections: Pulse, Governance, Operations, Quality, Insights. See Section 9 for the module → section mapping.

### 4.2 Pulse (Refined — Drawer Pattern)

The Pulse is reframed from a horizontal banner to a **slide-out drawer** triggered by a button in the top bar. The top bar itself shows a compact summary (e.g., "3 red · 12 amber") and is the affordance to open the drawer.

The drawer is grouped by category (Calibration, NCR/Quality, CAPA, Documents, Training, Risk, Suppliers, etc.) with expandable groups. Severity-sorted within each group. Each tile is clickable and navigates to the underlying filtered list.

**Reverse-pulse tiles** ("good news"): when no alerts of a given category exist for a configured window, show a positive-state tile in that category's slot — "47 days since last NCR," "100% calibration currency," "All operator qualifications current." The cultural and operational value is real: it makes red items stand out, and it lets a QM see at a glance that the system is functioning.

### 4.3 The "Why Is This Locked?" Inspector (NEW)

Every lock indicator, red-light banner, and blocked-action state must be **interrogable**. Clicking the lock icon opens a popover showing the full causal chain. Example:

> Job J-2026-0847 — Final Release blocked because:
> ↳ NCR-2026-0033 is open
> ↳ Reading #4 (Shoulder) measured 36.4 HRC, below tolerance
> ↳ Tolerance 38.0–42.0 HRC sourced from Part Master AD-1184-C Rev 3 (effective 2026-01-12)

Each link in the chain is navigable. This kills the "why won't this work?" support-ticket-to-the-QM problem and is a powerful auditor demonstration.

### 4.4 General UI Discipline

- **Color discipline:** Red exclusively for failures, lockouts, and overdue items. Green for passing/approved. Amber for "review needed." Blue for informational/in-progress.
- **Purple is reserved for cross-cutting linkage affordances** — linked records, module surfaces, customer concession references, and similar non-severity cues. Purple is never used for pass/fail or any severity tier. The four-color severity discipline (red / green / amber / blue) is exclusive to state communication; purple sits outside it as a "this points to something else" accent.
- **No color-only signaling.** Icons paired with every color signal (accessibility).
- **Keyboard-first** for data entry grids (hardness readings, etc.). Tab order must be deliberate.
- **Every list view** supports filter, sort, column chooser, and CSV export.
- **Every detail view** has a visible status badge and a "History" panel showing the audit trail entries for that record.
- **Dirty-state guard:** Navigating away from unsaved edits prompts the user.
- **No silent failures.** Errors surface in a consistent toast/dialog pattern with a log correlation ID the user can quote.

### 4.5 Print-Friendly Views (NEW)

Every detail screen must have a **print stylesheet** that:
- Strips navigation chrome.
- Inlines the audit trail (no expansion required).
- Renders cleanly on US Letter without operator setup.
- Includes a footer with record ID, snapshot timestamp, and page numbers.

Auditors still print. Print views are part of acceptance, not an afterthought.

### 4.6 Deferred

- **Deep links / shareable filtered-view URLs**: deferred to v2. Low value in a desktop app on a closed network where users sit feet apart.

---

## 5. Module Specifications

> Section numbers preserved from Rev 1 for stability; new modules appended as 5.7–5.11. UI ordering and roadmap phase ordering are independent.

### 5.1 Module: Document Controller ("The Brain")

**Purpose:** The system of record for all controlled documents.

**Sub-libraries:**
- **Internal Library** — SOPs, Work Instructions, Safety Manuals. Owned by EasySynQ; full revision control.
- **External Library** — ASTM, AMS, customer specifications. Imported; tracked by issuing body, designation, revision, effective date. **Referenced only — never authored or controlled by EasySynQ.**

**Lifecycle State Machine:** `Draft → In Review → Approved (Active) → Superseded → Archived`
- Only Quality Manager (or Administrator) can transition `In Review → Approved`.
- Approval requires a digital signature.
- Approving a new revision automatically supersedes the prior active revision.
- **Approving a revision also writes retraining-required records to all operators currently qualified on the prior revision** (see Module 5.9).

**Vault Storage:** All document files stored content-addressed (Section 3.6).

**Viewer:** Embedded PDF rendering. Documents are read-only inside the app.

**Compatibility Flagging:** When an external document is updated to a new revision, all linked internal documents flag with **"Compatibility Review Required"** until a Quality Manager signs off or issues a new internal revision.

**Acceptance Criteria:**
- Approving Rev B of an SOP makes Rev A read-only and unselectable in production workflows.
- Approving a new SOP revision transactionally writes retraining records to affected operators in the same transaction.
- A user cannot edit an `Approved` document — they must create a new revision.
- All transitions recorded in the audit log with before/after states.

---

### 5.2 Module: Asset & Equipment Management ("The Body")

**Purpose:** Track every piece of equipment that affects product quality. Manage calibration and preventive maintenance.

**Hierarchy:** Parent-child relationships, arbitrary depth.

**Asset Record Fields:** Unique Asset ID, display name, asset type (lookup), manufacturer, model, serial, parent asset (nullable), location, status, in-service date, retirement date (nullable), notes.

**Status:** `Active | Out-of-Service (OOS) | Retired`

**Calibration:**
- Each asset has a calibration schedule (effective-dated; see Section 3.7).
- Calibration records: date performed, performed-by, Pass/Fail, certificate PDF (mandatory on Pass), next-due date (auto-calculated), notes.
- A Fail result automatically sets asset status to OOS and creates a Maintenance Work Order.

**Preventive Maintenance (NEW DETAIL):**
- Each asset may have zero or more PM tasks. Each task has its own schedule, independent of calibration.
- **Schedule types:** Daily, Weekly, Monthly, Quarterly, Semi-Annual, Annual, By Hours, By Cycles, Custom Days.
- Each task: title, schedule, assigned owner, last completed date, next-due date (auto-calculated from schedule type), completion checklist.
- Completing a PM task writes a signed completion record.
- Overdue PM tasks surface in the Pulse drawer.

**Maintenance Work Orders:**
- Status: `Open | In Progress | Completed | Cancelled`.
- Completing requires either (a) a follow-up Pass calibration record, or (b) Quality Manager override with justification text.

**OOS Lockout (the rule, not just the convention):**
- An asset in OOS status is digitally blocked from selection in any Production Job, calibration verification, inspection record, or PM task.
- Attempts to use an OOS asset show the "Why is this locked?" inspector (Section 4.3).

**Retirement:**
- Retired assets are flagged hidden from active views.
- Their historical use in past jobs remains intact and queryable.

**Acceptance Criteria:**
- The Pulse Drawer shows count of assets/PM tasks within 7 days of due plus count overdue.
- Searching for an asset by partial Asset ID, serial, or name returns results in <300 ms with 5,000 assets in the DB.
- An OOS asset cannot be selected anywhere in the system; lockout reason is visible via the lock inspector.

---

### 5.3 Module: Production Control ("The Workhorse")

**Purpose:** Manage jobs from intake to release.

**Part Master Library:**
- Customer, part number, material designation, target tolerance(s), recipe reference, customer spec link (External Library), drawing revision.
- All tolerances and recipe parameters are effective-dated (Section 3.7).
- Template-first job creation — operators cannot type free-form materials or recipes; they must select a Part Master record.
- **Required SOPs:** Each Part Master carries a `RequiredSOPs` collection of `DocumentRevisionRef` entries — each pointing to a specific controlled-document revision (Module 5.1). The Part Master is the **single authoritative source** for which controlled procedures apply to a job; the required-SOP list is not derived from furnace asset, material lot, or customer.
- **SOP-revision cascade:** When a new revision of an SOP is approved (Module 5.1), every Part Master whose `RequiredSOPs` references the prior revision is auto-flagged for review (the same compatibility-flag pattern §5.1 uses for external-document revisions). The Quality Manager must either bump the Part Master to the new SOP revision or sign an explicit "prior revision continues to apply" attestation. Until one of those occurs, the Part Master is flagged in lists and detail views and surfaces on the Pulse drawer; new jobs may still open against it.
- **Lock-chain shape:** The Operator Qualification Gate (below) reads the required SOPs from the active Part Master revision at the time of job open. The lock-inspector chain reads naturally: `Job → Part Master → Required SOPs → Operator Qualifications`.

**Job Traveler:**
- Job ID, customer, part master snapshot, **material lot reference (required — see Module 5.10)**, quantity in, quantity out, operator, asset(s) used, recipe parameters (snapshotted from Part Master at job open), status.

**Status (refined — three QA gates):**
`Open → In Process → Chart Review → Inspection Review → CoC Issuance → Final Released | Held (NCR)`

The single "Awaiting QA Review" of Rev 1 is decomposed into three discrete signature gates, each captured as a separate signature record bound to the job:

1. **Chart Review Sign-Off** — confirms the furnace recorder output (or analogous process record) matches recipe set points within tolerance. May be performed by a Lab Tech with chart-review authority.
2. **Inspection Completeness Sign-Off** — confirms all required inspection readings have been entered and reviewed. Typically Lab Tech.
3. **Certificate of Conformance Sign-Off** — final release. **Must be Quality Manager.** Generates the CoC PDF.

Each gate must be signed in order. The job status surfaces which gate is pending. The `Held` state (via NCR) blocks all downstream gates.

**Process Chart Import:**
- Operators drag-and-drop process recorder output onto the Job Traveler.
- File copied into Document Vault (content-addressed; Section 3.6).
- File hash stored with the job for tamper detection.
- If CSV, parse and store key data points.

**Operator Qualification Gate (NEW):**
- Selecting an Operator on a Job Traveler validates against the Competency Matrix (Module 5.9).
- Operators with expired qualifications on the required SOPs cannot be selected. Lock inspector explains why.

**Lab Inspection Grid:**
- Granular reading entry: location on part, reading, unit, tester asset, technician, timestamp.
- Real-time comparison against tolerance (from Part Master as of job open).
- Out-of-tolerance readings highlight red and **prompt** NCR creation.

**Certificate of Conformance (CoC):**
- Generated via QuestPDF.
- Includes job summary, part master snapshot, measured results, chart reference, material lot/heat traceability, signing Quality Manager identity, UTC timestamp, signature hash.
- Stored in Vault content-addressed.
- The CoC PDF hash is stored on the job.

**Acceptance Criteria:**
- A job cannot reach Final Released with any out-of-tolerance reading unless an NCR has been opened and dispositioned.
- A job cannot be Final Released if any asset used is currently OOS.
- A job cannot be opened with an unqualified operator.
- A job cannot be opened without at least one Material Lot reference.
- All three QA gates must be signed in order; the Job's status always reflects the currently-pending gate.

---

### 5.4 Module: Quality Immune System (NCR / CAPA)

**Purpose:** Detect, contain, investigate, and prevent quality failures.

**Non-Conformance Record (NCR):**
- Triggered from any quality-impacting record (job, inspection, audit finding, customer complaint, supplier defect).
- **Red Light Workflow:** Opening an NCR against a job immediately moves the job to `Held` status and blocks all downstream gates.
- Fields: source record reference, description, severity, opened-by, opened-on.

**Investigation:**
- **Five Whys** form is mandatory.
- Disposition options: `Scrap | Rework | Use-As-Is (with Customer Concession) | Return to Vendor`.
- A `Use-As-Is` disposition requires a linked **Customer Concession** record (customer rep, concession authorization document, scope, expiration).
- Each disposition requires a digital signature from the Quality Manager.

**CAPA:**
- Manually escalated from one or more NCRs by the Quality Manager. Not every NCR escalates.
- **State machine:** `Open → Action Implemented → Pending Verification (30/60/90 day window) → Verified Effective | Reopened`.
- Verification timer surfaces on the Pulse drawer.
- Closing as `Verified Effective` requires a signature, a verification note, and **at least one linked evidence record** drawn from the enumerated valid types below. Closing without a linked evidence record of a valid type is rejected.

**Valid CAPA effectiveness-evidence types:**

1. **Post-implementation inspection readings.** One or more inspection readings on jobs run *after* the CAPA's `Action Implemented` date, demonstrating the conforming outcome the CAPA targeted. The linked readings must belong to jobs whose `OpenedUtc` is later than the `ActionImplementedUtc` of the CAPA.
2. **Audit-finding clearance.** An audit finding that previously cited the same ISO 9001 clause (or module clause) the CAPA addresses, now rated `Conforming` on a subsequent audit whose `OpenedUtc` is later than the CAPA's `ActionImplementedUtc`.
3. **Signed "no recurrence observed" attestation.** A Quality Manager signature covering a defined verification window (30, 60, or 90 days, matching the CAPA's chosen window), attesting that the targeted failure mode has not recurred in that interval. The signed payload must include the CAPA id, the window dates, and the operational scope reviewed.

(1) or (2) are preferred. (3) is the fallback for CAPAs that address **risks** (Module 5.7) rather than realized non-conformances — i.e., when the CAPA implements a control before a known failure mode materializes and there is therefore no recurrence event to point at. A CAPA opened from one or more NCRs (the common case) should not close on (3) alone unless the QM signs a justification explaining why (1) and (2) were not available.

**Acceptance Criteria:**
- The Pulse Drawer shows: Open NCRs, NCRs older than 14 days, CAPAs entering verification this week, overdue verifications.
- An NCR cannot be closed without a completed Five Whys and a disposition signature.
- A `Use-As-Is` disposition cannot be signed without a linked Customer Concession.
- A CAPA cannot close `Verified Effective` without at least one linked evidence record.

---

### 5.5 Module: The Watcher (Internal Auditing)

**Purpose:** Run internal audits and prepare for external ones.

**Master Checklists:**
- Clause-based templates for ISO 9001:2015.
- Optional modules (Section 12) add their own clause sets.
- Clauses are version-controlled separately from audits — opening an audit copies the current clause set as an immutable snapshot.

**Audit Execution:**
- Auditor walks the checklist clause-by-clause.
- Each clause: rating (Conforming / Minor / Major / Observation), notes, evidence links — direct references to specific Job IDs, Calibration records, Document IDs, NCRs, CAPAs, Risk entries, Management Review records, Competency entries.
- Attaching evidence creates a permanent, signed link.

**Internal Observation Log:**
- Non-conforming findings spawn observation entries with assigned owner and due date.

**External Audit "War Room" Export:**
- Curated package: HTML index page linking to copies of every cited document, certificate, calibration cert, NCR, audit finding for a defined date range.
- Self-contained timestamped folder; external auditors review without touching the live database.

**Customer Portal Export (NEW):**
- Single-job (or single-CoC) export package containing only customer-facing artifacts: the CoC PDF, the process chart, and the traceability chain (material certs, calibration evidence for assets used).
- **Excludes internal NCRs unless** the disposition was `Use-As-Is`, in which case the Customer Concession authorization is included.
- One-click generation from any released Job. Replaces the manual "email me the cert package" workflow.

**Acceptance Criteria:**
- An audit cannot be marked Complete with unresolved findings older than 30 days unless the Quality Manager signs an exception.
- War Room Export for one fiscal year completes in under 2 minutes for a typical dataset.
- Customer Portal Export generates in under 5 seconds for a single job.

---

### 5.6 Module: Data Intelligence & Review

**Purpose:** Make compliance and operational health visible at a glance.

**Dashboards:**
- **First Pass Yield (FPY)** — by part, by customer, by month.
- **Equipment Uptime** — % time assets Active vs. OOS.
- **Audit Health** — open findings by age bucket.
- **NCR Pareto** — root causes ranked by frequency.
- **Training Currency** — % of required qualifications current (rollup from Module 5.9).
- **Supplier Performance** — OTD and quality rating rollup (rollup from Module 5.11).

**Drill-Down:**
- Clicking any chart element navigates to the filtered underlying record list.

**Acceptance Criteria:**
- All dashboard tiles refresh on demand and on a configurable interval (default 5 minutes).
- Drill-down preserves the dashboard's date range and grouping.

---

### 5.7 Module: Risk Register ("The Forecast") — NEW

**Purpose:** Track risks and opportunities per ISO 9001 §6.1.

**Risk Record Fields:** Risk ID, description, category (operational / quality / personnel / external / IT / supplier), likelihood (1–5), impact (1–5), severity score (L × I), mitigation actions, mitigation owner, review cadence, last review date, last review signature, status (Open / Monitoring / Mitigated / Closed).

**Heat Map View:** 5×5 likelihood × impact matrix. Risks plotted as labeled bubbles. Cells colored by severity.

**Review Cadence:** Configurable per risk (default quarterly). Overdue reviews surface in the Pulse drawer.

**Linkage:**
- Risks may be linked to NCRs (when a realized risk became an actual non-conformance), CAPAs (when a CAPA addresses a risk), Management Review records, and Audit findings.
- A high-severity risk (score ≥ 15) auto-creates a notification on the Pulse drawer for the Quality Manager.

**Acceptance Criteria:**
- A quarterly risk review with no signed evidence creates an alert at +1 day overdue.
- New high-severity risks auto-surface on the Pulse drawer.
- Each review creates a signed record bound to the risk's state at time of review.

---

### 5.8 Module: Management Review ("The Bridge") — NEW

**Purpose:** Conduct and record management reviews per ISO 9001 §9.3.

**Required Inputs (clause 9.3.2) — auto-populated from system data where possible:**
1. Status of actions from previous reviews.
2. Changes in internal/external issues.
3. Customer satisfaction & feedback.
4. Quality objectives — performance.
5. Process performance & conformity of products.
6. Nonconformities & corrective actions (NCR/CAPA Pareto).
7. Monitoring and measurement results.
8. Audit results.
9. Performance of external providers (Module 5.11 rollup).
10. Adequacy of resources.
11. Effectiveness of actions taken to address risks (Module 5.7).
12. Opportunities for improvement.

**Required Outputs (clause 9.3.3):**
- Decisions on improvement opportunities.
- Decisions on changes to the QMS.
- Decisions on resource needs.
- Action items (with owner and due date — tracked to closure).

**Cadence:** Quarterly minimum (configurable). Overdue reviews surface in the Pulse drawer.

**Signature:** Chair (typically QM) signs the completed review. Signature binds attendees, inputs, decisions, and action items via SHA-256 hash.

**Acceptance Criteria:**
- A Management Review cannot be marked Complete until all required 9.3.2 inputs are checked.
- Action items captured during a review flow to a tracked queue; overdue items surface on the Pulse drawer.
- Each review's signed record is queryable as evidence in Internal Audits (Module 5.5).

---

### 5.9 Module: Competency Matrix ("The Roster") — NEW

**Purpose:** Track operator qualifications per ISO 9001 §7.2.

**Matrix Structure:** Operators (rows) × Skills (columns). Skills include:
- Controlled documents (e.g., "SOP-HT-014 Rev B") — automatically populated from Module 5.1.
- Safety certifications (Forklift, Hot Work, Crane, etc.) — manually maintained.
- Competency certifications (Internal Auditor, etc.).

**Cell States:** Qualified | Expiring (<30 days) | Expired | In Training | Not Applicable.

**Training Records:** Each qualification entry includes training date, trainer, expiration date (nullable), training method (classroom / OJT / online), and any test/evaluation result. Each entry is a signed record.

**Document Revision Auto-Triggers:**
- Approving a new revision of a controlled document automatically marks all operators previously qualified on the prior revision as "Retraining Required."
- Operators in this state cannot be selected as Operator on any new Job Traveler that references the affected SOP until they sign acknowledgment of the new revision.

**Production Lockout:**
- Expired SOP qualifications block the operator from selection on jobs requiring that SOP.
- Lock state is visible via the lock inspector (Section 4.3) with full reason chain.

**Source of the per-job required-SOP list:**
- The set of SOPs a Job Traveler requires is sourced *exclusively* from the active Part Master revision's `RequiredSOPs` collection at the time of job open (Module 5.3). Part Master is the single authoritative source.
- The qualification check is not derived from furnace asset, material lot, customer, or any other proxy. This keeps the lock-inspector chain canonical: `Job → Part Master → Required SOPs → Operator Qualifications`.

**Acceptance Criteria:**
- A Job Traveler cannot be opened with an operator whose required SOP qualifications are expired or in retraining-required state.
- Document revision approval writes retraining-required records to affected operators in the same transaction as the revision approval.
- The matrix renders in under 500 ms for 50 operators × 30 skills.

---

### 5.10 Module: Material & Lot Traceability ("The Lineage") — NEW

**Purpose:** Provide forward and backward traceability per ISO 9001 §8.5.2.

**Material Lot Record:** Lot ID, material designation, heat number (from mill), supplier (Module 5.11), mill certificate (PDF, content-addressed), PO number, receiving date, receiving inspector, quantity received, quantity on hand, status (Quarantine / Released / Held / Depleted).

**Receiving Inspection:** Required at receipt. Captures visual + dimensional + cert review. Sign-off transitions `Quarantine → Released`.

**Job Linkage:**
- Every Job Traveler must reference one or more Material Lots (enforced at job open).
- CoC includes lot/heat traceability automatically.

**Forward Traceability:** From any Material Lot, list every Job, CoC, and Customer that received material from it.

**Backward Traceability:** From any Job or CoC, navigate to the Material Lot, mill certificate, and supplier.

**Held Lot Workflow:**
- Placing a lot on `Held` immediately blocks selection in new jobs.
- The system surfaces an alert listing jobs that have already consumed material from the held lot.

**Acceptance Criteria:**
- A Job cannot be opened without at least one Material Lot reference.
- Forward traceability for one lot returns complete results in <500 ms with 50k jobs in the DB.
- Held lots are blocked from selection; lock inspector explains why.

---

### 5.11 Module: Supplier Management ("The Gatekeeper") — NEW

**Purpose:** Manage approved suppliers per ISO 9001 §8.4.

**Supplier Record:** Name, scope (raw material / calibration / outsourced process / consumables / tooling / other), approval status (Approved / Conditional / Suspended / Removed), approval date, approval signature, last audit date, next audit due, OTD rate (rolling 12 months), quality rating (A/B/C, rules-based), open NCRs against supplier.

**Audit Cadence:** Configurable per supplier (default annual). Overdue surfaces on the Pulse drawer.

**Performance Rollups:**
- OTD rate calculated from received-vs-promised dates on material receipts.
- Quality rating computed from NCR frequency and severity against the supplier.

**Linkage to Material Lots:** Lots reference their supplier. Suspending a supplier locks new lots from being received but does not invalidate prior released lots.

**Acceptance Criteria:**
- A Material Lot cannot be received from a supplier whose status is `Suspended` or `Removed`.
- Supplier scoring rolls up automatically without manual recalculation.
- Suspending a supplier triggers a Pulse alert listing any material currently on hand from that supplier.

---

## 6. Data Model Guidance (Non-Exhaustive)

Core entities — designed carefully and consistently:

**Identity & Audit:** `User`, `Role`, `Signature`, `AuditLog`

**Configuration & Temporal:** `EffectiveDateRange` (value object), `ConfigurationVersion<T>` (generic wrapper for effective-dated values)

**Documents:** `Document`, `DocumentRevision`, `ExternalDocument`, `DocumentLink`, `VaultBlob` (content-addressed)

**Assets:** `Asset`, `AssetType`, `CalibrationRecord`, `PmTask`, `PmTaskCompletion`, `MaintenanceWorkOrder`

**Production:** `PartMaster`, `PartMasterRevision`, `Job`, `JobChart`, `InspectionReading`, `CoC`

**Quality:** `NCR`, `FiveWhysEntry`, `Disposition`, `CustomerConcession`, `CAPA`, `CAPAVerification`

**Risk & Governance:** `RiskRecord`, `RiskReview`

**Management Review:** `ManagementReview`, `ManagementReviewInput`, `ManagementReviewActionItem`

**Workforce:** `Operator`, `Skill`, `OperatorQualification`, `TrainingRecord`

**Material:** `MaterialLot`, `MillCertificate`, `ReceivingInspection`, `MaterialConsumption`

**Suppliers:** `Supplier`, `SupplierAudit`, `SupplierScoreEvent`

**Auditing:** `Audit`, `AuditClause`, `AuditFinding`, `EvidenceLink`

**Infrastructure:** `Snapshot`, `LockReason` (for the lock inspector)

**Standard fields on every signable entity:** `CreatedBy`, `CreatedUtc`, `ModifiedBy`, `ModifiedUtc`, `RowVersion`, `IsDeleted`, `LockedAtUtc` (the moment it crossed from draft to immutable).

**Effective-dated entities** carry an `EffectiveFromUtc` and nullable `EffectiveToUtc`, and queries against them must default to "as-of the event timestamp" rather than "as-of now."

---

## 7. Testing Requirements

- **Unit tests** for every domain service (signatures, lifecycle transitions, lockout rules, Five Whys validation, effective-dating resolution, lock-reason chain generation).
- **Integration tests** against a temp SQLite file covering the end-to-end happy path: supplier approval → material receipt → job creation → operator qualification check → chart import → inspection → NCR → disposition → CAPA → verification → audit evidence linking → management review.
- **Concurrency test:** Two simulated clients editing the same Job — optimistic concurrency must reject the second write cleanly.
- **Snapshot tests:** Daily/weekly/monthly snapshot service produces valid, restorable ZIPs at each tier.
- **Effective-dating test:** A historical job evaluated under a since-changed tolerance produces the same pass/fail result it did originally.
- **Vault deduplication test:** Two identical files written under different metadata produce one physical Vault blob.
- **Lock-inspector test:** For each lockout scenario in the spec, the inspector returns a complete causal chain ending in a navigable record.
- **Print test:** Each detail screen's print stylesheet renders on US Letter without horizontal overflow.
- Target ≥80% line coverage on Domain and Services projects.

---

## 8. Non-Functional Requirements

- **Performance:** Cold app start under 4 seconds. Common list views render under 500 ms with 50k records.
- **Reliability:** No data loss on power failure mid-write (WAL + transactions).
- **Maintainability:** Code follows .editorconfig conventions; no warnings allowed in CI.
- **Documentation:** Every public service method has XML doc comments. A `/docs` folder contains a developer onboarding guide and a user manual draft.
- **Localization:** All UI strings centralized in resource files (English only at launch).
- **Accessibility:** WPF automation peers for screen readers; no color-only signaling.
- **Print:** All detail screens produce clean US Letter prints without operator setup. Validated by visual review.

---

## 9. Deployment Roadmap

Build in this order; do not skip ahead. At the end of each phase, deliver: working build, release notes, updated user manual section, and passing test suite.

**Nav grouping shipped with each phase is shown in brackets.**

| Phase | Scope | Rationale |
|---|---|---|
| 1 | **Foundation** — Auth, roles, audit log, signature service, snapshot service, repository interface (mode-agnostic), shell UI with Pulse drawer placeholders, lock inspector framework. | Everything else depends on this. |
| 2 | **Document Controller** [Governance] | Source of truth for procedures; required before competency tracking. |
| 3 | **Risk Register** [Governance] + **Supplier Management** [Operations] | Governance basics. Suppliers needed before Material Lots. |
| 4 | **Competency Matrix** [Governance] | Depends on Doc Controller revisions; required before Production. |
| 5 | **Asset & Equipment Management** (incl. PM) [Operations] | Required before Production. |
| 6 | **Material & Lot Traceability** [Operations] | Depends on Suppliers; required before Production. |
| 7 | **Production Control** [Operations] | Depends on Modules 2–6. Three QA gates. |
| 8 | **NCR / CAPA** [Quality] | |
| 9 | **Internal Auditing + Management Review** [Governance] | Management Review depends on NCR, Audit, Risk data. |
| 10 | **Data Intelligence** [Insights] | Final rollups. |
| 11 | **Hardening** | Performance pass, security review, snapshot/restore drill, print stylesheet validation, documentation completion, installer. |

---

## 10. Definition of Done (Per Feature)

A feature is "done" only when **all** of the following are true:
- [ ] Implements the spec without scope creep.
- [ ] Has unit and integration tests; coverage maintained.
- [ ] Writes to the audit log where applicable.
- [ ] Uses digital signatures where applicable.
- [ ] Respects role-based authorization.
- [ ] Honors effective-dating where the feature is configuration-bearing.
- [ ] Any new lockout state has a populated entry in the lock inspector.
- [ ] Has a print-friendly rendering for detail views.
- [ ] Has a user manual section drafted.
- [ ] Passes a manual auditor-perspective walkthrough: "Could I prove this happened to a third-party assessor?"
- [ ] No new compiler warnings; no new linter violations.

---

## 11. Open Questions to Resolve Before Phase 7

These need a stakeholder decision and should be raised in a discovery meeting, not assumed:
1. Exact process recorder file formats in pilot use (Honeywell? Eurotherm? proprietary CSV schema?).
2. Customer list and any customer-specific CoC formatting requirements.
3. Backup destination off the production network drive (USB rotation? secondary NAS?).
4. Concurrent user count expected — drives Shared File Mode vs. Local Service Mode decision (Section 3.1).
5. Whether retired-asset historical references must remain editable for typo fixes or be locked.
6. Customer Concession authorization document format — does it need a specific layout, or is any signed customer document acceptable as an attachment?

---

## 12. Industry Compliance Modules (Optional, Post-v1)

The base ISO 9001:2015 modules above constitute the core product. Industry-specific compliance is delivered as **optional modules** that layer onto the base. Each module:
- Adds clause checklists to the Internal Audit module.
- May add dashboard tiles and Pulse alerts.
- May add fields to existing entities (e.g., AMS 2750 adds pyrometry fields to assets).
- May add new entities (e.g., AS9100 adds FAI records).
- May add new workflows (e.g., AMS 2750 adds TUS/SAT scheduling).
- Is enabled/disabled by Administrator. Disabling hides UI surface but does not delete historical data — records authored under a previously-enabled module remain queryable but read-only.

**Planned optional modules (not in v1 scope):**

| Module | Standard | Adds |
|---|---|---|
| AS9100D | Aerospace QMS | FOD inspection records, FAI (AS9102) forms, counterfeit parts process, configuration management for mid-job revisions, special process control. |
| AMS 2750G | Pyrometry (heat treat) | TUS, SAT, instrument typing, furnace classification, qualified range envelopes, Hi-Limit functional test scheduling, correction-factor application. |
| CQI-9 v4 | Heat Treat System Assessment (AIAG) | Process Table tracking, job audits, system audits, AIAG-specific reporting. |
| IATF 16949 | Automotive QMS | PPAP packages, control plans, APQP gates, customer-specific requirements. |
| Nadcap | Special process accreditation | Self-audit checklists by commodity, evidence packaging, sub-tier flowdown. |

Module loading is plugin-style. The base product must never reference module-specific code; modules reference base interfaces.

---

**End of EasySynQ Coding Project Prompt — Revision 2.**