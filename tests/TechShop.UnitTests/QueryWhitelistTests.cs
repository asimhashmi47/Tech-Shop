using TechShop.Application.Common.Query;

namespace TechShop.UnitTests;

/// <summary>
/// The field whitelist is what keeps the generic query contract safe: a screen declares
/// the columns it offers, and nothing outside that list can reach the database. These
/// tests are as much a security boundary as a behaviour one.
/// </summary>
public class QueryWhitelistTests
{
    private sealed record Row(long Id, string Name, string? Note, decimal Amount, bool Active, DateTime On);

    private static readonly List<Row> Rows =
    [
        new(1, "Alpha", "first", 10m, true, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(2, "Bravo", null, 250m, false, new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)),
        new(3, "Charlie", "", 99.5m, true, new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc)),
        new(4, "alpha bis", "fourth", 10m, false, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
    ];

    /// <summary>Deliberately does not expose Active, so filtering on it must be impossible.</summary>
    private static readonly QuerySpec<Row> Spec = new QuerySpec<Row>()
        .Map("id", r => r.Id)
        .Text("name", r => r.Name)
        .Text("note", r => r.Note)
        .Map("amount", r => r.Amount)
        .Map("on", r => r.On);

    private static IQueryable<Row> Source => Rows.AsQueryable();

    // ---------------------------------------------------------------- whitelist

    [Fact]
    public void A_filter_on_a_field_the_spec_never_offered_is_ignored_not_applied()
    {
        var request = new QueryRequest
        {
            Filters = [new FilterRule { Field = "Active", Operator = FilterOperator.Equals, Value = "true" }],
        };

        // Ignoring it is the safe outcome: the alternative is a screen filtering on a
        // column it was never given.
        Assert.Equal(Rows.Count, Source.ApplyFilters(request, Spec).Count());
    }

    [Fact]
    public void A_sort_on_an_unknown_field_leaves_the_order_alone()
    {
        var request = new QueryRequest { Sort = [new SortRule { Field = "Active", Desc = true }] };

        Assert.Equal([1, 2, 3, 4], Source.ApplySort(request, Spec).Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Resolving_columns_drops_unknown_names_and_falls_back_to_everything_when_none_match()
    {
        Assert.Equal(["name", "amount"], Spec.ResolveColumns(["name", "nope", "amount"]).Select(f => f.Name));
        Assert.Equal(Spec.Fields.Count, Spec.ResolveColumns(["nope", "also-nope"]).Count);
    }

    [Fact]
    public void Only_fields_marked_searchable_take_part_in_a_quick_search()
    {
        Assert.Equal(["name", "note"], Spec.SearchableFields.Select(f => f.Name));
    }

    // ---------------------------------------------------------------- operators

    [Fact]
    public void Search_matches_any_searchable_text_field_without_regard_to_case()
    {
        var request = new QueryRequest { Search = "ALPHA" };

        Assert.Equal([1, 4], Source.ApplyFilters(request, Spec).Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Search_does_not_reach_a_numeric_field()
    {
        // "10" is the Amount of two rows, but Amount is not searchable, so a quick
        // search must not find them.
        var request = new QueryRequest { Search = "10" };

        Assert.Empty(Source.ApplyFilters(request, Spec).ToList());
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "Bravo", new[] { 2 })]
    [InlineData(FilterOperator.NotEquals, "Bravo", new[] { 1, 3, 4 })]
    [InlineData(FilterOperator.StartsWith, "alp", new[] { 1, 4 })]
    [InlineData(FilterOperator.EndsWith, "bis", new[] { 4 })]
    [InlineData(FilterOperator.Contains, "ar", new[] { 3 })]
    public void Text_operators_behave(FilterOperator op, string value, int[] expected)
    {
        var request = new QueryRequest
        {
            Filters = [new FilterRule { Field = "name", Operator = op, Value = value }],
        };

        Assert.Equal(expected, Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    [Fact]
    public void IsEmpty_treats_null_and_blank_text_the_same_way()
    {
        var request = new QueryRequest
        {
            Filters = [new FilterRule { Field = "note", Operator = FilterOperator.IsEmpty }],
        };

        Assert.Equal([2, 3], Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    [Fact]
    public void Between_includes_both_ends()
    {
        var request = new QueryRequest
        {
            Filters =
            [
                new FilterRule
                {
                    Field = "amount",
                    Operator = FilterOperator.Between,
                    Value = "10",
                    Value2 = "99.5",
                },
            ],
        };

        Assert.Equal([1, 3, 4], Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    [Fact]
    public void In_accepts_a_comma_separated_list()
    {
        var request = new QueryRequest
        {
            Filters = [new FilterRule { Field = "id", Operator = FilterOperator.In, Value = "2, 4" }],
        };

        Assert.Equal([2, 4], Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    [Fact]
    public void A_value_that_cannot_be_parsed_drops_the_rule_rather_than_throwing()
    {
        var request = new QueryRequest
        {
            Filters = [new FilterRule { Field = "amount", Operator = FilterOperator.Equals, Value = "not-a-number" }],
        };

        Assert.Equal(Rows.Count, Source.ApplyFilters(request, Spec).Count());
    }

    [Fact]
    public void Several_filters_combine_with_and()
    {
        var request = new QueryRequest
        {
            Filters =
            [
                new FilterRule { Field = "amount", Operator = FilterOperator.Equals, Value = "10" },
                new FilterRule { Field = "name", Operator = FilterOperator.StartsWith, Value = "alpha" },
            ],
        };

        Assert.Equal([1, 4], Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    [Fact]
    public void A_filter_group_joined_with_or_widens_the_result()
    {
        var request = new QueryRequest
        {
            FilterGroup = new FilterGroup
            {
                Join = FilterJoin.Or,
                Rules =
                [
                    new FilterRule { Field = "name", Operator = FilterOperator.Equals, Value = "Bravo" },
                    new FilterRule { Field = "name", Operator = FilterOperator.Equals, Value = "Charlie" },
                ],
            },
        };

        Assert.Equal([2, 3], Source.ApplyFilters(request, Spec).Select(r => (int)r.Id).ToArray());
    }

    // ---------------------------------------------------------------- paging

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-5, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 10_000)]
    public void Paging_clamps_nonsense_to_something_safe(int page, int pageSize)
    {
        var result = QueryRunner.RunInMemory(Rows, new QueryRequest { Page = page, PageSize = pageSize }, Spec);

        Assert.True(result.Page >= 1);
        Assert.InRange(result.PageSize, 1, 500);
        Assert.Equal(Rows.Count, result.TotalCount);
    }

    [Fact]
    public void A_page_reports_the_total_before_paging_not_the_rows_it_returned()
    {
        var result = QueryRunner.RunInMemory(Rows, new QueryRequest { Page = 1, PageSize = 2 }, Spec);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void Sorting_descending_reverses_the_order()
    {
        var request = new QueryRequest { Sort = [new SortRule { Field = "amount", Desc = true }] };

        Assert.Equal([2, 3, 1, 4], Source.ApplySort(request, Spec).Select(r => (int)r.Id).ToArray());
    }
}
