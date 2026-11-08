using MicroORM.Dialects;
using MicroORM.Mapping;
using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Querying;

/// <summary>
/// Translates a deliberately small subset of C# expressions over one entity parameter into SQL.
/// Anything that does not reference the entity is evaluated in .NET and sent as a parameter.
/// Unsupported shapes throw <see cref="NotSupportedException"/> rather than silently running
/// client-side.
/// </summary>
/// <remarks>
/// With a <see cref="GroupKey"/>, the parameter is an <see cref="IGrouping{TKey, TElement}"/> of
/// entities instead: <c>g.Key</c> is the grouping key and <c>Count</c>, <c>Sum</c>, <c>Min</c>,
/// <c>Max</c> and <c>Average</c> over <c>g</c> are aggregates. Inside a subquery, the parameter's
/// columns are qualified with the subquery's alias and the enclosing entities are outer scopes.
/// </remarks>
internal sealed class ExpressionTranslator
{
    /// <summary>The column alias of a projection that selects one value.</summary>
    internal const string ValueAlias = "value";

    private readonly EntityMap _map;

    private readonly ParameterExpression _entity;

    private readonly SqlDialect _dialect;

    private readonly GroupKey? _group;

    /// <summary>Qualifies the entity's columns in a subquery; <see langword="null"/> at the top level.</summary>
    private readonly string? _qualifier;

    /// <summary>The entities of the enclosing queries, innermost first.</summary>
    private readonly IReadOnlyList<Scope> _outer;

    private readonly bool _subqueries;

    private readonly ParameterExpression[] _parameters;

    private ExpressionTranslator(
        EntityMap map,
        ParameterExpression entity,
        SqlDialect dialect,
        GroupKey? group = null,
        string? qualifier = null,
        IReadOnlyList<Scope>? outer = null,
        bool subqueries = true)
    {
        _map = map;
        _entity = entity;
        _dialect = dialect;
        _group = group;
        _qualifier = qualifier;
        _outer = outer ?? [];
        _subqueries = subqueries && group is null;
        _parameters = [entity, .. _outer.Select(s => s.Parameter)];
    }

    /// <summary>How a subquery refers to this entity: by its alias, or at the top level by its table.</summary>
    private string Reference => _qualifier ?? _map.Table;

    /// <summary>Translates a predicate (<c>x =&gt; bool</c>) into a <c>WHERE</c> condition, or over groups into a <c>HAVING</c> one.</summary>
    public static Sql.Sql Predicate(LambdaExpression lambda, EntityMap map, SqlDialect dialect, GroupKey? group = null) =>
        new ExpressionTranslator(map, lambda.Parameters[0], dialect, group).Condition(lambda.Body);

    /// <summary>Translates a predicate whose columns are qualified with <paramref name="qualifier"/>, the alias of a <c>FROM</c> item.</summary>
    public static Sql.Sql Predicate(LambdaExpression lambda, EntityMap map, SqlDialect dialect, string qualifier) =>
        new ExpressionTranslator(map, lambda.Parameters[0], dialect, qualifier: qualifier).Condition(lambda.Body);

    /// <summary>Translates a value expression (<c>x =&gt; x.Name</c>) into a SQL expression.</summary>
    public static Sql.Sql Value(LambdaExpression lambda, EntityMap map, SqlDialect dialect, GroupKey? group = null) =>
        new ExpressionTranslator(map, lambda.Parameters[0], dialect, group).Projected(lambda.Body);

    /// <summary>Whether a <c>Select</c> lambda selects one value (aliased <see cref="ValueAlias"/>) rather than members.</summary>
    public static bool SelectsSingleValue(LambdaExpression lambda) =>
        lambda.Body is not (NewExpression { Members: not null } or NewExpression { Arguments.Count: > 0 } or MemberInitExpression);

    /// <summary>
    /// Translates the parts of a grouping key: one value, or the members of an anonymous type. A
    /// part is a column when it is a plain mapped property.
    /// </summary>
    public static IReadOnlyList<(string? Name, Sql.Sql Value, bool IsColumn)> GroupKeyParts(LambdaExpression lambda, EntityMap map, SqlDialect dialect)
    {
        var translator = new ExpressionTranslator(map, lambda.Parameters[0], dialect);
        IEnumerable<(string? Name, Expression Value)> items = lambda.Body is NewExpression { Members: { } members } anonymous
            ? anonymous.Arguments.Select<Expression, (string?, Expression)>((a, i) => (members[i].Name, a))
            : [(null, lambda.Body)];

        return items.Select(item => (
            item.Name,
            translator.Projected(item.Value),
            StripConvert(item.Value) is MemberExpression member && member.Expression == lambda.Parameters[0])).ToList();
    }

