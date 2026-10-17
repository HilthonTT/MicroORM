using MicroORM.Procedures;
using MicroORM.Sql;
using MicroORM.TypeHandlers;
using System.Data;

namespace MicroORM.Sessions;

internal sealed record PreparedCommand(
    string Text,
    CommandType CommandType,
    IReadOnlyList<SqlParameterValue> Parameters,
    IReadOnlyList<ITypeHandler?> Handlers,
    ProcedureParameters? Procedure);
