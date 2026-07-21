using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymorphicOwned.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Query;

/// <summary>
/// Lets a polymorphic owned value object be read straight from a projection — <c>o.Discount</c> in a
/// <c>Select</c> — by rewriting that (otherwise unmapped) member access, at query-compilation time,
/// into a projection of the discriminator + the mapping's flattened columns followed by a client-side
/// <see cref="PolymorphicProjection.Rebuild"/>. EF translates the <see cref="EF.Property{TProperty}"/>
/// reads to columns and runs the reconstruction in the projection shaper, so only the owned value's
/// columns are read and the owner is neither fully materialized nor tracked.
/// </summary>
public sealed class PolymorphicProjectionInterceptor : IQueryExpressionInterceptor
{
    private static readonly MethodInfo EfPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo RebuildMethod =
        typeof(PolymorphicProjection).GetMethod(nameof(PolymorphicProjection.Rebuild))!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        var model = eventData.Context?.Model;
        if (model is null)
        {
            return queryExpression;
        }

        var info = PolymorphicModelInfo.For(model);
        if (info.IsEmpty)
        {
            return queryExpression;
        }

        return new Rewriter(model, info).Visit(queryExpression);
    }

    private sealed class Rewriter(IModel model, PolymorphicModelInfo info) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            // ((TSub)owner.Nav).Member -> the member's flattened column, so it filters/sorts/projects
            // server-side and strongly-typed instead of via EF.Property<T>(o, "Member").
            if (TryRewriteSubtypeMemberAccess(node, out var column))
            {
                return column;
            }

            // owner.Nav -> reconstruct the value object in the projection.
            if (node.Expression is not null
                && node.Member is PropertyInfo property
                && ResolveMapping(node.Expression.Type, property.Name) is { } mapping)
            {
                var owner = Visit(node.Expression)!;
                return BuildReconstruction(owner, mapping, node.Type);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            // owner.Nav is TSub -> discriminator == "<value>".
            if (node.NodeType == ExpressionType.TypeIs
                && node.Expression is MemberExpression { Member: PropertyInfo navProperty, Expression: { } ownerExpression }
                && ResolveMapping(ownerExpression.Type, navProperty.Name) is { } mapping
                && mapping.FindSubtype(node.TypeOperand) is { } subtype)
            {
                var owner = Visit(ownerExpression)!;
                var discriminator = Expression.Call(
                    EfPropertyMethod.MakeGenericMethod(typeof(string)),
                    owner,
                    Expression.Constant(mapping.DiscriminatorPropertyName));
                return Expression.Equal(discriminator, Expression.Constant(subtype.DiscriminatorValue, typeof(string)));
            }

            return base.VisitTypeBinary(node);
        }

        private bool TryRewriteSubtypeMemberAccess(MemberExpression node, out Expression column)
        {
            column = node;

            if (node.Member is not PropertyInfo member
                || node.Expression is not UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast)
            {
                return false;
            }

            var navigation = cast.Operand;
            while (navigation is UnaryExpression { NodeType: ExpressionType.Convert } innerCast)
            {
                navigation = innerCast.Operand;
            }

            if (navigation is not MemberExpression { Member: PropertyInfo navProperty, Expression: { } ownerExpression }
                || ResolveMapping(ownerExpression.Type, navProperty.Name) is not { } mapping
                || mapping.FindSubtype(cast.Type) is not { } subtype
                || subtype.ShadowNameForMember(member.Name) is not { } shadowName)
            {
                return false;
            }

            var shadowType = model.FindEntityType(mapping.OwnerClrType)!.FindProperty(shadowName)!.ClrType;
            var owner = Visit(ownerExpression)!;
            Expression read = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(shadowType),
                owner,
                Expression.Constant(shadowName));

            // Preserve the member's declared (non-nullable) type so surrounding operators stay valid.
            column = shadowType == node.Type ? read : Expression.Convert(read, node.Type);
            return true;
        }

        private UnaryExpression BuildReconstruction(Expression owner, PolymorphicMapping mapping, Type resultType)
        {
            var entityType = model.FindEntityType(mapping.OwnerClrType)
                ?? throw new InvalidOperationException($"Entity type '{mapping.OwnerClrType}' is not in the model.");

            var mappingId = PolymorphicProjection.Register(mapping);

            // EF.Property<string>(owner, "<discriminator>") -> the discriminator column.
            var discriminator = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(typeof(string)),
                owner,
                Expression.Constant(mapping.DiscriminatorPropertyName));

            // new object?[] { (object?)EF.Property<T>(owner, "col"), ... } in shadow-property order.
            var values = mapping.AllShadowPropertyNames
                .Select(shadowName =>
                {
                    var clrType = entityType.FindProperty(shadowName)!.ClrType;
                    var read = Expression.Call(
                        EfPropertyMethod.MakeGenericMethod(clrType),
                        owner,
                        Expression.Constant(shadowName));
                    return (Expression)Expression.Convert(read, typeof(object));
                })
                .ToArray();

            var rebuild = Expression.Call(
                RebuildMethod,
                Expression.Constant(mappingId),
                discriminator,
                Expression.NewArrayInit(typeof(object), values));

            return Expression.Convert(rebuild, resultType);
        }

        private PolymorphicMapping? ResolveMapping(Type ownerType, string navigationName)
        {
            if (info.MappingsFor(ownerType) is { } mappings)
            {
                return mappings.FirstOrDefault(m => m.NavigationName == navigationName);
            }

            // Tolerate a declared/base owner type by matching on assignability.
            foreach (var entityType in model.GetEntityTypes())
            {
                if (entityType.ClrType.IsAssignableTo(ownerType)
                    && info.MappingsFor(entityType.ClrType) is { } candidate
                    && candidate.FirstOrDefault(m => m.NavigationName == navigationName) is { } match)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