    public static Sql.Sql SelectList(LambdaExpression lambda, EntityMap map, SqlDialect dialect, GroupKey? group = null)
    {
        var translator = new ExpressionTranslator(map, lambda.Parameters[0], dialect, group);
        var body = lambda.Body;
        if (body == lambda.Parameters[0])
        {
            throw new NotSupportedException(group is null
                ? "Select(x => x) selects the whole entity; run the query without Select instead."
                : "Select(g => g) selects whole groups, which can't be read as rows; select the key and aggregates instead, such as g => new { g.Key, Count = g.Count() }.");
        }

        IEnumerable<(Expression Value, string Alias)> items = body switch
        {
            NewExpression { Members: { } members } anonymous => anonymous.Arguments.Select((a, i) => (a, members[i].Name)),
            NewExpression { Arguments.Count: > 0 } constructor => ConstructorItems(constructor),
            MemberInitExpression init => ConstructorItems(init.NewExpression).Concat(init.Bindings.Select(b => b is MemberAssignment assignment
                ? (assignment.Expression, assignment.Member.Name)
                : throw Unsupported(body))),
            _ => [(body, ValueAlias)],
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
            ConditionalExpression conditional when !DependsOnEntity(conditional.Test) =>
                Condition(Evaluate(conditional.Test) is true ? conditional.IfTrue : conditional.IfFalse),
            ConditionalExpression conditional => Sql.Sql.Empty.Append("(").Append(VisitConditional(conditional)).Append(" = 1)"),
            _ => Sql.Sql.Empty.Append("(").Append(Visit(node)).Append(" = ").AppendParameter(true, typeof(bool)).Append(")"),
        };
    }

    private Sql.Sql Projected(Expression node) => IsProjectedCondition(node)
        ? Sql.Sql.Empty.Append("CASE WHEN ").Append(Condition(node)).Append(" THEN 1 ELSE 0 END")
        : Visit(node);

