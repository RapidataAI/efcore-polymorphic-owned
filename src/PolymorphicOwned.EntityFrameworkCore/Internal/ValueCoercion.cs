namespace PolymorphicOwned.EntityFrameworkCore.Internal;

/// <summary>
/// Coerces a value read from a shadow property (materialization) into the exact CLR type a
/// member/constructor-parameter expects. Values usually arrive already typed; this smooths over
/// nullable wrapping, enum backing, and the widening the provider occasionally applies.
/// </summary>
internal static class ValueCoercion
{
    public static object? Coerce(object? value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (value is null)
        {
            // A non-nullable value-type target with no stored value means the column was NULL
            // for the active subtype; fall back to default rather than throwing on Invoke.
            return target.IsValueType && Nullable.GetUnderlyingType(target) is null
                ? Activator.CreateInstance(target)
                : null;
        }

        var valueType = value.GetType();
        if (underlying.IsInstanceOfType(value) || valueType == underlying)
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            return Enum.ToObject(underlying, value);
        }

        if (value is IConvertible)
        {
            return Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }
}
