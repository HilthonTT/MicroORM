using MicroORM.Sessions;

namespace MicroORM.Querying;

/// <summary>A projection of one value usable as a subquery: <c>projection.Contains(value)</c>.</summary>
internal interface IValueSubquerySource
{
    MicroSession Session { get; }

    bool SelectsSingleValue { get; }

    /// <summary>A <c>FROM</c> item exposing the value as column <c>value</c> under <paramref name="alias"/>.</summary>
    Sql.Sql FromItem(string alias);
}
