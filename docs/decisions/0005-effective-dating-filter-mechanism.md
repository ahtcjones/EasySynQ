# ADR 0005 — Effective-Dating Filter Mechanism

**Status:** Accepted
**Date:** 2026-05-12
**Supersedes:** None
**Relates to:** ADR 0003 (ORM choice)

---

## Context

SPEC §3.7 requires every effective-dated query to evaluate against an "as of" instant resolved by `ITemporalResolver`. The data layer implements this via an EF Core global query filter on every entity that implements `IEffectiveDated`. The filter must read the resolver **at query execution time**, not at model-build time — otherwise a single-instance application that swaps between "current evaluation" and "historical evaluation" scopes would see stale, snapshotted-at-startup values from the historical-evaluation queries.

The naive approach is to write a small helper that takes the resolver as a parameter and produces filter lambdas referencing it:

```csharp
// PATTERN THAT LOOKS RIGHT AND IS SILENTLY WRONG
public static class EffectiveDatingQueryConfigurator
{
    public static void Apply(ModelBuilder modelBuilder, ITemporalResolver resolver)
    {
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur =>
            ur.EffectivePeriod.EffectiveFromUtc <= resolver.AsOfUtc && ...);
    }
}
```

This compiles, runs, and produces correct results **for the first value** the resolver ever has. After that, mutating `resolver.AsOfUtc` (or replacing the resolver with a historical one) has no observable effect on queries. The data is silently stale.

The bug was caught during Chunk B implementation by the `ResolverMutation_IsObservedLiveAtQueryTime_NotSnapshottedAtModelBuild` integration test, which mutates the resolver mid-test and asserts that two queries against the same compiled model return different result sets. A naive "set up filter, run one query" test would not have caught it.

## Decision

**Query filters on `IEffectiveDated` entities are written inline in `EasySynQDbContext.OnModelCreating`, referencing a private `AsOfUtc` property on the DbContext.** Static helpers that take the resolver as a parameter and produce filter lambdas are forbidden.

```csharp
public class EasySynQDbContext : DbContext
{
    private readonly ITemporalResolver _temporalResolver;

    // DbContext member access — EF Core's expression visitor recognizes
    // this and parameterizes the value at every query execution.
    private DateTime AsOfUtc => _temporalResolver.AsOfUtc;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur =>
            ur.EffectivePeriod.EffectiveFromUtc <= AsOfUtc
            && (ur.EffectivePeriod.EffectiveToUtc == null
                || AsOfUtc < ur.EffectivePeriod.EffectiveToUtc));
    }
}
```

## Why this works (and why the helper doesn't)

EF Core's query-filter pipeline classifies expression-tree references into two categories:

