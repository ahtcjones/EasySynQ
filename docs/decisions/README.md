# Architectural Decision Records (ADRs)

This folder contains records of significant architectural decisions made during the project.

## Why ADRs

Decisions get made, code gets written, and six months later nobody remembers *why* something was done a particular way. ADRs are short, dated documents that capture:

- The decision that was made
- The context that made it necessary
- The alternatives that were considered
- The consequences (good and bad) of choosing this path

When a future change forces a decision to be revisited, the ADR tells you what you'd be undoing.

## Format

One file per decision, numbered sequentially:

```
0001-stack-choice.md
0002-document-vault-content-addressed.md
0003-effective-dating-pattern.md
```

Each ADR includes:

- **Status:** Proposed · Accepted · Superseded · Deprecated
- **Context:** What's the situation that forces a decision?
- **Decision:** What did we decide?
- **Alternatives considered:** What else did we look at, and why didn't we pick it?
- **Consequences:** What does this decision lock us into?

If a later ADR supersedes an earlier one, update the earlier one's status and link forward to the new one. **Do not delete superseded ADRs** — the history is the value.