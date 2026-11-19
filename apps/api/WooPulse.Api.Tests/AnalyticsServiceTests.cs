using WooPulse.Api.Data;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using static WooPulse.Api.Tests.Infrastructure.ResultAssert;

namespace WooPulse.Api.Tests;

/// <summary>
/// The dashboard numbers. One hand-built data set, with every total worked out by hand
/// in the comments, so a wrong formula can't hide behind a "looks plausible" chart.
/// </summary>
public sealed class AnalyticsServiceTests : IDisposable
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 10);   // 10 days → previous period is Sep 21–30

    private readonly TestDb _db = new();
    private readonly int _storeId;
    private long _nextWooId = 1;

    public AnalyticsServiceTests()
    {
        _storeId = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));

        // In range. Revenue counts "processing" + "completed" only.
        Order(new(2026, 10, 2), "completed", total: 100, tax: 10, shipping: 5,
              (1, "Pen", 2, 60), (1, "Pen - Red", 1, 25));      // two variations of product 1
        Order(new(2026, 10, 2), "processing", total: 50, tax: 5, shipping: 0, (2, "Paper", 4, 45));
        Order(new(2026, 10, 5), "cancelled", total: 30, tax: 0, shipping: 0, (3, "Easel", 1, 30));
        Order(new(2026, 10, 6), "refunded", total: 20, tax: 0, shipping: 0, (3, "Easel", 1, 20));

        // Previous period, after the range, and another tenant: must not leak into "current".
        Order(new(2026, 9, 25), "completed", total: 40, tax: 0, shipping: 0, (1, "Pen", 1, 40));
        Order(new(2026, 10, 11), "completed", total: 999, tax: 0, shipping: 0, (1, "Pen", 1, 999));
        var other = _db.SeedStore(Users.Stranger);
        Order(new(2026, 10, 3), "completed", total: 500, tax: 0, shipping: 0, other, new (long, string, int, decimal)[] { (1, "Pen", 9, 500) });
    }

    public void Dispose() => _db.Dispose();

    private AnalyticsService Service() => new(_db.NewContext());

    private void Order(DateOnly day, string status, decimal total, decimal tax, decimal shipping,
                       params (long Product, string Name, int Qty, decimal Total)[] items)
        => Order(day, status, total, tax, shipping, _storeId, items);

    private void Order(DateOnly day, string status, decimal total, decimal tax, decimal shipping, int storeId,
                       params (long Product, string Name, int Qty, decimal Total)[] items)
    {
        using var db = _db.NewContext();
        var order = new WooOrder
        {
            StoreId = storeId, WooOrderId = _nextWooId++, Status = status, Currency = "USD",
            Total = total, TotalTax = tax, ShippingTotal = shipping, CreatedLocalDate = day,
            CreatedAtUtc = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        };
        foreach (var (product, name, qty, lineTotal) in items)
            order.Items.Add(new WooOrderItem { WooProductId = product, Name = name, Quantity = qty, Total = lineTotal });
        db.Orders.Add(order);
        db.SaveChanges();
    }

    [Fact]
    public async Task Kpis_CountOnlyRevenueStatuses_InTheRange_OfThisStore()
    {
        var (dto, error) = await Service().GetAsync(Users.Member, _storeId, From, To);

        Assert.Null(error);
        var k = dto!.Current;
        Assert.Equal(150m, k.Revenue);              // 100 + 50
        Assert.Equal(130m, k.NetRevenue);           // 150 - tax (10 + 5) - shipping 5
        Assert.Equal(2, k.Orders);
        Assert.Equal(75m, k.AverageOrderValue);     // 150 / 2
        Assert.Equal(7, k.ItemsSold);               // 2 + 1 + 4 (cancelled/refunded items excluded)
        Assert.Equal((1, 30m), (k.CancelledOrders, k.CancelledValue));
        Assert.Equal("USD", dto.Currency);
    }

    [Fact]
    public async Task PreviousPeriod_IsTheSameNumberOfDays_RightBeforeTheRange()
    {
        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, From, To);

        Assert.Equal(40m, dto!.Previous.Revenue);   // only the Sep 25 order
        Assert.Equal(1, dto.Previous.Orders);
    }

    [Fact]
    public async Task Statuses_ListEveryStatusInTheRange_IncludingNonRevenueOnes()
    {
        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, From, To);

        Assert.Equal(
            new[] { "cancelled", "completed", "processing", "refunded" },
            dto!.Statuses.Select(s => s.Status).Order().ToArray());
    }

    [Fact]
    public async Task DailySeries_HasOnePointPerDay_WithZeroDaysFilledIn()
    {
        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, From, To);

        Assert.Equal("day", dto!.Granularity);
        Assert.Equal(10, dto.Series.Count);
        Assert.Equal(From, dto.Series[0].Period);
        Assert.Equal((150m, 2), (dto.Series[1].Revenue, dto.Series[1].Orders));   // Oct 2
        Assert.Equal((0m, 0), (dto.Series[4].Revenue, dto.Series[4].Orders));     // Oct 5: only a cancelled order
        Assert.Equal(150m, dto.Series.Sum(p => p.Revenue));
    }

    [Fact]
    public async Task TopProducts_GroupVariations_RankByRevenue_AndSkipNonRevenueOrders()
    {
        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, From, To);

        Assert.Equal(2, dto!.TopProducts.Count);    // product 3 only had cancelled/refunded sales
        Assert.Equal((1L, 85m, 3), (dto.TopProducts[0].ProductId, dto.TopProducts[0].Revenue, dto.TopProducts[0].Quantity));
        Assert.Equal((2L, 45m, 4), (dto.TopProducts[1].ProductId, dto.TopProducts[1].Revenue, dto.TopProducts[1].Quantity));
    }

    [Fact]
    public async Task LongRanges_AreShownPerMonth()
    {
        Order(new(2026, 3, 10), "completed", total: 70, tax: 0, shipping: 0, (1, "Pen", 1, 70));

        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, new(2026, 1, 15), new(2026, 6, 30));

        Assert.Equal("month", dto!.Granularity);
        Assert.Equal(
            new DateOnly[] { new(2026, 1, 1), new(2026, 2, 1), new(2026, 3, 1), new(2026, 4, 1), new(2026, 5, 1), new(2026, 6, 1) },
            dto.Series.Select(p => p.Period).ToArray());
        Assert.Equal(70m, dto.Series[2].Revenue);
    }

    [Fact]
    public async Task NoRange_DefaultsToTheLast30DaysIncludingToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, null, null);

        Assert.Equal((today.AddDays(-29), today), (dto!.From, dto.To));
        Assert.Equal(30, dto.Series.Count);
    }

    [Fact]
    public async Task EmptyRange_GivesZeros_NotADivisionByZero()
    {
        var (dto, _) = await Service().GetAsync(Users.Owner, _storeId, new(2020, 1, 1), new(2020, 1, 31));

        Assert.Equal((0m, 0, 0m, 0), (dto!.Current.Revenue, dto.Current.Orders, dto.Current.AverageOrderValue, dto.Current.ItemsSold));
        Assert.Empty(dto.TopProducts);
    }

    [Fact]
    public async Task InvalidRanges_AndStrangers_AreRejected()
    {
        var (_, backwards) = await Service().GetAsync(Users.Owner, _storeId, To, From);
        var (_, tooLong) = await Service().GetAsync(Users.Owner, _storeId, From, From.AddDays(AnalyticsService.MaxRangeDays));
        var (_, stranger) = await Service().GetAsync(Users.Stranger, _storeId, From, To);

        HasStatus(400, backwards);
        HasStatus(400, tooLong);    // MaxRangeDays + 1 days, counting both ends
        HasStatus(404, stranger);
    }
}
