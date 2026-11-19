namespace WooPulse.Api.Dtos;

// Response of GET /stores/{storeId}/analytics?from=YYYY-MM-DD&to=YYYY-MM-DD
// Everything the dashboard needs in ONE request (fewer round trips, one consistent snapshot).
public record AnalyticsDto(
    DateOnly From,
    DateOnly To,
    string Granularity,                 // "day" (ranges up to 92 days) | "month" (longer)
    string Currency,                    // e.g. "USD"
    KpiDto Current,                     // the selected range
    KpiDto Previous,                    // the same number of days right before it (for "% vs previous")
    List<SeriesPointDto> Series,        // one point per day/month, gaps filled with zeros
    List<TopProductDto> TopProducts,    // top 10 by revenue
    List<StatusCountDto> Statuses,      // every status in the range (incl. cancelled/refunded)
    DateTimeOffset? LastSyncCompletedAtUtc
);

// Key numbers. "Revenue" only counts orders with a revenue status (processing + completed).
public record KpiDto(
    decimal Revenue,            // sum of order totals (incl. tax + shipping), like WooCommerce's "Total sales"
    decimal NetRevenue,         // Revenue minus tax and shipping
    int Orders,
    decimal AverageOrderValue,  // Revenue / Orders (0 when there are no orders)
    int ItemsSold,
    int CancelledOrders,
    decimal CancelledValue
);

public record SeriesPointDto(DateOnly Period, decimal Revenue, int Orders);

public record TopProductDto(long ProductId, string Name, int Quantity, decimal Revenue);

public record StatusCountDto(string Status, int Orders, decimal Total);
