using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Materialization;
using MicroORM.Procedures;
using MicroORM.Sql;
using MicroORM.TypeHandlers;
using System.Data;
using System.Globalization;

namespace MicroORM.Sessions;

internal sealed class MicroEngine
{
    public MicroEngine(SqlDialect dialect, MicroOptions options)
    {
        Dialect = dialect;
        Options = options;
        Handlers = new TypeHandlerRegistry(new Dictionary<Type, ITypeHandler>(options.TypeHandlers));
        Mappings = new MappingRegistry(options.Naming, options.Entities, Handlers);
        Materializer = new Materializer(Mappings, Handlers);
    }

    public SqlDialect Dialect { get; }

    public MicroOptions Options { get; }

    public TypeHandlerRegistry Handlers { get; }

    public MappingRegistry Mappings { get; }

    public Materializer Materializer { get; }

    public PreparedCommand Prepare(
        string text,
        CommandType commandType,
        IReadOnlyList<SqlParameterValue> parameters,
        ProcedureParameters? procedure)
    {
        var converted = new SqlParameterValue[parameters.Count];
        var handlers = new ITypeHandler?[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            SqlParameterValue parameter = parameters[i];
            object? value = parameter.Value;
            if (Handlers.TryGet(value?.GetType() ?? parameter.ClrType, out var handler))
            {
                handlers[i] = handler;
                converted[i] = value is null ? parameter : parameter with { Value = handler.Write(value) };
            }
            else
            {
                // Providers disagree on enums; their underlying number is understood everywhere.
                converted[i] = value is Enum e
                    ? parameter with { Value = Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture) }
                    : parameter;
            }
        }

        return new PreparedCommand(text, commandType, converted, handlers, procedure);
    }
}
