using System.Linq.Expressions;
using System.Text;

namespace SystemX.Auth2.Server.Application.Lists;

/// <summary>One sort field. <c>Descending</c> comes from the <c>-</c> prefix.</summary>
public sealed record SortSpec(string Field, bool Descending);

/// <summary>
/// The parsed query of every admin list (page and API). Built by <see cref="ListQueryParser"/>; state lives in the URL.
/// </summary>
public sealed record ListQuery(int Page, int PageSize, IReadOnlyList<SortSpec> Sort, string? Q,
    IReadOnlyDictionary<string, string[]> Filters, string? Cursor = null)
{
    /// <summary>The page sizes a caller may pick.</summary>
    public static readonly int[] AllowedPageSizes = [10, 25, 50, 100];
    /// <summary>The default page size.</summary>
    public const int DefaultPageSize = 25;
    /// <summary>Largest page size of the API.</summary>
    public const int ApiMaxPageSize = 200;
    /// <summary>Shortest search text.</summary>
    public const int MinSearchLength = 2;
    /// <summary>Longest search text.</summary>
    public const int MaxSearchLength = 100;
    /// <summary>Most sort fields.</summary>
    public const int MaxSortFields = 2;
}

/// <summary>One page of a list.</summary>
public sealed record ListPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, bool TotalIsCapped, int TotalPages, bool HasNext, bool HasPrevious, string? NextCursor = null, string? PreviousCursor = null);

/// <summary>A query value that is not valid. The API answers 400 naming the parameter; the page ignores it and shows a notice.</summary>
public sealed record ListProblem(string Parameter, string Message);

/// <summary>A filter of a list. It turns the raw values of one name into a where clause. It never builds a column name from the value.</summary>
public sealed class FilterRule<TEntity>
{
    /// <summary>Makes the predicate from the values (already trimmed). Return null when a value is not valid.</summary>
    public required Func<string[], Expression<Func<TEntity, bool>>?> Build { get; init; }
    /// <summary>Range filters (<c>f.name</c> + <c>From</c> and <c>To</c>) use <see cref="BuildRange"/> instead.</summary>
    public Func<string?, string?, Expression<Func<TEntity, bool>>?>? BuildRange { get; init; }
}

/// <summary>What an entity declares so that its list works: the whitelists, the search, the default sort and the projection to a row.</summary>
public sealed class ListDefinition<TEntity, TRow>
{
    /// <summary>Sort fields (name to key selector). Only these names can be sorted.</summary>
    public required IReadOnlyDictionary<string, Expression<Func<TEntity, object?>>> Sortable { get; init; }
    /// <summary>Filter names. Only these can be used as <c>f.name</c>.</summary>
    public required IReadOnlyDictionary<string, FilterRule<TEntity>> Filters { get; init; }
    /// <summary>Builds the search predicate from a text whose wildcard characters are already escaped (<see cref="ListQueryParser.EscapeLike"/>).</summary>
    public required Func<string, Expression<Func<TEntity, bool>>> Search { get; init; }
    /// <summary>The sort when none is given (for example <c>-createdAt</c>).</summary>
    public required string DefaultSort { get; init; }
    /// <summary>The stable tiebreaker that is always added last (normally the id).</summary>
    public required Expression<Func<TEntity, object?>> Tiebreaker { get; init; }
    /// <summary>The projection to the row DTO. Lists never load entities.</summary>
    public required Expression<Func<TEntity, TRow>> Select { get; init; }
}

/// <summary>Parses and checks the query string of a list. One implementation for pages and API.</summary>
public static class ListQueryParser
{
    /// <summary>The escape character used with <c>EF.Functions.ILike(..., escape)</c>.</summary>
    public const string EscapeChar = "\\";

    /// <summary>Escapes <c>%</c>, <c>_</c> and the escape character, so a search text is only text.</summary>
    public static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// Parses <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>q</c>, <c>cursor</c> and <c>f.*</c>. Values that are not valid are reported in <paramref name="problems"/>
    /// and replaced by the default. Sort and filter names are checked against the definition.
    /// </summary>
    public static ListQuery Parse<TEntity, TRow>(IEnumerable<KeyValuePair<string, string[]>> query, ListDefinition<TEntity, TRow> definition, List<ListProblem> problems, bool api = false)
    {
        var values = query.GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.SelectMany(x => x.Value).ToArray(), StringComparer.OrdinalIgnoreCase);
        string? One(string name) => values.TryGetValue(name, out var v) ? v.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() : null;

