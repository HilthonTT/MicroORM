using MicroORM.Mapping;
using MicroORM.Sessions;

namespace MicroORM.Querying;

/// <summary>A typed query usable as an <c>EXISTS</c> subquery: <c>query.Any(x =&gt; ...)</c>.</summary>
internal interface ISubquerySource
{
    MicroSession Session { get; }

    EntityMap Map { get; }

    /// <summary>A <c>FROM</c> item exposing every mapped column under <paramref name="alias"/>.</summary>
    Sql.Sql FromItem(string alias);
}
