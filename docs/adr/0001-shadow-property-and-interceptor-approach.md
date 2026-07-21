# ADR 0001 — Shadow properties + interceptors for polymorphic owned value objects

- Status: Accepted
- Date: 2026-07-21

## Context

We need to persist a **polymorphic owned value object** — an interface or abstract base with a
handful of concrete shapes, owned 1:1 by an entity and having no identity of its own — as **inline
typed columns** on the owner's table (a discriminator column + the union of the subtypes' scalar
members as nullable columns).

EF Core cannot do this natively:

- **Entity inheritance (TPH/TPT/TPC)** applies only to entity types, which have keys and their own
  change-tracking identity. A value object has neither; promoting it to an entity introduces a
  surrogate key, a table (or shared table + discriminator plumbing), and a relationship to manage —
  semantics we explicitly do not want.
- **Owned types / complex types do not support inheritance.** `OwnsOne`/`ComplexProperty` require a
  single concrete CLR type; you cannot map an interface or abstract base with alternative concrete
  shapes. This is a long-standing EF limitation, not an oversight we can configure around.
- **`ToJson()`** stores the value as a JSON document, not typed columns — losing column-level types,
  indexing, and straightforward SQL filtering.

The remaining option teams use in practice is hand-rolled: declare the union of columns plus a
discriminator by hand, expose the value object through a `[NotMapped]` accessor, and flatten /
reconstruct manually in every entity. It works but is pure boilerplate that rots as shapes change.
This library automates exactly that pattern.

## Decision

Three cooperating mechanisms, split along EF's own lifecycle boundaries:

### 1. Model building — shadow properties

`OwnsPolymorphic(...)` runs during `OnModelCreating` and, on the owner entity type:

- `Ignore`s the CLR navigation (EF cannot and must not try to map the interface/abstract property).
- Registers a **discriminator shadow property** and, for the union of the subtypes' scalar members,
  one **nullable shadow property** each — as real model properties.

Because these are ordinary shadow properties, they flow into migrations and the model snapshot
**exactly like hand-written columns**, with no design-time services or snapshot post-processing.
This is the make-or-break requirement: `dotnet ef migrations add` must emit the columns unedited.

Column names are left to convention unless overridden with `HasColumnName`, so
`EFCore.NamingConventions` (snake_case) rewrites them alongside the rest of the entity.

The serialized mapping (subtypes, discriminator values, member→shadow map) is stored as a single
**string annotation** on the model. A string keeps the snapshot's annotation generator happy (it
renders as a plain literal); the reflection-heavy runtime mapping is rebuilt from it once per model
and cached in a `ConditionalWeakTable` keyed on the finalized model.

### 2. Materialization — `IMaterializationInterceptor`

After EF materializes an owner, `InitializedInstance` reads the discriminator + shadow values from
the materialization data, constructs the correct concrete subtype (constructor-matching for
records/immutables, otherwise setters / backing fields), and assigns it to the ignored CLR property.

### 3. Persistence — `ISaveChangesInterceptor`

`SavingChanges` flattens each tracked owner's current value object back into the shadow columns: set
the discriminator, populate the active subtype's columns, null the rest. Values are recomputed on
**every** save and written through the entry API, so:

- an unchanged value object produces the same values → no modification → **no spurious UPDATE**;
- a mutated member or a swapped subtype differs → the entry is marked modified → the correct UPDATE.

This sidesteps the fact that EF's own change detection never sees the value object (it lives on an
ignored property).

## Consequences

**Positive**

- Columns are first-class in migrations and snapshots; nothing to hand-edit.
- Reads/writes are transparent — consumers use the value object as plain CLR objects.
- Provider-agnostic (validated on PostgreSQL and SQLite); cooperates with naming conventions.

**Negative / limits**

- **No translation of logic run on the reconstructed object** (method calls on it, cast-less member
  access). Column-mapped forms *do* translate — `((TSub)x.Nav).Member`, `x.Nav is TSub`, and
  `EF.Property<T>(...)` for filter/sort — and the object can be *projected* (see below); only running
  arbitrary code against the materialized object inside the query is out of scope.
- Reconstruction uses reflection. Values are read with a cached closed-generic accessor and subtype
  activation is planned once per subtype, but it is reflection nonetheless (not compiled delegates).
- Interceptors are registered explicitly via `UsePolymorphicOwned()`; forgetting it means the value
  object is never populated or persisted.

### 4. Projection — `IQueryExpressionInterceptor` (added in 0.2.0)

Reading the reconstructed object inside a projection previously forced consumers to either hand-write
one `EF.Property<T>` per member (verbose, duplicating the mapping) or project the whole owner (over-
materializing). Accessing the property directly — `o.Discount` in a `Select` — now works: a
query-expression interceptor rewrites that member access, at query-compilation time, into a
projection of the discriminator + that mapping's flattened columns (`EF.Property<T>` reads) wrapped
in a client-side `Rebuild`. EF translates the column reads to SQL and runs the reconstruction in the
projection shaper, so only the owned value's columns are selected and no entity is tracked. The
reconstruction logic stays in the library (keyed by a per-mapping id in a small registry) rather than
being re-implemented per consumer.

Rewriting the raw member access (rather than a dedicated marker method) is safe precisely because the
navigation is `Ignore`d in the model: EF can never translate `o.Discount` on its own, so there is no
existing behaviour to collide with — the interceptor is the only thing that gives it meaning.

The same interceptor also rewrites the two query forms that *do* map onto columns, so filtering and
sorting stay strongly-typed instead of stringly-typed `EF.Property`:

- `((TSub)owner.Nav).Member` → `EF.Property<TShadow>(owner, shadowName)` (the member's column). The
  result is wrapped back to the member's declared type so surrounding operators keep their types; the
  cast never executes, so no `InvalidCastException` for other-subtype rows (their column is `NULL`).
- `owner.Nav is TSub` → `discriminator == "<value>"`.

Both are detected before the projection rewrite so the inner `owner.Nav` isn't turned into a
reconstruction. What remains out of scope is running logic on the *reconstructed* object in the query
(method calls, cast-less member access) — that still only exists post-materialization, so the object
itself is reconstructed only in a final `Select`.
- Reconstruction uses reflection. Values are read with a cached closed-generic accessor and subtype
  activation is planned once per subtype, but it is reflection nonetheless (not compiled delegates).
- Interceptors are registered explicitly via `UsePolymorphicOwned()`; forgetting it means the value
  object is never populated or persisted.

## Alternatives considered

- **`IConventionSetPlugin` / model-finalizing convention** to add the shadow properties: viable, but
  adding them directly in the fluent call is simpler and equally correct for migration output. A
  convention is the natural home if we later add automatic shape discovery.
- **Runtime annotations** instead of a string annotation for the runtime mapping: cleaner in theory
  but awkward to populate from `OnModelCreating`; the string annotation + weak-table cache is
  simpler and snapshot-safe.
- **Compiled expression accessors** instead of reflection: a performance optimization deferred until
  there is a measured need.
