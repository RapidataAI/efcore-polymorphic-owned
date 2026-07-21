using System.Reflection;

namespace PolymorphicOwned.EntityFrameworkCore.Internal;

/// <summary>
/// Reads and writes a single CLR member of a subtype instance. Writes fall back to the
/// compiler-generated backing field so get-only auto-properties (records, immutable value
/// objects) can still be populated during materialization.
/// </summary>
internal sealed class MemberBinding
{
    private readonly PropertyInfo _property;
    private readonly FieldInfo? _backingField;

    public MemberBinding(PropertyInfo property)
    {
        _property = property;
        MemberName = property.Name;
        MemberType = property.PropertyType;

        if (!property.CanWrite)
        {
            _backingField = property.DeclaringType?.GetField(
                $"<{property.Name}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    public string MemberName { get; }

    public Type MemberType { get; }

    public bool CanWrite => _property.CanWrite || _backingField is not null;

    public object? Read(object instance) => _property.GetValue(instance);

    public void Write(object instance, object? value)
    {
        var coerced = ValueCoercion.Coerce(value, MemberType);
        if (_property.CanWrite)
        {
            _property.SetValue(instance, coerced);
        }
        else
        {
            _backingField?.SetValue(instance, coerced);
        }
    }
}
