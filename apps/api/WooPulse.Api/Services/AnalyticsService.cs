using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;

namespace WooPulse.Api.Services;

/// <summary>
/// What: turns synced orders into dashboard numbers (DEVLOG #013).
/// All heavy lifting is done by Postgres (GROUP BY / SUM / COUNT); C# only fills gaps and
/// assembles the response, so even 3 years of orders answer in milliseconds.
/// Permission: any member of the store (owner decision #011).
/// </summary>
public class AnalyticsService
{
    /// <summary>
    /// Which order statuses count as revenue (owner decision #011). This is the ONE place to
    /// change it. Same default as WooCommerce's own Analytics.
    /// </summary>
    public static readonly string[] RevenueStatuses = { "processing", "completed" };

    public const int MaxRangeDays = 1100;   // a bit over 3 years (the synced history)
    public const int DailyMaxDays = 92;     // longer ranges are shown per month
    private const int TopProductsCount = 10;

    private readonly AppDbContext _db;

    public AnalyticsService(AppDbContext db) => _db = db;

    public async Task<(AnalyticsDto? dto, IResult? error)> GetAsync(string sub, int storeId, DateOnly? from, DateOnly? to)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());

        // Defaults: the last 30 days, including today.
        // (Server's local date. This app runs on the same machine/country as the shop.)
        var end = to ?? DateOnly.FromDateTime(DateTime.Now);
        var start = from ?? end.AddDays(-29);

        // DayNumber = days since 0001-01-01, so subtracting gives the length of the range.
        var days = end.DayNumber - start.DayNumber + 1;
        if (days < 1)
            return (null, Results.Problem("Invalid range", "\"from\" must be on or before \"to\".", statusCode: 400));
        if (days > MaxRangeDays)
            return (null, Results.Problem("Range too long", $"Choose at most {MaxRangeDays} days.", statusCode: 400));

        // The previous period has the same length and ends the day before "start".
        var prevEnd = start.AddDays(-1);
        var prevStart = prevEnd.AddDays(-(days - 1));

        var (current, statuses) = await KpisAsync(storeId, start, end);
        var (previous, _) = await KpisAsync(storeId, prevStart, prevEnd);   // "_" = discard: we don't need its statuses

        var granularity = days <= DailyMaxDays ? "day" : "month";
        var series = await SeriesAsync(storeId, start, end, granularity);
        var top = await TopProductsAsync(storeId, start, end);

        var currency = await _db.Orders.Where(o => o.StoreId == storeId)
            .Select(o => o.Currency).FirstOrDefaultAsync() ?? "";

        var lastSync = await _db.StoreConnections.Where(c => c.StoreId == storeId)
            .Select(c => c.LastSyncCompletedAtUtc).FirstOrDefaultAsync();

        return (new AnalyticsDto(start, end, granularity, currency, current, previous,
                                 series, top, statuses, lastSync), null);
    }

    /// <summary>Orders of one store in a date range (inclusive), a reusable query building block.</summary>
    private IQueryable<WooOrder> OrdersIn(int storeId, DateOnly from, DateOnly to)
        => _db.Orders.Where(o => o.StoreId == storeId && o.CreatedLocalDate >= from && o.CreatedLocalDate <= to);
    // IQueryable = a query that hasn't run yet. Callers add .Where/.GroupBy and EF builds ONE SQL statement.

    private async Task<(KpiDto Kpi, List<StatusCountDto> Statuses)> KpisAsync(int storeId, DateOnly from, DateOnly to)
    {
        // One GROUP BY "Status" query gives totals for every status at once:
        //   SELECT "Status", COUNT(*), SUM("Total"), SUM("TotalTax"), SUM("ShippingTotal")
        //   FROM "Orders" WHERE ... GROUP BY "Status"
        var byStatus = await OrdersIn(storeId, from, to)
            .GroupBy(o => o.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Total = g.Sum(o => o.Total),
                Tax = g.Sum(o => o.TotalTax),
                Shipping = g.Sum(o => o.ShippingTotal),
            })
            .ToListAsync();

        // The rest is plain C# over a handful of rows (one per status).
        var revenueRows = byStatus.Where(s => RevenueStatuses.Contains(s.Status)).ToList();
        var revenue = revenueRows.Sum(s => s.Total);
        var orders = revenueRows.Sum(s => s.Count);
        var net = revenue - revenueRows.Sum(s => s.Tax) - revenueRows.Sum(s => s.Shipping);
        var cancelled = byStatus.FirstOrDefault(s => s.Status == "cancelled");

        var itemsSold = await _db.OrderItems
            .Where(i => i.Order.StoreId == storeId
                     && i.Order.CreatedLocalDate >= from && i.Order.CreatedLocalDate <= to
                     && RevenueStatuses.Contains(i.Order.Status))
            .SumAsync(i => (int?)i.Quantity) ?? 0;
        //           ^ (int?) cast: SUM over zero rows is NULL in SQL. Summing a nullable lets
        //             EF return null instead of throwing, and "?? 0" turns it into 0.

        var kpi = new KpiDto(
            Revenue: revenue,
            NetRevenue: net,
            Orders: orders,
            AverageOrderValue: orders == 0 ? 0 : Math.Round(revenue / orders, 2),
            ItemsSold: itemsSold,
            CancelledOrders: cancelled?.Count ?? 0,
            CancelledValue: cancelled?.Total ?? 0);

        var statuses = byStatus
            .OrderByDescending(s => s.Count)
            .Select(s => new StatusCountDto(s.Status, s.Count, s.Total))
            .ToList();

        return (kpi, statuses);
    }

    /// <summary>
    /// Revenue + order count per day (or month), with ZERO-FILLED gaps: a day without orders
    /// must still appear as 0, or the line chart would draw a misleading straight line across it.
    /// </summary>
    private async Task<List<SeriesPointDto>> SeriesAsync(int storeId, DateOnly from, DateOnly to, string granularity)
    {
        // SQL: one row per day that HAS orders.
        var perDay = await OrdersIn(storeId, from, to)
            .Where(o => RevenueStatuses.Contains(o.Status))
            .GroupBy(o => o.CreatedLocalDate)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(o => o.Total), Orders = g.Count() })
            .ToDictionaryAsync(x => x.Day);

        var points = new List<SeriesPointDto>();

        if (granularity == "day")
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                points.Add(perDay.TryGetValue(d, out var p)
                    ? new SeriesPointDto(d, p.Revenue, p.Orders)
                    : new SeriesPointDto(d, 0, 0));
            }
            return points;
        }

        // Month buckets: Period = the 1st of each month. The first/last month may be partial.
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var monthEnd = m.AddMonths(1).AddDays(-1);
            var inMonth = perDay.Values.Where(p => p.Day >= m && p.Day <= monthEnd).ToList();
            points.Add(new SeriesPointDto(m, inMonth.Sum(p => p.Revenue), inMonth.Sum(p => p.Orders)));
        }
        return points;
    }

    /// <summary>
    /// Best sellers by revenue. Grouped by WooCommerce product id, so all variations of a
    /// product (sizes, colors) count together. Max(Name) picks one name if it changed over time.
    /// </summary>
    private async Task<List<TopProductDto>> TopProductsAsync(int storeId, DateOnly from, DateOnly to)
    {
        // BUG FIX (DEVLOG #014): project into an ANONYMOUS object, not straight into the
        // TopProductDto record. EF can "see through" anonymous objects (it knows Revenue = SUM(Total)),
        // so OrderBy/Take can still be translated to SQL. After a record CONSTRUCTOR, EF can't map
        // p.Revenue back to SQL, and the query fails at runtime with "could not be translated".
        var rows = await _db.OrderItems
            .Where(i => i.Order.StoreId == storeId
                     && i.Order.CreatedLocalDate >= from && i.Order.CreatedLocalDate <= to
                     && RevenueStatuses.Contains(i.Order.Status))
            .GroupBy(i => i.WooProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Name = g.Max(i => i.Name),
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.Total),
            })
            .OrderByDescending(p => p.Revenue)   // SQL: ORDER BY SUM("Total") DESC
            .Take(TopProductsCount)               // SQL: LIMIT 10
            .ToListAsync();

        // Now in memory (10 rows): build the DTOs.
        return rows.Select(r => new TopProductDto(r.ProductId, r.Name ?? "", r.Quantity, r.Revenue)).ToList();
    }
}
