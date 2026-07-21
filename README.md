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
types**. So a value object like this (the base may be an interface **or** an abstract class):

```csharp
public abstract class GraduationRule;

public sealed class ScoreThresholdRule : GraduationRule
{
    public double GraduationScore { get; set; }
    public double DemotionScore { get; set; }
    public int MinResponsesToGraduate { get; set; }
}

public sealed class TaskAccuracyRule : GraduationRule
{
    public double TargetAccuracy { get; set; }
    public int MinTasks { get; set; }
    public int MaxTasks { get; set; }
}

public sealed class Audience
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public GraduationRule GraduationRule { get; set; } = default!;   // 1:1 owned, polymorphic
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
modelBuilder.Entity<Audience>()
    .OwnsPolymorphic(a => a.GraduationRule, poly =>
    {
        poly.HasDiscriminatorColumn("graduation_rule_type");   // default: "<prop>_type"
        poly.HasDerivedType<ScoreThresholdRule>("score_threshold");
        poly.HasDerivedType<TaskAccuracyRule>("task_accuracy");

        // Column names default to convention (and cooperate with EFCore.NamingConventions).
        // Override individually if you like:
        poly.Property(r => ((ScoreThresholdRule)r).GraduationScore).HasColumnName("graduation_score");
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
db.Audiences.Add(new Audience
{
    Name = "Reliable reviewers",
    GraduationRule = new ScoreThresholdRule { GraduationScore = 0.85, DemotionScore = 0.4, MinResponsesToGraduate = 50 },
});
await db.SaveChangesAsync();

var audience = await db.Audiences.SingleAsync(a => a.Name == "Reliable reviewers");
if (audience.GraduationRule is ScoreThresholdRule score)
{
    Console.WriteLine(score.GraduationScore);   // 0.85
}
```

The `audiences` table ends up with exactly these columns — no separate table, no JSON:

| id | name | graduation_rule_type | graduation_score | demotion_score | min_responses_to_graduate | target_accuracy | min_tasks | max_tasks |
|----|------|----------------------|------------------|----------------|---------------------------|-----------------|-----------|-----------|

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
subtype-qualified columns (`SubtypeA_Foo`, `SubtypeB_Foo`). Call `CollapseSharedMembers()` to fold
same-name-and-type members into a single column.

**Activation.** Subtypes are reconstructed via a constructor whose parameters match member names
(records / immutable value objects), otherwise via a parameterless constructor and property setters.
Get-only auto-properties are supported (written through the backing field).

## Querying: the hard boundary

**v1 does not translate the reconstructed object in LINQ.** EF cannot translate
`a.GraduationRule is TaskAccuracyRule` or member access on the polymorphic property to SQL — the
value object only exists after materialization.

What you **can** do is filter, sort, and project against the flattened columns server-side, because
they are real (shadow) properties:

```csharp
// Translated to SQL against the real column. Rows of other subtypes have a NULL there.
var strictScoreGates = await db.Audiences
    .Where(a => EF.Property<double?>(a, "GraduationScore") >= 0.8)
    .ToListAsync();

// The discriminator is queryable too:
var accuracyGated = await db.Audiences
    .Where(a => EF.Property<string>(a, "graduation_rule_type") == "task_accuracy")
    .ToListAsync();
```

`EF.Property<T>(entity, name)` takes the **property name** (the member name, e.g. `GraduationScore`),
which maps to the configured/conventioned column (`graduation_score`). Strongly-typed query helpers
are a stretch goal, not part of v1.

## Non-goals

- **Not** a replacement for entity inheritance (TPH/TPT/TPC). Those are for entities with identity;
  this is for value objects without.
- **Not** a JSON mapper. If you want the value stored as a JSON document, use EF's built-in
  `ToJson()` / owned-JSON support instead.
- **Not** (in v1) full LINQ translation of the polymorphic property — see the boundary above.

## Runnable sample

[`samples/PolymorphicOwned.Sample`](samples/PolymorphicOwned.Sample) models exactly
`Audience { GraduationRule: ScoreThresholdRule | TaskAccuracyRule }` against PostgreSQL, including a
committed auto-generated migration.

```bash
# Start a Postgres (or point POSTGRES_CONNECTION at your own):
docker run -d --name poly-pg -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=polymorphic_owned_sample -p 5432:5432 postgres:16-alpine

dotnet run --project samples/PolymorphicOwned.Sample
```

Output:

```
#1 Reliable reviewers   -> ScoreThresholdRule(graduate>=0.85, demote<0.4, minResponses=50)
#2 Accurate labelers    -> TaskAccuracyRule(target=0.95, tasks 20..200)
Audiences with a graduation score >= 0.8: Reliable reviewers
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
