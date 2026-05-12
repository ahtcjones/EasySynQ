# ADR 0001 — Stack Choice and Foundational Constraints

**Status:** Accepted
**Date:** 2026-05-11
**Supersedes:** None

---

## Context

EasySynQ is a desktop Quality Management System deployed in a small-to-mid-sized industrial facility on a closed corporate network. The application must be auditor-ready for ISO 9001:2015 compliance from day one, with industry-specific compliance as future modules.

The deployment environment has specific properties that shaped the technology choices:

- Windows-based workstations
- Shared network drive available
- No cloud connectivity assumed or required
- Small concurrent user count (estimated 4–8 at pilot)
- Auditor walkthroughs are part of acceptance — "could I prove this to a third-party assessor?" is a real test
- A single Quality Manager will be the primary administrator; deep IT expertise is not on staff

## Decision

### Language and runtime
- **C# on the latest stable .NET LTS release.** Strict nullability enabled.

### UI
- **WPF with the MVVM pattern.** No code-behind logic.
- Rationale: WPF is mature, Windows-native, well-supported on the target deployment OS, and produces installable applications without runtime gymnastics.

### Persistence
- **SQLite** as the database, with WAL mode enabled.
- **Single shared database file** on a mapped network drive (default Shared File Mode).
- **Local Service Mode** designed in from day one as a fallback: a small .NET service owns the SQLite file locally and serves WPF clients over gRPC or named pipes. The Data layer is built behind a repository interface so the swap is configuration-driven.

### ORM
- **Entity Framework Core OR Dapper.** Pick one at Phase 1 and stay consistent.
- Rationale: deferred at this ADR level. The decision will be recorded in a follow-up ADR once Phase 1 implementation begins.

### Document Vault
- **Content-addressed storage.** SHA-256 of file content is the filename. Flat (or 2-char sharded) directory. Database holds all human-readable paths.

### PDF
- **QuestPDF** for generation.
- **PdfiumViewer, WebView2, or equivalent** for viewing. Choice is implementation detail; the constraint is no external app opens.

### Charts
- **LiveCharts2** is the initial choice, with a re-evaluation gate at Phase 10 (Data Intelligence). If a lighter SVG-based approach covers the dashboard needs by then, prefer it.

### Logging
- **Serilog** with rolling file sink. Structured logging.

### Testing
- **xUnit + Moq + FluentAssertions.** Target ≥80% line coverage on Domain and Services projects.

### Packaging
- **MSIX or ClickOnce.** Centralized version pin.

---

## Alternatives Considered

### Database: PostgreSQL or SQL Server instead of SQLite
- **Why considered:** Better concurrency story, more familiar to many .NET developers.
- **Why rejected:** Introduces an operational dependency (a running database server) that a small shop without IT staff cannot reliably maintain. The "no cloud, no extra moving parts" deployment constraint dominates.

### UI: Avalonia, MAUI, or Web (Blazor)
- **Avalonia:** Cross-platform, but adds complexity for a Windows-only deployment. Smaller ecosystem.
- **MAUI:** Less mature on desktop. Mobile-first heritage shows.
- **Web (Blazor Server / WASM):** Would require a hosted backend. Conflicts with the no-cloud, network-drive-only deployment.
- **WPF chosen:** Best fit for Windows-native installable desktop with a strong MVVM story.

### Document Vault: hierarchical folder structure
- **Why considered:** More intuitive for humans browsing the filesystem.
- **Why rejected:** Forces filesystem migrations every time you want to reorganize. Tamper-evidence is weaker. Content-addressed gives free deduplication and a tamper-evident filename. Rev 1 of the spec used the hierarchical approach; Rev 2 changed it.

### Snapshot retention: simple 90-day rolling
- **Why considered:** Simpler.
- **Why rejected:** ISO 9001 and customer audit retention requirements typically span 3–7 years. Tiered retention (90 days daily, 1 year weekly, indefinite monthly) covers this cheaply.

### Authentication: Active Directory / Azure AD
- **Why considered:** Already deployed at many customer sites.
- **Why rejected:** The "no third-party identity providers" rule comes from the deployment context — the system must remain functional even when network identity services are unavailable, and the audit-log identity story needs to be self-contained for compliance defensibility.

---

## Consequences

### Positive
- Single-file database is dramatically simpler to back up, snapshot, and restore than a hosted DBMS.
- WPF + WAL SQLite gives a robust offline-capable experience with minimal operational footprint.
- The repository-interface design from day one means Plan B (Local Service Mode) is a configuration swap, not a rewrite.
- Content-addressed Vault gives tamper-evidence and deduplication for free.

### Negative (and accepted)
- SQLite over SMB is fragile under high concurrency. **Mitigation:** Local Service Mode designed in from day one; concurrency tested at Phase 11 (Hardening).
- WPF locks us to Windows. **Mitigation:** Acceptable — pilot and target customers are Windows shops; the cost of cross-platform support outweighs the benefit.
- Self-managed authentication is one more thing to get right (password hashing, brute-force protection, session management). **Mitigation:** Use PBKDF2 or Argon2id, follow OWASP recommendations, document the auth model in a separate ADR before Phase 1 ships.

### Decisions deferred to future ADRs
- ORM choice (EF Core vs Dapper) — ADR 0002, before Phase 1 implementation.
- Authentication implementation specifics (PBKDF2 vs Argon2id, iteration counts, salt size) — ADR 0003, before Phase 1 ships.
- Packaging choice (MSIX vs ClickOnce) — ADR 0004, before Phase 11 (Hardening).

---

## References

- `docs/SPEC.md` §2 — Technology Stack & Foundational Constraints
- `docs/SPEC.md` §3 — System Architecture (Local Service Mode rationale)
- `docs/SPEC.md` §3.6 — Document Vault content-addressed storage
- `docs/SPEC.md` §3.7 — Effective Dating