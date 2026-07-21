# PolymorphicOwned.EntityFrameworkCore

[![CI](https://github.com/RapidataAI/efcore-polymorphic-owned/actions/workflows/ci.yml/badge.svg)](https://github.com/RapidataAI/efcore-polymorphic-owned/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PolymorphicOwned.EntityFrameworkCore.svg)](https://www.nuget.org/packages/PolymorphicOwned.EntityFrameworkCore/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Map a **polymorphic owned value object** — an interface or abstract base with a few concrete
shapes and no identity of its own — to **inline columns** on the owner's table: a discriminator
column plus the union of the subtypes' scalar members as nullable columns. This is the owned-type
inheritance that EF Core [does not support natively](#why-this-exists).

Targets **EF Core 8, 9, and 10**. Provider-agnostic core, validated against **PostgreSQL** (Npgsql)
and **SQLite**.

## Installing

The package is published to the **RapidataAI GitHub Packages** feed on merge to `main` and on
`v*` tags. Add the feed (a repo-local `NuGet.config`), then reference the package:

```xml
<!-- NuGet.config -->
<configuration>
  <packageSources>
    <add key="github-rapidata" value="https://nuget.pkg.github.com/RapidataAI/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-rapidata>
      <add key="Username" value="x-access-token" />
      <!-- A PAT (or Actions GITHUB_TOKEN) with read:packages -->
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github-rapidata>
  </packageSourceCredentials>
</configuration>
```

```
dotnet add package PolymorphicOwned.EntityFrameworkCore
```

Inside RapidataAI CI the ambient `GITHUB_TOKEN` already carries `read:packages`, so no extra secret
is needed to restore.

## The problem

EF Core supports inheritance for **entities** (TPH/TPT/TPC) but **not** for **owned or complex
types**. So a value object like this:

```csharp
public interface IDiscount;

public sealed class PercentageDiscount : IDiscount
{
    public double Percentage { get; set; }
    public double MaxAmount { get; set; }
    public int MinItems { get; set; }
}

public sealed class FixedAmountDiscount : IDiscount
{
    public double Amount { get; set; }
    public double MinOrderTotal { get; set; }
    public int MaxRedemptions { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public IDiscount Discount { get; set; } = default!;   // 1:1 owned, polymorphic
}
```

…can today only be stored by:

1. **JSON** — loses typed columns, indexing, and clean SQL filtering.
2. **Promote to an entity + TPH** — adds an identity, a table, and a join for something that is
   conceptually a value.
3. **Hand-rolled** — a `[NotMapped]` accessor over manually-declared nullable columns and a
   discriminator, flattened and reconstructed by hand in every entity. This is what teams actually
   do, and it is rotting boilerplate.

**This library automates option 3.**

## Usage

Configure the mapping in `OnModelCreating`:

```csharp
modelBuilder.Entity<Order>()
    .OwnsPolymorphic(o => o.Discount, poly =>
    {
        poly.HasDiscriminatorColumn("discount_type");   // default: "<prop>_type"
        poly.HasDerivedType<PercentageDiscount>("percentage");
        poly.HasDerivedType<FixedAmountDiscount>("fixed_amount");

        // Column names default to convention (and cooperate with EFCore.NamingConventions).
        // Override individually if you like:
        poly.Property(d => ((PercentageDiscount)d).MaxAmount).HasColumnName("max_discount_amount");
    });
```

Register the interceptors on the context options:

```csharp
optionsBuilder
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()   // optional; snake_cases the shadow columns too
    .UsePolymorphicOwned();
```

That's it. Insert and read value objects as their concrete types:

```csharp
db.Orders.Add(new Order
{
    Reference = "ORD-1001",
    Discount = new PercentageDiscount { Percentage = 15, MaxAmount = 50, MinItems = 3 },
});
await db.SaveChangesAsync();

var order = await db.Orders.SingleAsync(o => o.Reference == "ORD-1001");
if (order.Discount is PercentageDiscount percentage)
{
    Console.WriteLine(percentage.Percentage);   // 15
}
```

The `orders` table ends up with exactly these columns — no separate table, no JSON
(`max_amount` is shown as `max_discount_amount` if you use the `HasColumnName` override above):

| id | reference | discount_type | percentage | max_amount | min_items | amount | min_order_total | max_redemptions |
|----|-----------|---------------|------------|------------|-----------|--------|-----------------|-----------------|

`dotnet ef migrations add` emits every one of those columns with **no hand-editing** — they are
registered as shadow properties, so they appear in migrations and the model snapshot exactly like
hand-written columns.

## Configuration reference

| Call | Effect |
|------|--------|
| `HasDerivedType<T>("value")` | Registers a concrete subtype and its discriminator value. Required (at least one). |
| `HasDiscriminatorColumn("name")` | Discriminator column name. Default: `<property>_type`. |
| `Property(r => ((T)r).Member).HasColumnName("col")` | Override the column for a subtype member. |
| `IsRequired(bool)` / `IsOptional()` | Whether the owner always has a value. Default: required. |
| `CollapseSharedMembers()` | Members that share a name **and** type across subtypes collapse to one column. |

**Nullability.** The owner is required by default (the discriminator column is `NOT NULL`); mark it
`IsOptional()` to allow a null value object. Subtype-specific columns are always nullable, since only
one subtype's columns are populated per row.

**Shared members.** By default a member that appears on more than one subtype maps to
subtype-qualified columns (`PercentageDiscount_Foo`, `FixedAmountDiscount_Foo`). Call
`CollapseSharedMembers()` to fold same-name-and-type members into a single column.

**Activation.** Subtypes are reconstructed via a constructor whose parameters match member names
(records / immutable value objects), otherwise via a parameterless constructor and property setters.
Get-only auto-properties are supported (written through the backing field).

## Querying

### Filter and sort — strongly-typed, on the flattened columns

Cast to a subtype and access a member, or use an `is` check, and it translates to SQL against the
underlying column — no magic strings:

```csharp
// ((TSub)owner.Nav).Member  ->  the member's column
var bigPercentageOff = await db.Orders
    .Where(o => ((PercentageDiscount)o.Discount).Percentage >= 10)
    .OrderByDescending(o => ((PercentageDiscount)o.Discount).Percentage)
    .ToListAsync();

// owner.Nav is TSub  ->  the discriminator column
var fixedDiscounts = await db.Orders
    .Where(o => o.Discount is FixedAmountDiscount)
    .ToListAsync();
```

These become plain column predicates (rows of another subtype have a `NULL` in that column, so a
comparison is simply `false` — the cast never runs, so no `InvalidCastException`):

```sql
WHERE o.percentage >= 10          -- ((PercentageDiscount)o.Discount).Percentage >= 10
WHERE o.discount_type = 'fixed_amount'   -- o.Discount is FixedAmountDiscount
```

The raw shadow properties are also addressable with `EF.Property<T>(o, "Percentage")` if you prefer
strings, but the cast form above is refactor-safe.

### Project the reconstructed value object

Just access the property in a projection — `o.Discount` — to get the reconstructed value object
**without materializing the whole owner** and **without a hand-written `EF.Property` per member**:

```csharp
var items = await db.Orders
    .Where(o => o.Discount is PercentageDiscount)
    .Select(o => new OrderListItem
    {
        Id = o.Id,
        Reference = o.Reference,
        Discount = o.Discount,   // reconstructed, typed
    })
    .ToListAsync();

if (items[0].Discount is PercentageDiscount p) { /* ... */ }
```

The query interceptor (registered by `UsePolymorphicOwned()`) rewrites the `o.Discount` access to
select the discriminator + that mapping's flattened columns and reconstruct the concrete subtype in
the projection shaper — so the SQL is just those columns, the owner entity is neither selected in
full nor tracked, and the mapping knowledge stays in the library. The generated SQL for the `Select`
above is, e.g.:

```sql
SELECT o."Id", o."Reference", o.discount_type, o.percentage, o.max_amount, o.min_items,
       o.amount, o.min_order_total, o.max_redemptions
FROM orders AS o
WHERE o.discount_type = 'percentage'
```

Access the property only in the **final** `Select` projection (it reconstructs client-side, so it
can't appear inside a `Where`/`OrderBy`). For an optional owner with no value it yields `null`.

### The remaining boundary

What translates to SQL is anything that maps onto the flattened columns: a subtype-member access
through a cast (`((TSub)o.Nav).Member`), an `is TSub` check, and `EF.Property<T>`. What does **not** is
running arbitrary logic on the *reconstructed* object inside the query — a method call on it, or a
member access without a cast (the base type has no members) — because the object only exists after
materialization. Access via the subtype cast (translated) or project the object and operate on it in
memory.

## Non-goals

- **Not** a replacement for entity inheritance (TPH/TPT/TPC). Those are for entities with identity;
  this is for value objects without.
- **Not** a JSON mapper. If you want the value stored as a JSON document, use EF's built-in
  `ToJson()` / owned-JSON support instead.
- **Not** (in v1) full LINQ translation of the polymorphic property — see the boundary above.

## Runnable sample

[`samples/PolymorphicOwned.Sample`](samples/PolymorphicOwned.Sample) models exactly
`Order { Discount: PercentageDiscount | FixedAmountDiscount }` against PostgreSQL, including a
committed auto-generated migration.

```bash
# Start a Postgres (or point POSTGRES_CONNECTION at your own):
docker run -d --name poly-pg -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=polymorphic_owned_sample -p 5432:5432 postgres:16-alpine

dotnet run --project samples/PolymorphicOwned.Sample
```

Output:

```
#1 ORD-1001   -> PercentageDiscount(15% up to 50, minItems=3)
#2 ORD-1002   -> FixedAmountDiscount(20 off over 100, maxRedemptions=1000)
Orders with a percentage discount >= 10%: ORD-1001
```

## Why this exists

See [docs/adr/0001-shadow-property-and-interceptor-approach.md](docs/adr/0001-shadow-property-and-interceptor-approach.md)
for the architecture: why owned-type inheritance can't be done natively, and why shadow properties
(model building) plus a materialization interceptor (read) and a save-changes interceptor (write)
are the approach.

## Development

```bash
dotnet build
dotnet test          # round-trip tests use Testcontainers (Docker required) for Postgres + SQLite
```

## License

MIT — see [LICENSE](LICENSE).
