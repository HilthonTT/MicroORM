using MicroORM.Materialization;
using System.Data;
using System.Data.Common;

namespace MicroORM.Procedures;

/// <summary>
/// Parameters for a stored procedure call: inputs, outputs, input/outputs and the return value.
/// After the call, read outputs with <see cref="Get{T}"/>. An instance holds the results of its
/// most recent call, so don't share one between concurrent calls.
/// </summary>
/// <example>
/// <code>
/// var p = new ProcedureParameters().Add("customer_id", 42).Output&lt;decimal&gt;("balance");
/// await db.ExecuteProcedureAsync("get_balance", p);
/// decimal balance = p.Get&lt;decimal&gt;("balance");
/// </code>
/// </example>
public sealed class ProcedureParameters
{
    private readonly List<ProcedureParameter> _parameters = [];

    internal IReadOnlyList<ProcedureParameter> Parameters => _parameters;

    /// <summary>Adds an input parameter.</summary>
    public ProcedureParameters Add(string name, object? value) =>
        Push(name, value, value?.GetType() ?? typeof(object), ParameterDirection.Input, null);

    /// <summary>Adds an output parameter of type <typeparamref name="T"/>.</summary>
    /// <param name="name">The parameter name, without a prefix.</param>
    /// <param name="size">The size for variable-length types; strings default to the maximum.</param>
    public ProcedureParameters Output<T>(string name, int? size = null) =>
        Push(name, null, typeof(T), ParameterDirection.Output, size);

    /// <summary>Adds a parameter that passes a value in and receives one back.</summary>
    public ProcedureParameters InputOutput<T>(string name, T value, int? size = null) =>
        Push(name, value, typeof(T), ParameterDirection.InputOutput, size);

    /// <summary>Adds the procedure's return value (SQL Server <c>RETURN</c>).</summary>
    public ProcedureParameters ReturnValue(string name = "ReturnValue") =>
        Push(name, null, typeof(int), ParameterDirection.ReturnValue, null);

    /// <summary>Gets an output, input/output or return value after the call.</summary>
    /// <exception cref="KeyNotFoundException">No parameter has that name.</exception>
    /// <exception cref="InvalidOperationException">The parameter is input-only, or the procedure has not run yet.</exception>
    public T Get<T>(string name)
    {
        var parameter = _parameters.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No procedure parameter is named '{name}'.");
        return parameter.HasResult
            ? ValueConverter.To<T>(parameter.Result)
            : throw new InvalidOperationException($"Parameter '{name}' has no result: it is input-only or the procedure has not run yet.");
    }

    internal void Configure(DbParameter parameter, int index)
    {
        var definition = _parameters[index];
        parameter.Direction = definition.Direction;
        if (definition.Direction == ParameterDirection.Input)
        {
            return;
        }

        if (DbTypes.For(definition.Type) is DbType dbType)
        {
            parameter.DbType = dbType;
        }

        if (definition.Size is int size)
        {
            parameter.Size = size;
        }
        else if (definition.Type == typeof(string) || definition.Type == typeof(byte[]))
        {
            parameter.Size = -1;
        }
    }

    internal void CollectOutputs(DbCommand command)
    {
        for (var i = 0; i < _parameters.Count; i++)
        {
            if (_parameters[i].Direction != ParameterDirection.Input)
            {
                _parameters[i].Result = command.Parameters[i].Value;
                _parameters[i].HasResult = true;
            }
        }
    }

    private ProcedureParameters Push(string name, object? value, Type type, ParameterDirection direction, int? size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var bare = name.TrimStart('@', ':', '?');

        // Some providers write parameter names into the generated CALL text, so allow identifiers only.
        if (bare.Length == 0 || char.IsDigit(bare[0]) || !bare.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$'))
        {
            throw new ArgumentException($"'{name}' is not a valid parameter name; use letters, digits and underscores.", nameof(name));
        }

        _parameters.Add(new ProcedureParameter(bare, value, type, direction, size));
        return this;
    }
}
