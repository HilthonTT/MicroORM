using MicroORM.Dialects;
using MicroORM.Mapping;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Querying;

/// <summary>
/// Translates a deliberately small subset of C# expressions over one entity parameter into SQL.
/// Anything that does not reference the entity is evaluated in .NET and sent as a parameter.
/// Unsupported shapes throw <see cref="NotSupportedException"/> rather than silently running
/// client-side.
/// </summary>
internal sealed class ExpressionTranslator
{
    private readonly EntityMap _map;

    private readonly ParameterExpression _entity;

    private readonly SqlDialect _dialect;

    private ExpressionTranslator(EntityMap map, ParameterExpression entity, SqlDialect dialect)
    {
        _map = map;
        _entity = entity;
        _dialect = dialect;
    }

    /// <summary>Translates a predicate (<c>x =&gt; bool</c>) into a <c>WHERE</c> condition.</summary>
    public static Sql.Sql Predicate(LambdaExpression lambda, EntityMap map, SqlDialect dialect) =>
        new ExpressionTranslator(map, lambda.Parameters[0], dialect).Condition(lambda.Body);

    /// <summary>Translates a value expression (<c>x =&gt; x.Name</c>) into a SQL expression.</summary>
    public static Sql.Sql Value(LambdaExpression lambda, EntityMap map, SqlDialect dialect) =>
        new ExpressionTranslator(map, lambda.Parameters[0], dialect).Visit(lambda.Body);

    public static Sql.Sql SelectList(LambdaExpression lambda, EntityMap map, SqlDialect dialect)
    {
        var translator = new ExpressionTranslator(map, lambda.Parameters[0], dialect);
        var body = lambda.Body;
        if (body == lambda.Parameters[0])
        {
            throw new NotSupportedException("Select(x => x) selects the whole entity; run the query without Select instead.");
        }

        IEnumerable<(Expression Value, string Alias)> items = body switch
        {
            NewExpression { Members: { } members } anonymous => anonymous.Arguments.Select((a, i) => (a, members[i].Name)),
            NewExpression { Arguments.Count: > 0 } constructor => ConstructorItems(constructor),
            MemberInitExpression init => ConstructorItems(init.NewExpression).Concat(init.Bindings.Select(b => b is MemberAssignment assignment
                ? (assignment.Expression, assignment.Member.Name)
                : throw Unsupported(body))),
            _ => [(body, "value")],
        };

        return Sql.Sql.Join(", ", items.Select(item => Sql.Sql.Empty.Append(translator.Projected(item.Value)).Append(" AS ").AppendIdentifier(item.Alias)));

        static IEnumerable<(Expression, string)> ConstructorItems(NewExpression node)
        {
            var parameters = node.Constructor!.GetParameters();
            return node.Arguments.Select((a, i) => (a, parameters[i].Name!));
        }
    }

    public static ColumnMap Column(LambdaExpression lambda, EntityMap map)
    {
        var body = StripConvert(lambda.Body);
        return body is MemberExpression member && member.Expression == lambda.Parameters[0]
            ? map.FindByProperty(member.Member.Name) ?? throw new NotSupportedException($"'{member.Member.Name}' is not a mapped column of {map.EntityType.Name}.")
            : throw new NotSupportedException($"Expected a column selector such as x => x.Name, but got {lambda}.");
    }

