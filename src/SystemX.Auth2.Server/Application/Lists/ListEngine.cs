using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SystemX.Auth2.Server.Application.Lists;

/// <summary>Applies a <see cref="ListQuery"/> to a query in SQL: filters, search, sort, tiebreaker and paging, then projects to the row. Never loads a whole table.</summary>
public static class ListEngine
{
    /// <summary>The count is capped at this value for large tables (the page says "10,000+").</summary>
    public const int CountCap = 10_000;

    /// <summary>Filters, searches and sorts (with the tiebreaker). No paging yet.</summary>
    public static IQueryable<TEntity> Apply<TEntity, TRow>(IQueryable<TEntity> source, ListQuery query, ListDefinition<TEntity, TRow> definition, List<ListProblem>? problems = null)
    {
        var q = source;
        foreach (var (name, values) in query.Filters)
        {
            var isFrom = name.EndsWith("From", StringComparison.Ordinal) && !definition.Filters.ContainsKey(name);
            var isTo = name.EndsWith("To", StringComparison.Ordinal) && !definition.Filters.ContainsKey(name);
            Expression<Func<TEntity, bool>>? predicate;
            if (isFrom || isTo)
            {
                var rule = definition.Filters[name[..^(isFrom ? 4 : 2)]];
                predicate = rule.BuildRange?.Invoke(isFrom ? values[0] : null, isTo ? values[0] : null);
            }
            else predicate = definition.Filters[name].Build(values);
            if (predicate is null) { problems?.Add(new("f." + name, "The value is not valid.")); continue; }
            q = q.Where(predicate);
        }
        if (query.Q is { } text) q = q.Where(definition.Search(ListQueryParser.EscapeLike(text)));

        IOrderedQueryable<TEntity>? ordered = null;
        foreach (var s in query.Sort)
        {
            var key = definition.Sortable[s.Field];
            ordered = ordered is null
                ? (s.Descending ? q.OrderByDescending(key) : q.OrderBy(key))
                : (s.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }
        // the tiebreaker keeps the order stable across pages: no duplicate and no missing rows
        ordered = ordered is null ? q.OrderBy(definition.Tiebreaker) : ordered.ThenBy(definition.Tiebreaker);
        return ordered;
    }

    /// <summary>Offset paging with an exact total.</summary>
    public static async Task<ListPage<TRow>> PageAsync<TEntity, TRow>(IQueryable<TEntity> source, ListQuery query, ListDefinition<TEntity, TRow> definition,
        List<ListProblem>? problems = null, CancellationToken ct = default)
    {
        var ordered = Apply(source, query, definition, problems);
        var total = await ordered.LongCountAsync(ct);
        var pages = (int)Math.Max(1, (total + query.PageSize - 1) / query.PageSize);
        var page = Math.Min(query.Page, pages);   // a page beyond the end is clamped to the last page
        var rows = await ordered.Skip((page - 1) * query.PageSize).Take(query.PageSize).Select(definition.Select).ToListAsync(ct);
        return new ListPage<TRow>(rows, page, query.PageSize, total, false, pages, page < pages, page > 1);
    }

    /// <summary>Offset paging with a capped count (for large tables): the total stops at <see cref="CountCap"/>.</summary>
    public static async Task<ListPage<TRow>> PageCappedAsync<TEntity, TRow>(IQueryable<TEntity> source, ListQuery query, ListDefinition<TEntity, TRow> definition,
        List<ListProblem>? problems = null, CancellationToken ct = default)
    {
        var ordered = Apply(source, query, definition, problems);
        var counted = await ordered.Take(CountCap + 1).CountAsync(ct);
        var capped = counted > CountCap;
        var total = Math.Min(counted, CountCap);
        var pages = (int)Math.Max(1, (total + query.PageSize - 1) / query.PageSize);
        var page = Math.Min(query.Page, pages);
        var rows = await ordered.Skip((page - 1) * query.PageSize).Take(query.PageSize).Select(definition.Select).ToListAsync(ct);
        return new ListPage<TRow>(rows, page, query.PageSize, total, capped, pages, page < pages || capped, page > 1);
    }

    /// <summary>
    /// Keyset paging for big append-only lists (audit, sessions, authorizations): newest first by a time and an id. The cursor is the last row's time and id,
    /// so inserts between two requests change nothing about the pages already read. No count, no offset.
    /// </summary>
    public static async Task<ListPage<TRow>> KeysetAsync<TEntity, TRow>(IQueryable<TEntity> filtered, Expression<Func<TEntity, DateTimeOffset>> time, Expression<Func<TEntity, Guid>> id,
        string? cursor, int pageSize, Expression<Func<TEntity, TRow>> select, Func<TRow, (DateTimeOffset Time, Guid Id)> keyOf, CancellationToken ct = default)
    {
        var q = filtered;
        if (Cursor.TryDecode(cursor, out var after))
        {
            // (time, id) < (cursorTime, cursorId), written out because EF cannot translate a tuple comparison
            var t = time.Parameters[0];
            var timeBody = ReplaceParameter(time.Body, time.Parameters[0], t);
            var idBody = ReplaceParameter(id.Body, id.Parameters[0], t);
            var less = Expression.OrElse(
                Expression.LessThan(timeBody, Expression.Constant(after.Time)),
                Expression.AndAlso(Expression.Equal(timeBody, Expression.Constant(after.Time)),
                    Expression.LessThan(Expression.Call(idBody, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, Expression.Constant(after.Id)), Expression.Constant(0))));
            q = q.Where(Expression.Lambda<Func<TEntity, bool>>(less, t));
        }
        var rows = await q.OrderByDescending(time).ThenByDescending(id).Take(pageSize + 1).Select(select).ToListAsync(ct);
        var hasNext = rows.Count > pageSize;
        if (hasNext) rows.RemoveAt(rows.Count - 1);
        string? next = null;
        if (hasNext && rows.Count > 0) { var (t0, i0) = keyOf(rows[^1]); next = Cursor.Encode(t0, i0); }
        return new ListPage<TRow>(rows, 1, pageSize, rows.Count, true, 0, hasNext, cursor is not null, next);
    }

    private static Expression ReplaceParameter(Expression body, ParameterExpression from, ParameterExpression to) => new ParameterReplacer(from, to).Visit(body);

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>The keyset cursor: time and id of the last row, as one opaque text.</summary>
    public static class Cursor
    {
        /// <summary>Encodes a cursor.</summary>
        public static string Encode(DateTimeOffset time, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{time.UtcTicks}|{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Decodes a cursor. A bad cursor gives false and the first page is shown.</summary>
        public static bool TryDecode(string? cursor, out (DateTimeOffset Time, Guid Id) value)
        {
            value = default;
            if (string.IsNullOrEmpty(cursor) || cursor.Length > 100) return false;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/'); padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');
                if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParseExact(parts[1], "N", out var id)) return false;
                value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
            catch (FormatException) { return false; }
        }
    }
}

/// <summary>Writes a list as CSV: UTF-8 with BOM, a formula-injection guard on every cell, no secrets (rows hold none).</summary>
public static class CsvExport
{
    /// <summary>The default row limit of an export.</summary>
    public const int DefaultRowLimit = 50_000;

    /// <summary>Escapes one cell. A cell that starts with <c>= + - @</c> (or a tab or carriage return) gets a leading quote, so a spreadsheet shows text, not a formula.</summary>
    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => "",
            DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    /// <summary>Streams the rows. The properties of <typeparamref name="TRow"/> are the columns.</summary>
    public static async Task WriteAsync<TRow>(Stream output, IAsyncEnumerable<TRow> rows, int rowLimit, CancellationToken ct = default)
    {
        var props = typeof(TRow).GetProperties().Where(p => p.CanRead).ToArray();
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync(string.Join(',', props.Select(p => Cell(p.Name))));
        var n = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            if (++n > rowLimit) break;
            await writer.WriteLineAsync(string.Join(',', props.Select(p => Cell(p.GetValue(row)))));
        }
    }
}