1. **Closure-captured variables and parameters** — flattened to a `ConstantExpression` carrying the captured value at expression-tree creation time. EF Core treats these as fixed at model-build and inlines them as SQL literals (or, after parameterization, as parameters with a single bound value across the model's lifetime).

2. **Member access on the DbContext** — recognized as `MemberAccessExpression` over a `ParameterExpression` typed as the DbContext. EF Core's filter pipeline specifically binds this parameter to the running context at query execution and re-reads the member value every time.

The static-helper pattern produces case (1): the lambda is created inside a static method, the resolver is a closure-captured local, EF flattens the value at model-build time. The inline-on-DbContext pattern produces case (2): the lambda references `this.AsOfUtc`, which compiles to `MemberAccessExpression(ParameterExpression(DbContext), AsOfUtc)`, which EF re-evaluates per query.

This is documented behavior in EF Core 8+ but is **not obvious from the API surface** — both patterns type-check, compile, and produce correct results in single-resolver-value tests. The trap is subtle and high-impact.

## How we proved it works

`EffectiveDatingFilterTests.ResolverMutation_IsObservedLiveAtQueryTime_NotSnapshottedAtModelBuildAsync` (in `EasySynQ.Tests/Integration/Data/Interceptors/`) is the canary:

1. Inserts two `UserRole` rows with non-overlapping effective periods (`spring2024` and `fall2024`).
2. Sets `TemporalResolver.AsOfUtc` to a spring instant. Queries → expects `spring2024` only. ✓
3. **Mutates `TemporalResolver.AsOfUtc`** to a fall instant — same `DbContextOptions`, same cached model, same temporal resolver instance, only the field value changed.
4. Queries again → expects `fall2024` only. ✓ under the inline pattern; ✗ under the static-helper pattern.

A naive "set up filter, query, assert" test would have set the resolver once before any query and asserted the correct answer. That test passes under both patterns. The bug only surfaces when the resolver value changes between queries, which the mutable-resolver test exercises explicitly.

**Lesson for future filter-related tests:** any test that asserts a filter behavior must include a "change the input, query again, observe the change" step. Single-value assertion is not sufficient evidence of live evaluation.

## Forbidden pattern (do not introduce in future)

Do **not** write any helper, extension method, or convention that builds an EF Core filter lambda capturing external state. Specifically forbidden:

```csharp
// FORBIDDEN — closure capture of a parameter
public static void ApplyFilter<T>(ModelBuilder mb, ITemporalResolver resolver) { ... }

// FORBIDDEN — closure capture of a field on another class
public class FilterConventions
{
    private readonly ITemporalResolver _resolver;
    public void Apply(ModelBuilder mb) {
        mb.Entity<T>().HasQueryFilter(e => ... _resolver.AsOfUtc ...);
    }
}

// FORBIDDEN — Func<T> indirection still captures externally
public static void Apply(ModelBuilder mb, Func<DateTime> asOfProvider) { ... }
```

If a future entity becomes `IEffectiveDated`, its filter goes inline in `EasySynQDbContext.OnModelCreating` next to `UserRole`'s, referencing the same `AsOfUtc` private property. The minor duplication is far cheaper than the alternative debugging cost.

## Alternatives Considered

### Make the resolver immutable per DbContext, accept snapshot semantics
- **Why considered:** Sidesteps the bug — if the resolver never mutates, snapshotting is fine.
- **Why rejected:** Historical-evaluation scopes (SPEC §3.7's whole point) require swapping the resolver mid-process. An immutable resolver per context still works if every historical scope creates a new DbContext, but that's brittle: anyone who forgets and reuses a context gets silent stale results. The live-resolver semantics are the safe default.

### Build the filter expression tree manually with explicit `ParameterExpression`
- **Why considered:** Lets a static helper produce the right expression shape (parameter access on DbContext) rather than a closure.
- **Why rejected:** Substantially more code, hard to maintain, and the inline approach in the DbContext is one line per entity type. Worth it only if we had dozens of `IEffectiveDated` entities; we have one (Phase 1) and add maybe 5–10 over the product's life.

### Use a custom `IModelCustomizer` to apply filters
- **Why considered:** EF Core has an extension point for model customization that runs once per options and has access to the DbContext type.
- **Why rejected:** Still requires the lambda to reference the DbContext (via `this`) to get live values. Adds an indirection layer without solving the underlying problem.

## Consequences

### Positive
- Filter evaluation is live; historical scopes work as documented in SPEC §3.7.
- The pattern is one-line-per-entity inside the DbContext — discoverable, reviewable, hard to get wrong because the IDE shows `AsOfUtc` is a DbContext member.
- The cache-friendly model build still works: the compiled expression tree is shared across DbContext instances; only the value bound to the DbContext parameter differs per query.

### Negative (and accepted)
- Adding a new `IEffectiveDated` entity requires editing `EasySynQDbContext.OnModelCreating` — not just dropping a configuration class in `Configurations/`. Documented in the DbContext's class remarks so future contributors don't reach for the configurator pattern.
- The forbidden pattern is enforceable only via code review for now. If we accumulate more filter-style conventions, a Roslyn analyzer that flags closure captures inside `HasQueryFilter` calls would be worth the cost.

## References

- `docs/SPEC.md` §3.7 — Effective Dating
- ADR 0003 — ORM Choice (chose EF Core; this ADR pins one design rule the ORM enables but doesn't enforce)
- `src/EasySynQ.Data/Context/EasySynQDbContext.cs` — the canonical pattern, inline in `OnModelCreating`
- `src/EasySynQ.Tests/Integration/Data/Interceptors/EffectiveDatingFilterTests.cs` — the test that catches regressions
