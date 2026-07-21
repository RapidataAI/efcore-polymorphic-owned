using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.Sample;

// Round-trips the motivating example against Postgres:
//   Order { Discount: PercentageDiscount | FixedAmountDiscount }
// Set POSTGRES_CONNECTION to point at your database (defaults to localhost:5432).

await using var db = new OrderDbContext(SampleOptions.Build().Options);

await db.Database.MigrateAsync();
await db.Orders.ExecuteDeleteAsync();

db.Orders.AddRange(
    new Order
    {
        Reference = "ORD-1001",
        Discount = new PercentageDiscount
        {
            Percentage = 15,
            MaxAmount = 50,
            MinItems = 3,
        },
    },
    new Order
    {
        Reference = "ORD-1002",
        Discount = new FixedAmountDiscount
        {
            Amount = 20,
            MinOrderTotal = 100,
            MaxRedemptions = 1000,
        },
    });

await db.SaveChangesAsync();

// Fresh context so nothing is served from the identity map — this proves materialization.
await using var readContext = new OrderDbContext(SampleOptions.Build().Options);
var orders = await readContext.Orders.OrderBy(o => o.Id).ToListAsync();

foreach (var order in orders)
{
    var description = order.Discount switch
    {
        PercentageDiscount percentage =>
            $"PercentageDiscount({percentage.Percentage}% up to {percentage.MaxAmount}, minItems={percentage.MinItems})",
        FixedAmountDiscount fixedAmount =>
            $"FixedAmountDiscount({fixedAmount.Amount} off over {fixedAmount.MinOrderTotal}, maxRedemptions={fixedAmount.MaxRedemptions})",
        _ => order.Discount.GetType().Name,
    };

    Console.WriteLine($"#{order.Id} {order.Reference,-10} -> {description}");
}

// Server-side filter on a flattened column (see the query-limitation caveat in the README).
var bigPercentageOff = await readContext.Orders
    .Where(o => EF.Property<double?>(o, "Percentage") >= 10)
    .Select(o => o.Reference)
    .ToListAsync();

Console.WriteLine($"Orders with a percentage discount >= 10%: {string.Join(", ", bigPercentageOff)}");
