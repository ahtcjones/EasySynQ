# EasySynQ

A Quality Management System for ISO 9001:2015 compliance. Domain-agnostic at its core, with industry-specific compliance (AS9100, AMS 2750, IATF 16949, CQI-9) delivered as optional modules.

**Status:** Pre-development. Specification and UI prototype complete.

## Documents

- [`docs/SPEC.md`](docs/SPEC.md) — Full specification (Revision 3)
- [`docs/UI_PROTOTYPE.html`](docs/UI_PROTOTYPE.html) — Interactive UI prototype. Open in a browser.
- [`docs/decisions/`](docs/decisions/) — Architectural Decision Records

## Stack

- C# / .NET LTS
- WPF (MVVM)
- SQLite (WAL mode, content-addressed Document Vault)
- Entity Framework Core or Dapper
- QuestPDF · Serilog · xUnit

See `docs/SPEC.md` §2 for the full stack and constraints.

## Building

_To be added as Phase 1 (Foundation) completes._

## License

Proprietary. All rights reserved.