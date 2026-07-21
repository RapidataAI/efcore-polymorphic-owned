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

- **No LINQ translation of the reconstructed object** (`x.Rule is T`, member access). Querying is
  against the flattened shadow properties via `EF.Property<T>(...)`. This is the deliberate v1
  boundary.
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
