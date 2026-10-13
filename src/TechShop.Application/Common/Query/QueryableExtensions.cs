using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace TechShop.Application.Common.Query;

/// <summary>
/// Translates a <see cref="QueryRequest"/> into parameterised LINQ against a whitelist.
/// Every value becomes an Expression constant, so no text ever reaches SQL directly.
/// </summary>
public static class QueryableExtensions
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StringEndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo StringToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    public static IQueryable<T> ApplyQuery<T>(this IQueryable<T> source, QueryRequest request, QuerySpec<T> spec)
        => source.ApplyFilters(request, spec).ApplySort(request, spec);

    public static IQueryable<T> ApplyFilters<T>(this IQueryable<T> source, QueryRequest request, QuerySpec<T> spec)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        Expression? predicate = null;

        foreach (var rule in request.Filters)
        {
            predicate = And(predicate, BuildRule(rule, spec, parameter));
        }

        if (request.FilterGroup is { IsEmpty: false })
        {
            predicate = And(predicate, BuildGroup(request.FilterGroup, spec, parameter));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            Expression? search = null;
            foreach (var field in spec.SearchableFields)
            {
                if (field.ValueType != typeof(string))
                {
                    continue;
                }

                var body = Rebind(field.Selector, parameter);
                search = Or(search, Like(body, request.Search));
            }

            predicate = And(predicate, search);
        }

        return predicate is null
            ? source
            : source.Where(Expression.Lambda<Func<T, bool>>(predicate, parameter));
    }

    public static IQueryable<T> ApplySort<T>(this IQueryable<T> source, QueryRequest request, QuerySpec<T> spec)
    {
        var applied = false;
        IOrderedQueryable<T>? ordered = null;

        foreach (var rule in request.Sort)
        {
            var field = spec.Find(rule.Field);
            if (field is null)
            {
                continue;
            }

            var method = applied
                ? (rule.Desc ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy))
                : (rule.Desc ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy));

            var call = Expression.Call(
                typeof(Queryable),
                method,
                [typeof(T), field.ValueType],
                (applied ? ordered! : source).Expression,
                Expression.Quote(field.Selector));

            ordered = (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
            applied = true;
        }

        return applied ? ordered! : source;
    }

    private static Expression? BuildGroup<T>(FilterGroup group, QuerySpec<T> spec, ParameterExpression parameter)
    {
        Expression? result = null;

        foreach (var rule in group.Rules)
        {
            var expr = BuildRule(rule, spec, parameter);
            result = group.Join == FilterJoin.Or ? Or(result, expr) : And(result, expr);
        }

        foreach (var nested in group.Groups)
        {
            var expr = BuildGroup(nested, spec, parameter);
            result = group.Join == FilterJoin.Or ? Or(result, expr) : And(result, expr);
        }

        return result;
    }

    private static Expression? BuildRule<T>(FilterRule rule, QuerySpec<T> spec, ParameterExpression parameter)
    {
        var field = spec.Find(rule.Field);
        if (field is null)
        {
            return null;
        }

        var member = Rebind(field.Selector, parameter);
        var type = field.ValueType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (rule.Operator == FilterOperator.IsEmpty)
        {
            return type == typeof(string)
                ? Expression.OrElse(
                    Expression.Equal(member, Expression.Constant(null, type)),
                    Expression.Equal(member, Expression.Constant(string.Empty, type)))
                : Expression.Equal(member, Expression.Constant(null, type));
        }

        if (rule.Operator == FilterOperator.IsNotEmpty)
        {
            return type == typeof(string)
                ? Expression.AndAlso(
                    Expression.NotEqual(member, Expression.Constant(null, type)),
                    Expression.NotEqual(member, Expression.Constant(string.Empty, type)))
                : Expression.NotEqual(member, Expression.Constant(null, type));
        }

        if (string.IsNullOrWhiteSpace(rule.Value) && rule.Operator != FilterOperator.In)
        {
            return null;
        }

        if (type == typeof(string))
        {
            return rule.Operator switch
            {
                FilterOperator.Equals => Expression.Equal(LowerOrNull(member), Expression.Constant(rule.Value!.ToLowerInvariant())),
                FilterOperator.NotEquals => Expression.NotEqual(LowerOrNull(member), Expression.Constant(rule.Value!.ToLowerInvariant())),
                FilterOperator.StartsWith => StringCall(member, StringStartsWith, rule.Value!),
                FilterOperator.EndsWith => StringCall(member, StringEndsWith, rule.Value!),
                FilterOperator.In => InString(member, rule.Value),
                _ => Like(member, rule.Value!)
            };
        }

        if (rule.Operator == FilterOperator.In)
        {
            return InScalar(member, underlying, type, rule.Value);
        }

        if (rule.Operator == FilterOperator.Between)
        {
            var low = Parse(underlying, rule.Value);
            var high = Parse(underlying, rule.Value2);
            if (low is null || high is null)
            {
                return null;
            }

            if (underlying == typeof(DateTime))
            {
                high = ((DateTime)high).Date.AddDays(1).AddTicks(-1);
            }

            return Expression.AndAlso(
                Expression.GreaterThanOrEqual(member, Expression.Constant(low, type)),
                Expression.LessThanOrEqual(member, Expression.Constant(high, type)));
        }

        var parsed = Parse(underlying, rule.Value);
        if (parsed is null)
        {
            return null;
        }

        var constant = Expression.Constant(parsed, type);

        return rule.Operator switch
        {
            FilterOperator.Equals => Expression.Equal(member, constant),
            FilterOperator.NotEquals => Expression.NotEqual(member, constant),
            FilterOperator.GreaterThan => Expression.GreaterThan(member, constant),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, constant),
            FilterOperator.LessThan => Expression.LessThan(member, constant),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(member, constant),
            _ => Expression.Equal(member, constant)
        };
    }

    private static Expression InString(Expression member, string? csv)
    {
        var values = Split(csv).Select(v => v.ToLowerInvariant()).ToList();
        if (values.Count == 0)
        {
            return Expression.Constant(true);
        }

        return Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(string)],
            Expression.Constant(values),
            LowerOrNull(member));
    }

    private static Expression InScalar(Expression member, Type underlying, Type declared, string? csv)
    {
        var parsed = Split(csv).Select(v => Parse(underlying, v)).Where(v => v is not null).ToList();
        if (parsed.Count == 0)
        {
            return Expression.Constant(true);
        }

        var listType = typeof(List<>).MakeGenericType(underlying);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var value in parsed)
        {
            list.Add(value);
        }

        var target = Nullable.GetUnderlyingType(declared) is null
            ? member
            : Expression.Property(member, "Value");

        return Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [underlying],
            Expression.Constant(list, listType),
            target);
    }

    private static string[] Split(string? csv)
        => (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static MethodCallExpression Like(Expression member, string value)
        => StringCall(member, StringContains, value);

    private static MethodCallExpression StringCall(Expression member, MethodInfo method, string value)
        => Expression.Call(LowerOrNull(member), method, Expression.Constant(value.ToLowerInvariant()));

    private static MethodCallExpression LowerOrNull(Expression member)
        => Expression.Call(
            Expression.Coalesce(member, Expression.Constant(string.Empty)),
            StringToLower);

    private static object? Parse(Type type, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();

        try
        {
            if (type.IsEnum)
            {
                return Enum.TryParse(type, raw, ignoreCase: true, out var e) ? e : null;
            }

            if (type == typeof(DateTime))
            {
                return DateTime.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var d)
                    ? d
                    : null;
            }

            if (type == typeof(bool))
            {
                if (bool.TryParse(raw, out var b))
                {
                    return b;
                }

                return raw == "1" ? true : raw == "0" ? false : null;
            }

            return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static Expression Rebind(LambdaExpression selector, ParameterExpression parameter)
        => new ParameterRebinder(selector.Parameters[0], parameter).Visit(selector.Body)!;

    private static Expression? And(Expression? left, Expression? right)
        => left is null ? right : right is null ? left : Expression.AndAlso(left, right);

    private static Expression? Or(Expression? left, Expression? right)
        => left is null ? right : right is null ? left : Expression.OrElse(left, right);

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}

