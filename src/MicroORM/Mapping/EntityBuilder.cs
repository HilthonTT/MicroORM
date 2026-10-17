using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Mapping;

/// <summary>
/// Fluent mapping overrides for <typeparamref name="T"/>. Anything not configured here falls back
/// to data annotation attributes, then to the naming convention.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EntityBuilder<T> : EntityConfiguration
{
    internal EntityBuilder()
    {
    }

    /// <summary>Sets the table name.</summary>
    public EntityBuilder<T> ToTable(string name, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Table = schema is null ? name : schema + "." + name;
        return this;
    }

    /// <summary>
    /// Sets the primary key: one property (<c>x =&gt; x.Id</c>) or, for a composite key, an
    /// anonymous type (<c>x =&gt; new { x.OrderId, x.LineNo }</c>). The generation applies to a
    /// single-column key.
    /// </summary>
    public EntityBuilder<T> HasKey<TKey>(Expression<Func<T, TKey>> key, ValueGeneration? generation = null)
    {
        KeyProperties = PropertyNames(key);
        if (generation is ValueGeneration g)
        {
            if (KeyProperties.Count != 1)
            {
                throw new ArgumentException("A generation strategy can only be set for a single-column key.", nameof(generation));
            }

            Generation[KeyProperties[0]] = g;
        }

        return this;
    }

    /// <summary>
    /// Marks an integer property as the optimistic concurrency version. Updates and deletes check
    /// it and updates increment it.
    /// </summary>
    public EntityBuilder<T> IsVersion<TProperty>(Expression<Func<T, TProperty>> property)
    {
        VersionProperty = PropertyNames(property).Single();
        return this;
    }

    /// <summary>Sets the column name of a property.</summary>
    public EntityBuilder<T> Column<TProperty>(Expression<Func<T, TProperty>> property, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ColumnNames[PropertyNames(property).Single()] = name;
        return this;
    }

    /// <summary>Sets how the database generates a property's value.</summary>
    public EntityBuilder<T> Generated<TProperty>(Expression<Func<T, TProperty>> property, ValueGeneration generation)
    {
        Generation[PropertyNames(property).Single()] = generation;
        return this;
    }

    /// <summary>Excludes a property from the mapping.</summary>
    public EntityBuilder<T> Ignore<TProperty>(Expression<Func<T, TProperty>> property)
    {
        Ignored.Add(PropertyNames(property).Single());
        return this;
    }

    internal static List<string> PropertyNames(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression, nameof(expression));
        ParameterExpression parameter = expression.Parameters[0];

        string NameOf(Expression node)
        {
            if (node is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            {
                node = convert.Operand;
            }

            return node is MemberExpression { Member: PropertyInfo property } member && member.Expression == parameter
               ? property.Name
               : throw new ArgumentException($"Expected a property of {typeof(T).Name}, such as x => x.Name or x => new {{ x.A, x.B }}, but got {expression}.", nameof(expression));
        }

        return expression.Body is NewExpression { Arguments.Count: > 0 } composite
           ? composite.Arguments.Select(NameOf).ToList()
           : [NameOf(expression.Body)];
    }
}