        var page = 1;
        if (One("page") is { } p) { if (int.TryParse(p, out var n) && n >= 1) page = n; else problems.Add(new("page", "Must be a whole number of at least 1.")); }

        var size = ListQuery.DefaultPageSize;
        if (One("pageSize") is { } ps)
        {
            if (!int.TryParse(ps, out var n)) problems.Add(new("pageSize", "Must be a whole number."));
            else if (api && n is >= 1 and <= ListQuery.ApiMaxPageSize) size = n;
            else if (ListQuery.AllowedPageSizes.Contains(n)) size = n;
            else problems.Add(new("pageSize", api ? $"Must be between 1 and {ListQuery.ApiMaxPageSize}." : "Must be one of " + string.Join(", ", ListQuery.AllowedPageSizes) + "."));
        }

        var sort = new List<SortSpec>();
        foreach (var raw in (One("sort") ?? definition.DefaultSort).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var desc = raw.StartsWith('-');
            var name = desc ? raw[1..] : raw;
            var key = definition.Sortable.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (key is null) { problems.Add(new("sort", $"'{name}' cannot be sorted.")); continue; }
            if (sort.Count >= ListQuery.MaxSortFields) { problems.Add(new("sort", $"At most {ListQuery.MaxSortFields} fields.")); break; }
            if (sort.All(s => s.Field != key)) sort.Add(new SortSpec(key, desc));
        }
        if (sort.Count == 0) foreach (var raw in definition.DefaultSort.Split(',')) sort.Add(new SortSpec(raw.TrimStart('-'), raw.StartsWith('-')));

        string? q = null;
        if (One("q") is { } text)
        {
            if (text.Length < ListQuery.MinSearchLength) problems.Add(new("q", $"At least {ListQuery.MinSearchLength} characters."));
            else if (text.Length > ListQuery.MaxSearchLength) problems.Add(new("q", $"At most {ListQuery.MaxSearchLength} characters."));
            else q = text;
        }

        var filters = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, v) in values.Where(k => k.Key.StartsWith("f.", StringComparison.OrdinalIgnoreCase)))
        {
            var name = key[2..];
            var baseName = name.EndsWith("From", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name.EndsWith("To", StringComparison.OrdinalIgnoreCase) ? name[..^2] : name;
            var known = definition.Filters.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                        ?? definition.Filters.Keys.FirstOrDefault(k => string.Equals(k, baseName, StringComparison.OrdinalIgnoreCase) && definition.Filters[k].BuildRange is not null);
            if (known is null) { problems.Add(new(key, "Unknown filter.")); continue; }
            var canonical = definition.Filters.ContainsKey(name) ? known : known + (name.EndsWith("From", StringComparison.OrdinalIgnoreCase) ? "From" : "To");
            var cleaned = v.Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).Take(20).ToArray();
            if (cleaned.Length > 0) filters[canonical] = cleaned;
        }

        return new ListQuery(page, size, sort, q, filters, One("cursor"));
    }

    /// <summary>The query as a query string again (for links that keep the state), without the page.</summary>
    public static string ToQueryString(ListQuery q, int? page = null, string? sortOverride = null)
    {
        var sb = new StringBuilder();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) sb.Append(sb.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)); }
        Add("page", (page ?? q.Page) > 1 ? (page ?? q.Page).ToString() : null);
        Add("pageSize", q.PageSize != ListQuery.DefaultPageSize ? q.PageSize.ToString() : null);
        Add("sort", sortOverride ?? string.Join(',', q.Sort.Select(s => (s.Descending ? "-" : "") + s.Field)));
        Add("q", q.Q);
        foreach (var (k, v) in q.Filters) foreach (var item in v) Add("f." + k, item);
        return sb.ToString();
    }
}
