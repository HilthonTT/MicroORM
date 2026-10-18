using MicroORM.Sessions;

namespace MicroORM.Querying;

/// <summary>Something a <see cref="Projection{T, TResult}"/> selects from: a query or a grouped query.</summary>
internal interface IProjectionSource
{
    MicroSession Session { get; }

    /// <summary>Builds the <c>SELECT</c>.</summary>
    /// <param name="selectList">The projected values.</param>
    /// <param name="distinct">Whether to select distinct rows.</param>
    /// <param name="limit">A cap on the rows, which never raises a smaller <c>Take</c>.</param>
    /// <param name="ordered">
    /// Whether to keep the ordering. Inside a derived table it is dropped unless paging needs it,
    /// since SQL Server rejects <c>ORDER BY</c> there without paging.
    /// </param>
    Sql.Sql SelectSql(Sql.Sql selectList, bool distinct, int? limit, bool ordered);
}
