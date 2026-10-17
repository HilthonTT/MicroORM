using System.Data;

namespace MicroORM.Procedures;

internal sealed class ProcedureParameter(string name, object? value, Type type, ParameterDirection direction, int? size)
{
    public string Name { get; } = name;

    public object? Value { get; } = value;

    public Type Type { get; } = type;

    public ParameterDirection Direction { get; } = direction;

    public int? Size { get; } = size;

    public object? Result { get; set; }

    public bool HasResult { get; set; }
}
