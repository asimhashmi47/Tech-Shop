namespace TechShop.Application.Common.Query;

public enum FilterOperator
{
    Contains = 1,
    Equals = 2,
    NotEquals = 3,
    StartsWith = 4,
    EndsWith = 5,
    GreaterThan = 6,
    GreaterThanOrEqual = 7,
    LessThan = 8,
    LessThanOrEqual = 9,
    Between = 10,
    In = 11,
    IsEmpty = 12,
    IsNotEmpty = 13
}

public enum FilterJoin
{
    And = 1,
    Or = 2
}

public sealed class FilterRule
{
    public string Field { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; } = FilterOperator.Contains;
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

/// <summary>One level of the advanced filter builder: rules joined by And/Or, plus nested groups.</summary>
public sealed class FilterGroup
{
    public FilterJoin Join { get; set; } = FilterJoin.And;
    public List<FilterRule> Rules { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];

    public bool IsEmpty => Rules.Count == 0 && Groups.TrueForAll(g => g.IsEmpty);
}

public sealed class SortRule
{
    public string Field { get; set; } = string.Empty;
    public bool Desc { get; set; }
}

public sealed record QueryRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public List<SortRule> Sort { get; set; } = [];

    /// <summary>Simple per-column filter row. Combined with And.</summary>
    public List<FilterRule> Filters { get; set; } = [];

    /// <summary>Advanced filter builder tree. Combined with the simple filters using And.</summary>
    public FilterGroup? FilterGroup { get; set; }

    /// <summary>Global quick search across the resource's searchable text fields.</summary>
    public string? Search { get; set; }

    /// <summary>Visible columns, used by the Excel export to mirror the grid.</summary>
    public List<string> Columns { get; set; } = [];

    public long? ShopId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