    /// <summary>Whether <paramref name="node"/> is a condition that becomes <c>CASE WHEN ... THEN 1 ELSE 0 END</c> as a value.</summary>
    private bool IsProjectedCondition(Expression node)
    {
        var stripped = StripConvert(node);

        // A bool column, or a bool grouping key, is a value already.
        return node.Type == typeof(bool) && stripped is not MemberExpression { Expression: ParameterExpression } &&
            !(_group is not null && stripped is MemberExpression { Expression: MemberExpression { Member.Name: "Key", Expression: var key } } && key == _entity) &&
            DependsOnEntity(node);
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
            UnaryExpression { NodeType: ExpressionType.Not } not when IsBoolean(not.Type) => Negate(not),
            UnaryExpression { NodeType: ExpressionType.Negate } negate => Sql.Sql.Empty.Append("-(").Append(Visit(negate.Operand)).Append(")"),
            // A C# cast from a fraction to an integer truncates; SQL keeps the fraction, or rounds.
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert when IsIntegral(convert.Type) && IsFractional(convert.Operand.Type) =>
                _dialect.ToInteger(Visit(convert.Operand)),
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert => Visit(convert.Operand),
            MemberExpression member => VisitMember(member),
            MethodCallExpression call => VisitCall(call),
            ConditionalExpression conditional => VisitConditional(conditional),
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

                return Compare(node, isEqual);
            case ExpressionType.LessThan:
                return Infix(node, " < ");
            case ExpressionType.LessThanOrEqual:
                return Infix(node, " <= ");
            case ExpressionType.GreaterThan:
                return Infix(node, " > ");
            case ExpressionType.GreaterThanOrEqual:
                return Infix(node, " >= ");
            case ExpressionType.Add when node.Type == typeof(string):
                return _dialect.Concat(NullAsEmpty(node.Left), NullAsEmpty(node.Right));
            case ExpressionType.Add or ExpressionType.Subtract when IsDateTime(node.Left.Type) && IsTimeSpan(node.Right.Type) && !DependsOnEntity(node.Right):
                // date ± a TimeSpan value adds its seconds.
                var span = Evaluate(node.Right) as TimeSpan? ?? throw Unsupported(node);
                var seconds = node.NodeType == ExpressionType.Add ? span.TotalSeconds : -span.TotalSeconds;
                return _dialect.DateAdd(DatePart.Second, Visit(node.Left), Number(seconds, out var whole), whole, dateOnly: false);
            case ExpressionType.Add or ExpressionType.AddChecked or ExpressionType.Subtract or ExpressionType.SubtractChecked
                when IsDateOrTime(node.Left.Type) || IsDateOrTime(node.Right.Type):
                // date - date is a TimeSpan, which only translates through its Total* members.
                throw Unsupported(node);
            case ExpressionType.Add or ExpressionType.AddChecked:
                return Infix(node, " + ");
            case ExpressionType.Subtract or ExpressionType.SubtractChecked:
                return Infix(node, " - ");
            case ExpressionType.Multiply or ExpressionType.MultiplyChecked:
                return Infix(node, " * ");
            case ExpressionType.Divide:
                return Divide(node);
            case ExpressionType.Modulo:
                return _dialect.Modulo(Side(node.Left, node.Right), Side(node.Right, node.Left), IsIntegral(node.Type));
            case ExpressionType.Coalesce:
                return Sql.Sql.Empty.Append("COALESCE(").Append(Visit(node.Left)).Append(", ").Append(Visit(node.Right)).Append(")");
            default:
                throw Unsupported(node);
        }
    }

    private Sql.Sql Infix(BinaryExpression node, string op) =>
        Sql.Sql.Empty.Append("(").Append(Side(node.Left, node.Right)).Append(op).Append(Side(node.Right, node.Left)).Append(")");

    /// <summary>
    /// Translates <c>==</c> or <c>!=</c> with C#'s null semantics: null equals null and differs from
    /// any value, where in SQL a comparison with <c>NULL</c> is unknown.
    /// </summary>
    private Sql.Sql Compare(BinaryExpression node, bool isEqual)
    {
        var left = Side(node.Left, node.Right);
        var right = Side(node.Right, node.Left);
        var leftNull = CanBeNull(node.Left);
        var rightNull = CanBeNull(node.Right);
        var sql = Sql.Sql.Empty.Append("(").Append(left).Append(isEqual ? " = " : " <> ").Append(right);
        if (isEqual)
        {
            if (leftNull && rightNull)
            {
                sql.Append(" OR (").Append(left).Append(" IS NULL AND ").Append(right).Append(" IS NULL)");
            }
        }
        else if (leftNull && rightNull)
        {
            sql.Append(" OR (").Append(left).Append(" IS NULL AND ").Append(right).Append(" IS NOT NULL) OR (")
                .Append(left).Append(" IS NOT NULL AND ").Append(right).Append(" IS NULL)");
        }
        else if (leftNull || rightNull)
        {
            sql.Append(" OR ").Append(leftNull ? left : right).Append(" IS NULL");
        }

        return sql.Append(")");
    }

    /// <summary>
    /// Translates <c>!condition</c>. SQL's <c>NOT</c> keeps an unknown condition unknown, so a row with
    /// a <c>NULL</c> would match neither the condition nor its negation; in C# it matches the negation.
    /// </summary>
    private Sql.Sql Negate(UnaryExpression node) => node.Type == typeof(bool) && MayBeUnknown(node.Operand)
        ? Sql.Sql.Empty.Append("(CASE WHEN ").Append(Condition(node.Operand)).Append(" THEN 0 ELSE 1 END = 1)")
        : Sql.Sql.Empty.Append("NOT (").Append(Condition(node.Operand)).Append(")");

    /// <summary>Translates a division; casts are otherwise dropped, and SQL divides two integers as integers.</summary>
    private Sql.Sql Divide(BinaryExpression node)
    {
        var left = Side(node.Left, node.Right);
        var right = Side(node.Right, node.Left);
        if (IsIntegral(node.Type))
        {
            return _dialect.Divide(left, right, integral: true);
        }

        if (IsIntegral(SqlType(node.Left)) && IsIntegral(SqlType(node.Right)))
        {
            var type = (Nullable.GetUnderlyingType(node.Type) ?? node.Type) == typeof(decimal) ? _dialect.DecimalType : _dialect.FloatType;
            left = Sql.Sql.Empty.Append("CAST(").Append(left).Append(" AS " + type + ")");
        }

        return _dialect.Divide(left, right, integral: false);
    }

    /// <summary>A side of a string concatenation; C# treats a null string as empty, SQL makes the result <c>NULL</c>.</summary>
    private Sql.Sql NullAsEmpty(Expression node) => StripConvert(node).Type == typeof(string) && CanBeNull(node)
        ? Sql.Sql.Empty.Append("COALESCE(").Append(Visit(node)).Append(", '')")
        : Visit(node);

    /// <summary>The text <paramref name="sql"/> of <paramref name="node"/>, empty when the node is null.</summary>
    private Sql.Sql NullAsEmpty(Expression node, Sql.Sql sql) => CanBeNull(node)
        ? Sql.Sql.Empty.Append("COALESCE(").Append(sql).Append(", '')")
        : sql;

    private Sql.Sql Side(Expression node, Expression other)
    {
        // A condition compared with a bool (x.Age > 18 == flag) must be a value; SQL Server can't compare predicates.
        if (IsProjectedCondition(node))
        {
            return Projected(node);
        }

        // The condition's side is then an integer, so this side must be one too: PostgreSQL can't compare integer and boolean.
        if (IsBoolean(node.Type) && IsProjectedCondition(other))
        {
            return BoolAsInt(node);
        }

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
        if (_group is not null && GroupKeyMember(node) is { } key)
        {
            return key;
        }

        if (node.Expression == _entity)
        {
            var column = _map.FindByProperty(node.Member.Name)
                ?? throw new NotSupportedException($"'{_map.EntityType.Name}.{node.Member.Name}' is not a mapped column and cannot be used in a query.");
            return Sql.Sql.Identifier(_qualifier is null ? column.Name : _qualifier + "." + column.Name);
        }

        if (node.Expression is ParameterExpression parameter && _outer.FirstOrDefault(s => s.Parameter == parameter) is { } scope)
        {
            var column = scope.Map.FindByProperty(node.Member.Name)
                ?? throw new NotSupportedException($"'{scope.Map.EntityType.Name}.{node.Member.Name}' is not a mapped column and cannot be used in a query.");
            return Sql.Sql.Identifier(scope.Reference + "." + column.Name);
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
            return _dialect.Length(Visit(node.Expression));
        }

        if (node.Expression is not null && DatePartOf(node) is { } part)
        {
            return _dialect.DatePart(part, Visit(node.Expression));
        }

        if (node.Member.Name == nameof(DateTime.Date) && node.Expression?.Type == typeof(DateTime))
        {
            return _dialect.StartOfDay(Visit(node.Expression));
        }

        // (end - start).TotalDays and friends.
        if (node.Expression is BinaryExpression { NodeType: ExpressionType.Subtract } difference &&
            difference.Left.Type == typeof(DateTime) && difference.Right.Type == typeof(DateTime) &&
            SecondsPer(node.Member.Name) is { } divisor)
        {
            var seconds = _dialect.SecondsBetween(Visit(difference.Right), Visit(difference.Left));
            return divisor == 1 ? seconds : Sql.Sql.Empty.Append("(").Append(seconds).Append(" / " + divisor.ToString(CultureInfo.InvariantCulture) + ".0)");
        }

        throw Unsupported(node);
    }

    /// <summary>The seconds in the unit of a <see cref="TimeSpan"/> total, such as 86400 for <c>TotalDays</c>.</summary>
    private static int? SecondsPer(string member) => member switch
    {
        nameof(TimeSpan.TotalDays) => 86400,
        nameof(TimeSpan.TotalHours) => 3600,
        nameof(TimeSpan.TotalMinutes) => 60,
        nameof(TimeSpan.TotalSeconds) => 1,
        _ => null,
    };

    /// <summary>Translates <c>test ? a : b</c>; a bool result is 1 or 0, so SQL Server and PostgreSQL can use it as a value.</summary>
    private Sql.Sql VisitConditional(ConditionalExpression node)
    {
        if (!DependsOnEntity(node.Test))
        {
            return Visit(Evaluate(node.Test) is true ? node.IfTrue : node.IfFalse);
        }

        var boolean = IsBoolean(node.Type);
        return Sql.Sql.Empty.Append("CASE WHEN ").Append(Condition(node.Test))
            .Append(" THEN ").Append(boolean ? BoolAsInt(node.IfTrue) : Visit(node.IfTrue))
            .Append(" ELSE ").Append(boolean ? BoolAsInt(node.IfFalse) : Visit(node.IfFalse)).Append(" END");
    }

    /// <summary>A bool as the integer 1 or 0, or <c>NULL</c> for a null bool.</summary>
    private Sql.Sql BoolAsInt(Expression node)
    {
        if (!DependsOnEntity(node))
        {
            return Evaluate(node) is bool flag ? Parameter(flag ? 1 : 0, typeof(int)) : Parameter(null, typeof(int));
        }

        if (IsProjectedCondition(node))
        {
            return Projected(node);
        }

        // A bool column: NULL stays NULL, so it equals neither 1 nor 0.
        var condition = Condition(node);
        return Sql.Sql.Empty.Append("CASE WHEN ").Append(condition).Append(" THEN 1 WHEN NOT ").Append(condition).Append(" THEN 0 END");
    }

    /// <summary>Translates <c>g.Key</c>, or a member of a composite key (<c>g.Key.Name</c>).</summary>
    private Sql.Sql? GroupKeyMember(MemberExpression node)
    {
        if (node.Expression == _entity && node.Member.Name == "Key")
        {
            return _group!.IsComposite
                ? throw new NotSupportedException("A composite grouping key can't be used whole here; use its members, such as g.Key.Name.")
                : Sql.Sql.Empty.Append(_group.Parts[0].Value);
        }

        if (_group!.IsComposite && node.Expression is MemberExpression { Member.Name: "Key" } key && key.Expression == _entity)
        {
            return Sql.Sql.Empty.Append(_group.Parts.First(p => p.Name == node.Member.Name).Value);
        }

        return null;
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

        if (_group is not null && method.DeclaringType == typeof(Enumerable) && node.Arguments.Count > 0 && node.Arguments[0] == _entity)
        {
            return Aggregate(node);
        }

        if (IsSubquery(node))
        {
            return Subquery(node);
        }

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
                    return _dialect.Trim(Visit(node.Object!), leading: true, trailing: true);
                case nameof(string.TrimStart) when node.Arguments.Count == 0:
                    return _dialect.Trim(Visit(node.Object!), leading: true, trailing: false);
                case nameof(string.TrimEnd) when node.Arguments.Count == 0:
                    return _dialect.Trim(Visit(node.Object!), leading: false, trailing: true);
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
                    return Sql.Sql.Empty.Append("(").Append(text).Append(" IS NULL OR ").Append(_dialect.Trim(text, leading: true, trailing: true)).Append(" = '')");
                case nameof(string.Equals) when node.Object is not null && node.Arguments.Count == 1:
                    return Sql.Sql.Empty.Append("(").Append(Visit(node.Object)).Append(" = ").Append(Visit(node.Arguments[0])).Append(")");
                case nameof(string.IndexOf) when node.Object is not null && node.Arguments.Count == 1 && IsText(node.Arguments[0].Type):
                    // Like C#, the empty string is found at 0 (SQL Server's CHARINDEX finds it nowhere).
                    return !DependsOnEntity(node.Arguments[0]) && Evaluate(node.Arguments[0]) is ""
                        ? Parameter(0, typeof(int))
                        : Sql.Sql.Empty.Append("(").Append(_dialect.Position(Visit(node.Object), Text(node.Arguments[0]))).Append(" - 1)");
                case nameof(string.PadLeft) or nameof(string.PadRight) when node.Object is not null && node.Arguments.Count is 1 or 2:
                    return _dialect.Pad(
                        Visit(node.Object),
                        Visit(node.Arguments[0]),
                        node.Arguments.Count == 2 ? Text(node.Arguments[1]) : Parameter(" ", typeof(string)),
                        left: method.Name == nameof(string.PadLeft));
                case nameof(string.Compare) when node.Object is null && node.Arguments.Count == 2 && node.Arguments.All(a => a.Type == typeof(string)):
                    return CompareStrings(node.Arguments[0], node.Arguments[1]);
                case nameof(string.CompareTo) when node.Object is not null && node.Arguments.Count == 1 && node.Arguments[0].Type == typeof(string):
                    return CompareStrings(node.Object, node.Arguments[0]);
                case nameof(string.Concat) when node.Object is null:
                    return Concat(node.Arguments is [NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array] ? array.Expressions : node.Arguments, node);
                case nameof(string.Format) when node.Object is null && node.Arguments.Count >= 2 && node.Arguments[0].Type == typeof(string):
                    return Format(node);
                case nameof(ToString) when node.Object is not null && node.Arguments.Count == 0:
                    return Visit(node.Object);
            }
        }

        if (method.Name == nameof(ToString) && node.Object is not null && node.Arguments.Count == 0 && IsIntegral(node.Object.Type))
        {
            // Nullable<int>.ToString() is "" for null.
            return NullAsEmpty(node.Object, CastToText(Visit(node.Object)));
        }

        if ((method.DeclaringType == typeof(DateTime) || method.DeclaringType == typeof(DateOnly)) && node.Object is not null &&
            node.Arguments.Count == 1 && DateUnitOf(method.Name) is { } unit)
        {
            return _dialect.DateAdd(unit, Visit(node.Object), Amount(node.Arguments[0], out var whole), whole, dateOnly: method.DeclaringType == typeof(DateOnly));
        }

        if (method.DeclaringType == typeof(Math) && node.Arguments.Count == 2)
        {
            switch (method.Name)
            {
                case nameof(Math.Pow):
                    // Cast, or SQL Server returns the type of the base: POWER(2, 0.5) is 1.
                    return Sql.Sql.Empty.Append("POWER(CAST(").Append(Visit(node.Arguments[0])).Append(" AS " + _dialect.FloatType + "), ")
                        .Append(Visit(node.Arguments[1])).Append(")");
                case nameof(Math.Min) or nameof(Math.Max):
                    var a = Visit(node.Arguments[0]);
                    var b = Visit(node.Arguments[1]);
                    return Sql.Sql.Empty.Append("CASE WHEN ").Append(a).Append(method.Name == nameof(Math.Min) ? " <= " : " >= ").Append(b)
                        .Append(" THEN ").Append(a).Append(" ELSE ").Append(b).Append(" END");
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
                case nameof(Math.Sqrt):
                    return Function("SQRT", node.Arguments[0]);
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
            if (nonNull.Count == 0)
            {
                // Not "IN (NULL)": that is unknown rather than false, so NOT (x IN (NULL)) matches nothing.
                return values.Count == 0
                    ? Sql.Sql.Raw("1 = 0")
                    : Sql.Sql.Empty.Append("(").Append(column).Append(" IS NULL)");
            }

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

    /// <summary>Translates an aggregate over a group, such as <c>g.Count()</c> or <c>g.Sum(x =&gt; x.Amount)</c>.</summary>
    private Sql.Sql Aggregate(MethodCallExpression node)
    {
        var selector = node.Arguments.Count == 2 ? StripQuote(node.Arguments[1]) as LambdaExpression : null;
        switch (node.Method.Name)
        {
            case nameof(Enumerable.Count) or nameof(Enumerable.LongCount) when node.Arguments.Count == 1:
                return Sql.Sql.Raw("COUNT(*)");
            case nameof(Enumerable.Count) or nameof(Enumerable.LongCount) when selector is not null:
                return Sql.Sql.Empty.Append("COUNT(CASE WHEN ").Append(Rows(selector).Condition(selector.Body)).Append(" THEN 1 END)");
            case nameof(Enumerable.Sum) when selector is not null:
                // LINQ sums no values to zero; SQL to NULL.
                return Sql.Sql.Empty.Append("COALESCE(SUM(").Append(Rows(selector).Visit(selector.Body)).Append("), 0)");
            case nameof(Enumerable.Min) or nameof(Enumerable.Max) when selector is not null:
                return Sql.Sql.Empty.Append(node.Method.Name.ToUpperInvariant() + "(").Append(Rows(selector).Visit(selector.Body)).Append(")");
            case nameof(Enumerable.Average) when selector is not null:
                // Integers are averaged as floats so the fraction survives, as in LINQ; decimals stay exact.
                var value = Rows(selector).Visit(selector.Body);
                return (Nullable.GetUnderlyingType(node.Type) ?? node.Type) == typeof(decimal)
                    ? Sql.Sql.Empty.Append("AVG(").Append(value).Append(")")
                    : Sql.Sql.Empty.Append("AVG(CAST(").Append(value).Append(" AS " + _dialect.FloatType + "))");
            default:
                throw Unsupported(node);
        }

        ExpressionTranslator Rows(LambdaExpression lambda) => new(_map, lambda.Parameters[0], _dialect, subqueries: false);
    }

    /// <summary>
    /// Whether a call is a subquery: <c>query.Any(...)</c> on a typed query, or
    /// <c>projection.Contains(value)</c> on a projection.
    /// </summary>
    internal static bool IsSubquery(MethodCallExpression node) =>
        node.Object is not null &&
        node.Method.DeclaringType is { IsGenericType: true } type &&
        ((type.GetGenericTypeDefinition() == typeof(Query<>) && node.Method.Name is nameof(Query<object>.Any) or nameof(Query<object>.All)) ||
         (type.GetGenericTypeDefinition() == typeof(Projection<,>) && node.Method.Name == nameof(Projection<object, object>.Contains)));

    /// <summary>
    /// Translates <c>query.Any(x =&gt; ...)</c> into <c>EXISTS (...)</c>, its predicate correlated to the
    /// enclosing rows, <c>query.All(x =&gt; ...)</c> into <c>NOT EXISTS</c> of a row that fails it, and <c>projection.Contains(value)</c> into an <c>EXISTS</c> matching the value,
    /// <c>NULL</c> included as in C# (with <c>IN</c>, a <c>NULL</c> would make <c>NOT</c> match nothing).
    /// A filtered or paged source is a derived table, so its own filters and paging stay inside it.
    /// </summary>
    private Sql.Sql Subquery(MethodCallExpression node)
    {
        if (!_subqueries)
        {
            throw new NotSupportedException($"MicroORM cannot translate '{node}': subqueries aren't supported in grouped queries.");
        }

        if (EntityFinder.Finds(node.Object!, _parameters, subqueries: false))
        {
            throw new NotSupportedException($"MicroORM cannot translate '{node}': the subquery itself can't depend on the row. Correlate it in the predicate instead, such as orders.Any(o => o.CustomerId == c.Id).");
        }

        // One alias per nesting level: a subquery's own subqueries are one level deeper.
        var alias = "s" + _outer.Count.ToString(CultureInfo.InvariantCulture);
        var all = node.Method.Name == nameof(Query<object>.All);
        var exists = Sql.Sql.Empty.Append(all ? "NOT EXISTS (SELECT 1 FROM " : "EXISTS (SELECT 1 FROM ");
        switch (Evaluate(node.Object!))
        {
            case ISubquerySource query:
                RequireSameDialect(query.Session.Dialect, node);
                exists.Append(query.FromItem(alias));
                if (node.Arguments.Count == 1)
                {
                    var predicate = (LambdaExpression)StripQuote(node.Arguments[0]);
                    var nested = new ExpressionTranslator(query.Map, predicate.Parameters[0], _dialect, qualifier: alias, outer: [new Scope(_entity, _map, Reference), .. _outer]);

                    // All looks for a row where the predicate isn't true, unknown included, as in C#.
                    exists.Append(" WHERE ").Append(nested.Condition(all ? Expression.Not(predicate.Body) : predicate.Body));
                }

                return exists.Append(")");
            case IValueSubquerySource projection:
                RequireSameDialect(projection.Session.Dialect, node);
                if (!projection.SelectsSingleValue)
                {
                    throw new NotSupportedException($"MicroORM cannot translate '{node}': Contains needs a projection of one value, such as orders.Select(o => o.CustomerId).");
                }

                var column = Sql.Sql.Identifier(alias + "." + ValueAlias);
                var item = node.Arguments[0];
                Sql.Sql match;
                if (!DependsOnEntity(item))
                {
                    var value = Evaluate(item);
                    match = value is null
                        ? Sql.Sql.Empty.Append("(").Append(column).Append(" IS NULL)")
                        : Sql.Sql.Empty.Append("(").Append(column).Append(" = ").Append(Parameter(value, item.Type)).Append(")");
                }
                else
                {
                    // Qualified, so a column of the subquery can't shadow this row's.
                    var value = new ExpressionTranslator(_map, _entity, _dialect, qualifier: Reference, outer: _outer).Visit(item);
                    var type = StripConvert(item).Type;
                    match = type.IsValueType && Nullable.GetUnderlyingType(type) is null
                        ? Sql.Sql.Empty.Append("(").Append(column).Append(" = ").Append(value).Append(")")
                        : Sql.Sql.Empty.Append("(").Append(column).Append(" = ").Append(value)
                            .Append(" OR (").Append(column).Append(" IS NULL AND ").Append(value).Append(" IS NULL))");
                }

                return exists.Append(projection.FromItem(alias)).Append(" WHERE ").Append(match).Append(")");
            default:
                throw Unsupported(node);
        }
    }

    private void RequireSameDialect(SqlDialect dialect, Expression node)
    {
        if (!ReferenceEquals(dialect, _dialect))
        {
            throw new NotSupportedException($"MicroORM cannot translate '{node}': the subquery is for {dialect}, but the query for {_dialect}.");
        }
    }

    /// <summary>
    /// Translates <c>string.Compare(a, b)</c> or <c>a.CompareTo(b)</c>: -1, 0 or 1 in the database's
    /// collation, with <see langword="null"/> before any string as in C#.
    /// </summary>
    private Sql.Sql CompareStrings(Expression left, Expression right)
    {
        var a = Visit(left);
        var b = Visit(right);
        var sql = Sql.Sql.Empty.Append("CASE");
        if (CanBeNull(left) || CanBeNull(right))
        {
            sql.Append(" WHEN ").Append(a).Append(" IS NULL AND ").Append(b).Append(" IS NULL THEN 0 WHEN ")
                .Append(a).Append(" IS NULL THEN -1 WHEN ").Append(b).Append(" IS NULL THEN 1");
        }

        return sql.Append(" WHEN ").Append(a).Append(" = ").Append(b).Append(" THEN 0 WHEN ").Append(a).Append(" < ").Append(b).Append(" THEN -1 ELSE 1 END");
    }

    /// <summary>Translates <c>string.Concat(...)</c>, treating null as empty as C# does.</summary>
    private Sql.Sql Concat(IReadOnlyList<Expression> parts, Expression node)
    {
        if (parts.Any(p => typeof(IEnumerable).IsAssignableFrom(p.Type) && p.Type != typeof(string)))
        {
            throw Unsupported(node);
        }

        return ConcatPieces(parts.Select(p => ConcatPiece(p, node)).ToList());
    }

    /// <summary>
    /// Translates <c>string.Format</c> with a format string known up front, as interpolated strings
    /// compile to in expression trees: <c>$"{x.First} {x.Last}"</c>. Placeholders can't have an
    /// alignment or a format, which follow .NET's culture rules.
    /// </summary>
    private Sql.Sql Format(MethodCallExpression node)
    {
        var format = node.Arguments[0];
        IReadOnlyList<Expression> arguments = node.Arguments.Count == 2 && node.Arguments[1] is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array
            ? array.Expressions
            : node.Arguments.Skip(1).ToList();
        if (DependsOnEntity(format) || Evaluate(format) is not string text)
        {
            throw Unsupported(node);
        }

        var pieces = new List<Sql.Sql>();
        var literal = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                literal.Append(c);
                i++;
                continue;
            }

            if (c == '}')
            {
                throw Unsupported(node);
            }

            if (c != '{')
            {
                literal.Append(c);
                continue;
            }

            var end = text.IndexOf('}', i);
            if (end < 0 || !int.TryParse(text.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= arguments.Count)
            {
                throw new NotSupportedException($"MicroORM cannot translate '{node}': placeholders can't have an alignment or a format, such as {{0:N2}}; format the value in .NET instead.");
            }

            if (literal.Length > 0)
            {
                pieces.Add(Parameter(literal.ToString(), typeof(string)));
                literal.Clear();
            }

            pieces.Add(ConcatPiece(arguments[index], node));
            i = end;
        }

        if (literal.Length > 0)
        {
            pieces.Add(Parameter(literal.ToString(), typeof(string)));
        }

        return ConcatPieces(pieces);
    }

    private Sql.Sql ConcatPieces(List<Sql.Sql> pieces) => pieces.Count switch
    {
        0 => Parameter(string.Empty, typeof(string)),
        1 => pieces[0],
        _ => pieces.Skip(1).Aggregate(pieces[0], _dialect.Concat),
    };

    /// <summary>
    /// One value of a concatenation, as text: a value is formatted in .NET, a string column is
    /// empty when null, and an integer column is cast. Other columns (numbers with fractions,
    /// dates) format differently in SQL and are rejected.
    /// </summary>
    private Sql.Sql ConcatPiece(Expression node, Expression call)
    {
        var value = StripConvert(node);
        if (!DependsOnEntity(value))
        {
            return Parameter(Evaluate(value)?.ToString() ?? string.Empty, typeof(string));
        }

        if (value.Type == typeof(string))
        {
            return NullAsEmpty(value);
        }

        return IsIntegral(value.Type)
            ? NullAsEmpty(value, CastToText(Visit(value)))
            : throw new NotSupportedException($"MicroORM cannot translate '{call}': only strings and integers can be concatenated, since SQL formats {value.Type.Name} differently from .NET.");
    }

    private Sql.Sql CastToText(Sql.Sql value) => Sql.Sql.Empty.Append("CAST(").Append(value).Append(" AS " + _dialect.TextType + ")");

    /// <summary>The unit an <c>AddYears</c>...<c>AddSeconds</c> method of <see cref="DateTime"/> or <see cref="DateOnly"/> adds.</summary>
    private static DatePart? DateUnitOf(string method) => method switch
    {
        nameof(DateTime.AddYears) => DatePart.Year,
        nameof(DateTime.AddMonths) => DatePart.Month,
        nameof(DateTime.AddDays) => DatePart.Day,
        nameof(DateTime.AddHours) => DatePart.Hour,
        nameof(DateTime.AddMinutes) => DatePart.Minute,
        nameof(DateTime.AddSeconds) => DatePart.Second,
        _ => null,
    };

    /// <summary>The amount a date method adds, and whether it is known to be a whole number.</summary>
    private Sql.Sql Amount(Expression node, out bool whole)
    {
        if (!DependsOnEntity(node))
        {
            return Number(Convert.ToDouble(Evaluate(node), CultureInfo.InvariantCulture), out whole);
        }

        whole = IsIntegral(StripConvert(node).Type);
        return Visit(node);
    }

    /// <summary>A number as a parameter: an integer when it is whole, as SQL Server's DATEADD needs.</summary>
    private static Sql.Sql Number(double value, out bool whole)
    {
        whole = value == Math.Floor(value) && Math.Abs(value) <= long.MaxValue;
        if (!whole)
        {
            return Parameter(value, typeof(double));
        }

        return value is >= int.MinValue and <= int.MaxValue ? Parameter((int)value, typeof(int)) : Parameter((long)value, typeof(long));
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

        return _dialect.StringMatch(Visit(target), text, prefix, suffix);
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

        // Under C# 14, an array of a type that isn't IEquatable<T> (such as int?) binds to
        // MemoryExtensions.Contains(span, value, comparer) with a null comparer.
        var nullComparer = node.Arguments.Count == 3 && node.Method.DeclaringType == typeof(MemoryExtensions) &&
            node.Arguments[2] is ConstantExpression { Value: null };
        if (node.Object is null && (node.Arguments.Count == 2 || nullComparer) &&
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

    private static bool IsText(Type type) => type == typeof(string) || type == typeof(char);

    private static bool IsFractional(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(double) || type == typeof(float) || type == typeof(decimal);
    }

    private static bool IsDateTime(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTime);

    private static bool IsTimeSpan(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(TimeSpan);

    private static bool IsDateOrTime(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
    }

    private static bool IsIntegral(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return !type.IsEnum && Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
    }

    /// <summary>The type a side has in SQL: a value is sent as its converted type, but a column's conversion is dropped.</summary>
    private Type SqlType(Expression node) => DependsOnEntity(node) ? StripConvert(node).Type : node.Type;

    /// <summary>Whether an expression can be <c>NULL</c> in SQL: a nullable column, a null value, or a string function of one.</summary>
    private bool CanBeNull(Expression node)
    {
        if (!DependsOnEntity(node))
        {
            return Evaluate(node) is null;
        }

        var stripped = StripConvert(node);
        if (stripped.Type.IsValueType && Nullable.GetUnderlyingType(stripped.Type) is null)
        {
            return false;
        }

        return stripped switch
        {
            // A property declared non-nullable (string, not string?) is taken at its word.
            MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression } =>
                new NullabilityInfoContext().Create(property).ReadState != NullabilityState.NotNull,
            BinaryExpression { NodeType: ExpressionType.Add } add when add.Type == typeof(string) => false,
            BinaryExpression { NodeType: ExpressionType.Coalesce } coalesce => CanBeNull(coalesce.Right),
            ConditionalExpression conditional => CanBeNull(conditional.IfTrue) || CanBeNull(conditional.IfFalse),
            // Concatenations and ToString() treat null as empty.
            MethodCallExpression { Object: null, Method.Name: nameof(string.Concat) or nameof(string.Format) } call when call.Method.DeclaringType == typeof(string) => false,
            MethodCallExpression { Method.Name: nameof(ToString), Arguments.Count: 0 } => false,
            MethodCallExpression { Object: { } target } call when call.Method.DeclaringType == typeof(string) => CanBeNull(target),
            _ => true,
        };
    }

    /// <summary>Whether a condition can be unknown in SQL, because of a <c>NULL</c>, where C# gives true or false.</summary>
    private bool MayBeUnknown(Expression node)
    {
        node = StripConvert(node);
        if (!DependsOnEntity(node))
        {
            return false;
        }

        return node switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } logical => MayBeUnknown(logical.Left) || MayBeUnknown(logical.Right),
            // Null checks are never unknown, and Compare makes != never unknown either.
            BinaryExpression { NodeType: ExpressionType.Equal } equal => !IsNullValue(equal.Left) && !IsNullValue(equal.Right) && (CanBeNull(equal.Left) || CanBeNull(equal.Right)),
            BinaryExpression { NodeType: ExpressionType.NotEqual } => false,
            BinaryExpression comparison => CanBeNull(comparison.Left) || CanBeNull(comparison.Right),
            UnaryExpression { NodeType: ExpressionType.Not } not => not.Type != typeof(bool),
            MemberExpression { Member.Name: "HasValue" } => false,
            MemberExpression member => CanBeNull(member),
            MethodCallExpression call when IsSubquery(call) => false,
            MethodCallExpression { Method.Name: nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace) } call when call.Method.DeclaringType == typeof(string) => false,
            _ => true,
        };
    }

    private bool IsNullValue(Expression node) => !DependsOnEntity(node) && Evaluate(node) is null;

    private bool DependsOnEntity(Expression node) => EntityFinder.Finds(node, _parameters, subqueries: true);

    private static Expression StripQuote(Expression node) =>
        node is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : node;

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
      new($"MicroORM cannot translate '{node}' to SQL. Supported: comparisons, &&, ||, !, arithmetic, null checks, conditionals (?:), string StartsWith/EndsWith/Contains/IndexOf/ToUpper/ToLower/Trim/TrimStart/TrimEnd/PadLeft/PadRight/Substring/Replace/Length/IsNullOrWhiteSpace/Compare/Concat/Format, integer ToString, DateTime and DateOnly Year/Month/Day/Hour/Minute/Second/Date and AddYears...AddSeconds, (date - date).TotalDays/TotalHours/TotalMinutes/TotalSeconds, Math.Abs/Floor/Ceiling/Sqrt/Pow/Min/Max, casts to integers, SqlFunctions.Like, collection Contains, subqueries (query.Any, query.All, projection.Contains) and, over groups, Count/Sum/Min/Max/Average. Use .Where($\"...\") for anything else.");

    /// <summary>An entity of an enclosing query, which a subquery refers to by <paramref name="Reference"/> (its alias or table).</summary>
    private sealed record Scope(ParameterExpression Parameter, EntityMap Map, string Reference);
}
