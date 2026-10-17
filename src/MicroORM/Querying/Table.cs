using MicroORM.Dialects;
using MicroORM.Exceptions;
using MicroORM.Mapping;
using MicroORM.Sessions;
using System.Data;
using System.Linq.Expressions;

namespace MicroORM.Querying;

/// <summary>
/// A typed gateway to one table: single-entity CRUD and upsert plus everything
/// <see cref="Query{T}"/> offers. Every operation has an asynchronous and a synchronous form.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class Table<T> : Query<T>
    where T : class
{
    internal Table(MicroSession session, EntityMap map)
        : base(session, map)
    {
    }

    private SqlDialect Dialect => Session.Dialect;

    /// <summary>Inserts an entity. A database-generated key is read back and assigned to the entity.</summary>
    public Task InsertAsync(T entity, CancellationToken cancellationToken = default) => InsertCore(true, entity, cancellationToken).AsTask();

    /// <summary>Inserts an entity. A database-generated key is read back and assigned to the entity.</summary>
    public void Insert(T entity) => MicroSession.Sync(InsertCore(false, entity, default));

    /// <summary>
    /// Inserts entities in order. Outside a transaction, the batch runs in its own transaction so it
    /// is all-or-nothing.
    /// </summary>
    /// <returns>The number of inserted entities.</returns>
    public Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
        InsertManyCore(true, entities, cancellationToken).AsTask();

    /// <summary>Inserts entities in order, all-or-nothing outside a transaction.</summary>
    public int InsertMany(IEnumerable<T> entities) => MicroSession.Sync(InsertManyCore(false, entities, default));

    /// <summary>
    /// Updates every writable column of an entity, matched by primary key. For a versioned entity,
    /// the version must match and is incremented.
    /// </summary>
    /// <returns><see langword="true"/> if a row was updated.</returns>
    /// <exception cref="MicroConcurrencyException">The entity is versioned and the row changed or was deleted since it was read.</exception>
    public Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default) => UpdateCore(true, entity, cancellationToken).AsTask();

    /// <summary>Updates every writable column of an entity, matched by primary key (and version, if any).</summary>
    public bool Update(T entity) => MicroSession.Sync(UpdateCore(false, entity, default));

    /// <summary>Deletes an entity by its primary key (and version, if any).</summary>
    /// <returns><see langword="true"/> if a row was deleted.</returns>
    /// <exception cref="MicroConcurrencyException">The entity is versioned and the row changed or was deleted since it was read.</exception>
    public Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken = default) => DeleteCore(true, entity, cancellationToken).AsTask();

    /// <summary>Deletes an entity by its primary key (and version, if any).</summary>
    public bool Delete(T entity) => MicroSession.Sync(DeleteCore(false, entity, default));

    /// <summary>
    /// Deletes the row with the given key. For a composite key, pass a tuple in key order:
    /// <c>DeleteByKeyAsync((orderId, lineNo))</c>.
    /// </summary>
    /// <returns><see langword="true"/> if a row was deleted.</returns>
    public Task<bool> DeleteByKeyAsync(object key, CancellationToken cancellationToken = default) => DeleteByKeyCore(true, key, cancellationToken).AsTask();

    /// <summary>Deletes the row with the given key; pass a tuple for a composite key.</summary>
    public bool DeleteByKey(object key) => MicroSession.Sync(DeleteByKeyCore(false, key, default));

    /// <summary>
    /// Finds an entity by key, or returns <see langword="null"/>. For a composite key, pass a tuple
    /// in key order: <c>FindAsync((orderId, lineNo))</c>.
    /// </summary>
    public Task<T?> FindAsync(object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Where(KeyCondition(Map.RequireKeys("Find"), Map.KeyValues(key, "Find"))).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Finds an entity by key, or returns <see langword="null"/>; pass a tuple for a composite key.</summary>
    public T? Find(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Where(KeyCondition(Map.RequireKeys("Find"), Map.KeyValues(key, "Find"))).FirstOrDefault();
    }

    /// <summary>
    /// Inserts the entity, or updates the existing row when one with the same key (or the same
    /// <paramref name="on"/> columns) exists, in one atomic statement. A generated key is read back.
    /// </summary>
    /// <param name="entity">The entity to write.</param>
    /// <param name="on">
    /// Columns with a unique constraint to match on, such as <c>u =&gt; u.Email</c> or
    /// <c>u =&gt; new { u.TenantId, u.Slug }</c>. Defaults to the primary key. If the key is an
    /// identity column and <paramref name="on"/> is omitted, an entity with a default key is
    /// inserted and any other is updated (and inserted with a new key if its row is gone).
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task UpsertAsync(T entity, Expression<Func<T, object?>>? on = null, CancellationToken cancellationToken = default) =>
        UpsertCore(true, entity, on, cancellationToken).AsTask();

    /// <summary>Inserts or updates the entity in one atomic statement; see <see cref="UpsertAsync"/>.</summary>
    public void Upsert(T entity, Expression<Func<T, object?>>? on = null) => MicroSession.Sync(UpsertCore(false, entity, on, default));

    // ---- Cores -------------------------------------------------------------------------------

    private async ValueTask InsertCore(bool async, T entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var identity = Map.Identity;
        var sql = Dialect.Insert(Map.Table, InsertValues(entity), identity?.Name);
        if (identity is null)
        {
            await Session.ExecuteCore(async, sql, cancellationToken).ConfigureAwait(false);
            return;
        }

        var generated = await Session.ScalarCore<object>(async, sql, cancellationToken).ConfigureAwait(false);
        identity.SetValue(entity, generated, Session.Engine.Handlers);
    }

    private async ValueTask<int> InsertManyCore(bool async, IEnumerable<T> entities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (Session is MicroDb db)
        {
            var transaction = await db.BeginTransactionCore(async, IsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
            try
            {
                var inserted = await transaction.Table<T>().InsertManyCore(async, entities, cancellationToken).ConfigureAwait(false);
                if (async)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    transaction.Commit();
                }

                return inserted;
            }
            finally
            {
                await MicroSession.DisposeCore(transaction, async).ConfigureAwait(false);
            }
        }

        var count = 0;
        foreach (var entity in entities)
        {
            await InsertCore(async, entity, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private async ValueTask<bool> UpdateCore(bool async, T entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var keys = Map.RequireKeys("Update");
        var version = Map.Version;
        var assignments = Map.Columns.Where(c => c.IsUpdatable)
            .Select(c => Sql.Sql.Identifier(c.Name).Append(" = ").AppendParameter(c.GetValue(entity), c.Property.PropertyType))
            .ToList();
        if (version is not null)
        {
            assignments.Add(Sql.Sql.Identifier(version.Name).Append(" = COALESCE(").AppendIdentifier(version.Name).Append(", 0) + 1"));
        }

        if (assignments.Count == 0)
        {
            return false;
        }

        var sql = Sql.Sql.Empty.Append("UPDATE ").AppendIdentifier(Map.Table)
            .Append(" SET ").Append(Sql.Sql.Join(", ", assignments))
            .Append(" WHERE ").Append(EntityCondition(entity));
        var affected = await Session.ExecuteCore(async, sql, cancellationToken).ConfigureAwait(false);

        if (version is not null)
        {
            if (affected == 0)
            {
                throw Conflict(entity, "update");
            }

            version.SetValue(entity, Convert.ToInt64(version.GetValue(entity) ?? 0L, System.Globalization.CultureInfo.InvariantCulture) + 1, Session.Engine.Handlers);
        }

        return affected > 0;
    }

    private async ValueTask<bool> DeleteCore(bool async, T entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Map.RequireKeys("Delete");
        var sql = Sql.Sql.Empty.Append("DELETE FROM ").AppendIdentifier(Map.Table).Append(" WHERE ").Append(EntityCondition(entity));
        var affected = await Session.ExecuteCore(async, sql, cancellationToken).ConfigureAwait(false);
        return affected == 0 && Map.Version is not null ? throw Conflict(entity, "delete") : affected > 0;
    }

    private async ValueTask<bool> DeleteByKeyCore(bool async, object key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var sql = Sql.Sql.Empty.Append("DELETE FROM ").AppendIdentifier(Map.Table)
            .Append(" WHERE ").Append(KeyCondition(Map.RequireKeys("DeleteByKey"), Map.KeyValues(key, "DeleteByKey")));
        return await Session.ExecuteCore(async, sql, cancellationToken).ConfigureAwait(false) > 0;
    }

    private async ValueTask UpsertCore(bool async, T entity, LambdaExpression? on, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var identity = Map.Identity;
        var conflict = on is null
            ? Map.RequireKeys("Upsert")
            : EntityBuilder<T>.PropertyNames(on).Select(name => Map.FindByProperty(name)
                ?? throw new ArgumentException($"'{name}' is not a mapped column of {Map.EntityType.Name}.", nameof(on))).ToList();

        // An identity key can't be written explicitly, so "upsert by identity" means save: insert new, update existing.
        if (on is null && identity is not null && conflict.Contains(identity))
        {
            if (!IsDefault(identity.GetValue(entity)) && await UpdateCore(async, entity, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await InsertCore(async, entity, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (conflict.FirstOrDefault(c => !c.IsInsertable) is { } generated)
        {
            throw new NotSupportedException($"Upsert can't match on '{generated.Property.Name}' because the database generates it; match on columns the entity supplies.");
        }

        var update = Map.Columns
            .Where(c => c.IsInsertable && !c.IsKey && !c.IsVersion && !conflict.Contains(c))
            .Select(c => c.Name)
            .ToList();
        var sql = Dialect.Upsert(Map.Table, InsertValues(entity), conflict.Select(c => c.Name).ToList(), update, identity?.Name);
        if (identity is null)
        {
            await Session.ExecuteCore(async, sql, cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = await Session.ScalarCore<object>(async, sql, cancellationToken).ConfigureAwait(false);
        identity.SetValue(entity, key, Session.Engine.Handlers);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private List<ColumnValue> InsertValues(T entity) =>
        Map.Columns.Where(c => c.IsInsertable).Select(c => c.ToColumnValue(entity)).ToList();

    /// <summary>The key columns of the entity, plus its version when it has one.</summary>
    private Sql.Sql EntityCondition(T entity)
    {
        var keys = Map.Keys;
        var condition = KeyCondition(keys, keys.Select(k => k.GetValue(entity)).ToArray());
        if (Map.Version is { } version)
        {
            var current = version.GetValue(entity);
            condition.Append(" AND ").AppendIdentifier(version.Name);
            if (current is null)
            {
                condition.Append(" IS NULL");
            }
            else
            {
                condition.Append(" = ").AppendParameter(current, version.Property.PropertyType);
            }
        }

        return condition;
    }

    private static Sql.Sql KeyCondition(IReadOnlyList<ColumnMap> keys, object?[] values) =>
        Sql.Sql.Join(" AND ", keys.Select((key, i) => Sql.Sql.Identifier(key.Name).Append(" = ").AppendParameter(values[i], key.Property.PropertyType)));

    private static bool IsDefault(object? value) =>
        value is null || (value.GetType().IsValueType && value.Equals(Activator.CreateInstance(value.GetType())));

    private MicroConcurrencyException Conflict(T entity, string operation) => new(
        $"The {operation} of {Map.EntityType.Name} ({string.Join(", ", Map.Keys.Select(k => $"{k.Property.Name} = {k.GetValue(entity)}"))}) " +
        $"matched no row: it was changed or deleted since it was read (version {Map.Version!.GetValue(entity)}).",
        entity);
}
