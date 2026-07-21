namespace PolymorphicOwned.EntityFrameworkCore.Configuration;

/// <summary>
/// Configures the column a single subtype member flattens to. Returned by
/// <see cref="PolymorphicOwnedBuilder{TValue}.Property"/>.
/// </summary>
public sealed class PolymorphicMemberBuilder
{
    private readonly MemberOverride _override;

    internal PolymorphicMemberBuilder(MemberOverride memberOverride)
    {
        _override = memberOverride;
    }

    /// <summary>Sets an explicit column name, overriding convention (and naming-convention plugins).</summary>
    public PolymorphicMemberBuilder HasColumnName(string columnName)
    {
        _override.ColumnName = columnName;
        return this;
    }
}

/// <summary>Mutable record of a per-member column override, keyed by (subtype, member name).</summary>
internal sealed class MemberOverride
{
    public required Type Subtype { get; init; }

    public required string MemberName { get; init; }

    public string? ColumnName { get; set; }
}