    private Sql.Sql Condition(Expression node)
    {
        node = StripConvert(node);
        return node switch
        {
            // First, so a closed condition (flag, x > 3, list.Contains(5)) never becomes a bare parameter,
            // which SQL Server can't use as a predicate.
            _ when !DependsOnEntity(node) => Evaluate(node) is true ? Sql.Sql.Raw("1 = 1") : Sql.Sql.Raw("1 = 0"),
            BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } or
            BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual } or
            UnaryExpression { NodeType: ExpressionType.Not } or
            MethodCallExpression => Visit(node),
            MemberExpression { Member.Name: "HasValue" } => Visit(node),
            _ => Sql.Sql.Empty.Append("(").Append(Visit(node)).Append(" = ").AppendParameter(true, typeof(bool)).Append(")"),
        };
    }

    private Sql.Sql Projected(Expression node)
    {
        var stripped = StripConvert(node);
        var isCondition = node.Type == typeof(bool) && stripped is not MemberExpression { Expression: ParameterExpression };
        return isCondition && DependsOnEntity(node)
            ? Sql.Sql.Empty.Append("CASE WHEN ").Append(Condition(node)).Append(" THEN 1 ELSE 0 END")
            : Visit(node);
    }

    private Sql.Sql Visit(Expression node)
    {
        if (!DependsOnEntity(node))
        {
            return Parameter(Evaluate(node), node.Type);
        }

        return node switch
        {
            BinaryExpression binary => VisitBinary(binary),
            // On integers, ExpressionType.Not is the bitwise complement (~x), not a logical NOT.
            UnaryExpression { NodeType: ExpressionType.Not } not when IsBoolean(not.Type) => Sql.Sql.Empty.Append("NOT (").Append(Condition(not.Operand)).Append(")"),
            UnaryExpression { NodeType: ExpressionType.Negate } negate => Sql.Sql.Empty.Append("-(").Append(Visit(negate.Operand)).Append(")"),
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert => Visit(convert.Operand),
            MemberExpression member => VisitMember(member),
            MethodCallExpression call => VisitCall(call),
            _ => throw Unsupported(node),
        };
    }

    private Sql.Sql VisitBinary(BinaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.AndAlso:
                return Sql.Sql.Empty.Append("(").Append(Condition(node.Left)).Append(" AND ").Append(Condition(node.Right)).Append(")");
            case ExpressionType.OrElse:
                return Sql.Sql.Empty.Append("(").Append(Condition(node.Left)).Append(" OR ").Append(Condition(node.Right)).Append(")");
            case ExpressionType.Equal or ExpressionType.NotEqual:
                // "x == null" must become "IS NULL"; "= NULL" is never true in SQL.
                var isEqual = node.NodeType == ExpressionType.Equal;
                if (IsNullValue(node.Right))
                {
                    return Sql.Sql.Empty.Append("(").Append(Visit(node.Left)).Append(isEqual ? " IS NULL)" : " IS NOT NULL)");
                }

                if (IsNullValue(node.Left))
                {
                    return Sql.Sql.Empty.Append("(").Append(Visit(node.Right)).Append(isEqual ? " IS NULL)" : " IS NOT NULL)");
                }

                return Infix(node, isEqual ? " = " : " <> ");
            case ExpressionType.LessThan:
                return Infix(node, " < ");
            case ExpressionType.LessThanOrEqual:
                return Infix(node, " <= ");
            case ExpressionType.GreaterThan:
                return Infix(node, " > ");
            case ExpressionType.GreaterThanOrEqual:
                return Infix(node, " >= ");
            case ExpressionType.Add when node.Type == typeof(string):
                return _dialect.Concat(Visit(node.Left), Visit(node.Right));
            case ExpressionType.Add or ExpressionType.AddChecked:
                return Infix(node, " + ");
            case ExpressionType.Subtract or ExpressionType.SubtractChecked:
                return Infix(node, " - ");
            case ExpressionType.Multiply or ExpressionType.MultiplyChecked:
                return Infix(node, " * ");
            case ExpressionType.Divide:
                return Infix(node, " / ");
            case ExpressionType.Modulo:
                return Infix(node, " % ");
            case ExpressionType.Coalesce:
                return Sql.Sql.Empty.Append("COALESCE(").Append(Visit(node.Left)).Append(", ").Append(Visit(node.Right)).Append(")");
            default:
                throw Unsupported(node);
        }
    }

    private Sql.Sql Infix(BinaryExpression node, string op) =>
        Sql.Sql.Empty.Append("(").Append(Side(node.Left, node.Right)).Append(op).Append(Side(node.Right, node.Left)).Append(")");

    private Sql.Sql Side(Expression node, Expression other)
    {
        if (!DependsOnEntity(node) && EnumTypeOf(other) is { } enumType)
        {
            var value = Evaluate(node);
            return Parameter(value is null ? null : Enum.ToObject(enumType, value), enumType);
        }

        return Visit(node);
    }

    private static Type? EnumTypeOf(Expression node)
    {
        if (node is not UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(convert.Operand.Type) ?? convert.Operand.Type;
        return type.IsEnum ? type : null;
    }

    private Sql.Sql VisitMember(MemberExpression node)
    {
        if (node.Expression == _entity)
        {
            var column = _map.FindByProperty(node.Member.Name)
                ?? throw new NotSupportedException($"'{_map.EntityType.Name}.{node.Member.Name}' is not a mapped column and cannot be used in a query.");
            return Sql.Sql.Identifier(column.Name);
        }

        if (node.Expression is not null && Nullable.GetUnderlyingType(node.Expression.Type) is not null)
        {
            if (node.Member.Name == "HasValue")
            {
                return Sql.Sql.Empty.Append("(").Append(Visit(node.Expression)).Append(" IS NOT NULL)");
            }

            if (node.Member.Name == "Value")
            {
                return Visit(node.Expression);
            }
        }

        if (node.Member.Name == "Length" && node.Expression?.Type == typeof(string))
        {
            return Sql.Sql.Empty.Append(_dialect.LengthFunction + "(").Append(Visit(node.Expression)).Append(")");
        }

        if (node.Expression is not null && DatePartOf(node) is { } part)
        {
            return _dialect.DatePart(part, Visit(node.Expression));
        }

        throw Unsupported(node);
    }

    private static DatePart? DatePartOf(MemberExpression node)
    {
        var type = node.Expression!.Type;
        var isDate = type == typeof(DateOnly);
        if (!isDate && type != typeof(DateTime))
        {
            return null;
        }

        return node.Member.Name switch
        {
            nameof(DateTime.Year) => DatePart.Year,
            nameof(DateTime.Month) => DatePart.Month,
            nameof(DateTime.Day) => DatePart.Day,
            nameof(DateTime.Hour) when !isDate => DatePart.Hour,
            nameof(DateTime.Minute) when !isDate => DatePart.Minute,
            nameof(DateTime.Second) when !isDate => DatePart.Second,
            _ => null,
        };
    }

    private Sql.Sql VisitCall(MethodCallExpression node)
    {
        var method = node.Method;

        if (method.DeclaringType == typeof(string))
        {
            switch (method.Name)
            {
                case nameof(string.StartsWith) when node.Arguments.Count == 1:
                    return Like(node.Object!, node.Arguments[0], prefix: false, suffix: true);
                case nameof(string.EndsWith) when node.Arguments.Count == 1:
                    return Like(node.Object!, node.Arguments[0], prefix: true, suffix: false);
                case nameof(string.Contains) when node.Arguments.Count == 1 && (node.Arguments[0].Type == typeof(string) || node.Arguments[0].Type == typeof(char)):
                    return Like(node.Object!, node.Arguments[0], prefix: true, suffix: true);
                case nameof(string.ToUpper) when node.Arguments.Count == 0:
                    return Function("UPPER", node.Object!);
                case nameof(string.ToLower) when node.Arguments.Count == 0:
                    return Function("LOWER", node.Object!);
                case nameof(string.Trim) when node.Arguments.Count == 0:
                    return Function("TRIM", node.Object!);
                case nameof(string.TrimStart) when node.Arguments.Count == 0:
                    return Function("LTRIM", node.Object!);
                case nameof(string.TrimEnd) when node.Arguments.Count == 0:
                    return Function("RTRIM", node.Object!);
                case nameof(string.Substring) when node.Arguments.Count is 1 or 2:
                    return _dialect.Substring(
                        Visit(node.Object!),
                        OneBased(node.Arguments[0]),
                        node.Arguments.Count == 2 ? Visit(node.Arguments[1]) : null);
                case nameof(string.Replace) when node.Arguments.Count == 2 && node.Arguments[0].Type == node.Arguments[1].Type:
                    return Sql.Sql.Empty.Append("REPLACE(").Append(Visit(node.Object!)).Append(", ")
                        .Append(Text(node.Arguments[0])).Append(", ").Append(Text(node.Arguments[1])).Append(")");
                case nameof(string.IsNullOrEmpty):
                    var value = Visit(node.Arguments[0]);
                    return Sql.Sql.Empty.Append("(").Append(value).Append(" IS NULL OR ").Append(value).Append(" = '')");
                case nameof(string.IsNullOrWhiteSpace):
                    var text = Visit(node.Arguments[0]);
                    return Sql.Sql.Empty.Append("(").Append(text).Append(" IS NULL OR TRIM(").Append(text).Append(") = '')");
                case nameof(string.Equals) when node.Object is not null && node.Arguments.Count == 1:
                    return Sql.Sql.Empty.Append("(").Append(Visit(node.Object)).Append(" = ").Append(Visit(node.Arguments[0])).Append(")");
            }
        }

        // Math.Round is left out: databases round halves differently from .NET's default, and from each other.
        if (method.DeclaringType == typeof(Math) && node.Arguments.Count == 1)
        {
            switch (method.Name)
            {
                case nameof(Math.Abs):
                    return Function("ABS", node.Arguments[0]);
                case nameof(Math.Floor):
                    return Function("FLOOR", node.Arguments[0]);
                case nameof(Math.Ceiling):
                    return Function("CEILING", node.Arguments[0]);
            }
        }

        if (method.DeclaringType == typeof(SqlFunctions) && method.Name == nameof(SqlFunctions.Like))
        {
            return Sql.Sql.Empty.Append("(").Append(Visit(node.Arguments[0])).Append(" LIKE ").Append(Visit(node.Arguments[1])).Append(")");
        }

        if (method.Name == "Contains" && TryGetCollectionContains(node, out var source, out var item))
        {
            if (DependsOnEntity(source))
            {
                throw Unsupported(node);
            }

            var values = ((IEnumerable?)Evaluate(source) ?? Array.Empty<object>()).Cast<object?>().ToList();
            var nonNull = values.Where(v => v is not null).ToList();
            var column = Visit(item);
            var contains = Sql.Sql.Empty.Append("(").Append(column).Append(" IN ").AppendList(nonNull).Append(")");

            // C#'s Contains finds a null item, but SQL's IN never matches NULL.
            return nonNull.Count == values.Count
                ? contains
                : Sql.Sql.Empty.Append("(").Append(contains).Append(" OR ").Append(column).Append(" IS NULL)");
        }

        if (method.Name == nameof(Equals) && node.Object is not null && node.Arguments.Count == 1)
        {
            return Sql.Sql.Empty.Append("(").Append(Visit(node.Object)).Append(" = ").Append(Visit(node.Arguments[0])).Append(")");
        }

        throw Unsupported(node);
    }

    private Sql.Sql Function(string name, Expression argument) =>
        Sql.Sql.Empty.Append(name + "(").Append(Visit(argument)).Append(")");

    /// <summary>Converts a .NET zero-based index to SQL's one-based position.</summary>
    private Sql.Sql OneBased(Expression index) => DependsOnEntity(index)
        ? Sql.Sql.Empty.Append("(").Append(Visit(index)).Append(" + 1)")
        : Parameter((int)Evaluate(index)! + 1, typeof(int));

    /// <summary>Translates a string or char argument, sending chars as one-character strings.</summary>
    private Sql.Sql Text(Expression node) => DependsOnEntity(node) || node.Type != typeof(char)
        ? Visit(node)
        : Parameter(Evaluate(node)?.ToString(), typeof(string));

    private Sql.Sql Like(Expression target, Expression pattern, bool prefix, bool suffix)
    {
        if (DependsOnEntity(pattern))
        {
            throw new NotSupportedException("String matching against another column is not supported; the pattern must be a value.");
        }

        var text = Evaluate(pattern) switch
        {
            string s => s,
            char c => c.ToString(),
            _ => null,
        };
        if (text is null)
        {
            return Sql.Sql.Raw("1 = 0");
        }

        // '!' is the escape character because '\' means different things across dialects.
        var escaped = text.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal)
            .Replace("[", "![", StringComparison.Ordinal);
        var value = (prefix ? "%" : string.Empty) + escaped + (suffix ? "%" : string.Empty);
        return Sql.Sql.Empty.Append("(").Append(Visit(target)).Append(" LIKE ").AppendParameter(value, typeof(string)).Append(" ESCAPE '!')");
    }

    private static bool TryGetCollectionContains(MethodCallExpression node, out Expression source, out Expression item)
    {
        source = null!;
        item = null!;

        if (node.Object is not null && node.Arguments.Count == 1 && node.Object.Type != typeof(string))
        {
            (source, item) = (node.Object, node.Arguments[0]);
            return typeof(IEnumerable).IsAssignableFrom(node.Object.Type);
        }

        if (node.Object is null && node.Arguments.Count == 2 &&
            (node.Method.DeclaringType == typeof(Enumerable) || node.Method.DeclaringType == typeof(MemoryExtensions)))
        {
            source = node.Arguments[0];
            item = node.Arguments[1];

            // Unwrap ReadOnlySpan<T>.op_Implicit(T[]) so the array can be evaluated.
            if (source is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } implicitCall)
            {
                source = implicitCall.Arguments[0];
            }

            return typeof(IEnumerable).IsAssignableFrom(source.Type);
        }

        return false;
    }

    private static Sql.Sql Parameter(object? value, Type type) => Sql.Sql.Empty.AppendParameter(value, type);

    private static bool IsBoolean(Type type) => type == typeof(bool) || type == typeof(bool?);

    private bool IsNullValue(Expression node) => !DependsOnEntity(node) && Evaluate(node) is null;

    private bool DependsOnEntity(Expression node) => EntityFinder.Finds(node, _entity);

    private static Expression StripConvert(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            node = convert.Operand;
        }

        return node;
    }

    /// <summary>Evaluates a closed expression, with a fast path for constants and captured variables.</summary>
    internal static object? Evaluate(Expression node)
    {
        switch (node)
        {
            case ConstantExpression constant:
                return constant.Value;
            case MemberExpression { Member: FieldInfo field } member:
                return field.GetValue(member.Expression is null ? null : Evaluate(member.Expression));
            case MemberExpression { Member: PropertyInfo property } member when property.GetIndexParameters().Length == 0:
                return property.GetValue(member.Expression is null ? null : Evaluate(member.Expression));
            case UnaryExpression { NodeType: ExpressionType.Convert } convert when convert.Type == typeof(object) || Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type:
                return Evaluate(convert.Operand);
            default:
                var lambda = Expression.Lambda<Func<object?>>(Expression.Convert(node, typeof(object)));
                Func<object?> evaluate;
                try
                {
                    evaluate = lambda.Compile(preferInterpretation: true);
                }
                catch (ArgumentException)
                {
                    // The interpreter can't handle ref structs, such as the span in C# 14's array.Contains(x).
                    evaluate = lambda.Compile();
                }

                return evaluate();
        }
    }

    private static NotSupportedException Unsupported(Expression node) =>
      new($"MicroORM cannot translate '{node}' to SQL. Supported: comparisons, &&, ||, !, arithmetic, null checks, string StartsWith/EndsWith/Contains/ToUpper/ToLower/Trim/TrimStart/TrimEnd/Substring/Replace/Length/IsNullOrWhiteSpace, DateTime and DateOnly Year/Month/Day/Hour/Minute/Second, Math.Abs/Floor/Ceiling, SqlFunctions.Like, collection Contains. Use .Where($\"...\") for anything else.");
}
