using System.Linq.Expressions;

namespace TechShop.Application.Common.Query;

/// <summary>
/// Whitelist of the fields a resource allows for sorting, filtering, searching and export.
/// Nothing outside this map can reach the database, which is what keeps the generic
/// query endpoint free of injection through field names.
/// </summary>
public sealed class QuerySpec<T>
{
    private readonly Dictionary<string, FieldDescriptor> _fields = new(StringComparer.OrdinalIgnoreCase);

    public sealed record FieldDescriptor(string Name, string Header, LambdaExpression Selector, Type ValueType, bool Searchable);

    public IReadOnlyDictionary<string, FieldDescriptor> Fields => _fields;

    public QuerySpec<T> Map<TValue>(string name, Expression<Func<T, TValue>> selector, string? header = null, bool searchable = false)
    {
        _fields[name] = new FieldDescriptor(name, header ?? Humanize(name), selector, typeof(TValue), searchable);
        return this;
    }

    public QuerySpec<T> Text<TValue>(string name, Expression<Func<T, TValue>> selector, string? header = null)
        => Map(name, selector, header, searchable: true);

    public FieldDescriptor? Find(string? name)
        => name is not null && _fields.TryGetValue(name, out var f) ? f : null;

    public IReadOnlyList<FieldDescriptor> SearchableFields
        => _fields.Values.Where(f => f.Searchable).ToList();

    public IReadOnlyList<FieldDescriptor> ResolveColumns(IEnumerable<string> requested)
    {
        var picked = requested.Select(Find).Where(f => f is not null).Select(f => f!).ToList();
        return picked.Count > 0 ? picked : _fields.Values.ToList();
    }

    private static string Humanize(string name)
    {
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(i == 0 ? char.ToUpperInvariant(name[i]) : name[i]);
        }

        return new string([.. chars]);
    }
}

