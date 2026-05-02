using ExoraFx.Api.Configuration;
using ExoraFx.Api.Models;
using ExoraFx.Api.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Tests.Services;

public class ConversionHistoryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public ConversionHistoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = _dbPath }));
        new SchemaInitializer(_factory, NullLogger<SchemaInitializer>.Instance).Initialize();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + "-shm"))
            File.Delete(_dbPath + "-shm");
        if (File.Exists(_dbPath + "-wal"))
            File.Delete(_dbPath + "-wal");
    }

    private SqliteConversionHistoryStore CreateStore() => new(_factory);

    private static ConvertResult Forward(string from, decimal fromAmount, decimal toAmount, decimal profitUah, decimal? profitEur = null, string bank = "monobank") =>
        new()
        {
            From = from.ToUpperInvariant(),
            To = "UAH",
            FromAmount = fromAmount,
            ToAmount = toAmount,
            OfficialRate = 41m,
            EffectiveRate = 38m,
            MarginPercent = 7.5m,
            ProfitUah = profitUah,
            ProfitEur = profitEur,
            Bank = bank,
            IsStale = false,
            CalculatedAt = DateTime.UtcNow.ToString("o"),
            RateAgeSec = 0,
        };

    private static ConvertResult Reverse(string foreignCur, decimal uahAmount, decimal foreignAmount, decimal profitUah, decimal? profitEur = null, string bank = "monobank") =>
        new()
        {
            From = "UAH",
            To = foreignCur.ToUpperInvariant(),
            FromAmount = uahAmount,
            ToAmount = foreignAmount,
            OfficialRate = 41m,
            EffectiveRate = 38m,
            MarginPercent = 7.5m,
            ProfitUah = profitUah,
            ProfitEur = profitEur,
            Bank = bank,
            IsStale = false,
            CalculatedAt = DateTime.UtcNow.ToString("o"),
            RateAgeSec = 0,
        };

    [Fact]
    public void Append_NewEntry_StoredAsDraftWithReturnedId()
    {
        var store = CreateStore();
        var id = store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.True(id > 0);
        var page = store.GetPage(1L, 0, 10);
        Assert.Single(page);
        Assert.Equal(id, page[0].Id);
        Assert.Equal(HistoryState.Draft, page[0].State);
    }

    [Fact]
    public void ToggleState_FromDraftToDoneAndBack()
    {
        var store = CreateStore();
        var id = store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.Equal(HistoryState.Done, store.ToggleState(1L, id));
        Assert.Equal(HistoryState.Done, store.GetPage(1L, 0, 10)[0].State);

        Assert.Equal(HistoryState.Draft, store.ToggleState(1L, id));
        Assert.Equal(HistoryState.Draft, store.GetPage(1L, 0, 10)[0].State);
    }

    [Fact]
    public void ToggleState_OtherUsersEntry_ReturnsNull()
    {
        var store = CreateStore();
        var id = store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.Null(store.ToggleState(2L, id));
        Assert.Equal(HistoryState.Draft, store.GetPage(1L, 0, 10)[0].State);
    }

    [Fact]
    public void GetStats_AllDrafts_StillAggregates()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));
        store.Append(1L, "@op", "user", Forward("usd", 50m, 2000m, 150m));

        var stats = store.GetStats(1L);

        Assert.Equal(2, stats.TotalCount);
        Assert.Equal(0, stats.DoneCount);
        Assert.Equal(2, stats.DraftCount);
        Assert.Equal(100m, stats.ReceivedByCurrency["eur"]);
        Assert.Equal(50m, stats.ReceivedByCurrency["usd"]);
        Assert.Equal(450m, stats.ProfitUah);
        Assert.Equal(6000m, stats.PaidUah);
    }

    [Fact]
    public void GetStats_AggregatesAllRows_RegardlessOfState()
    {
        var store = CreateStore();
        var idEur = store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m, profitEur: 7.5m));
        var idUsd = store.Append(1L, "@op", "user", Forward("usd", 50m, 2000m, 150m));
        store.Append(1L, "@op", "user", Forward("eur", 999m, 39960m, 3000m));

        store.ToggleState(1L, idEur);
        store.ToggleState(1L, idUsd);

        var stats = store.GetStats(1L);

        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(2, stats.DoneCount);
        Assert.Equal(1, stats.DraftCount);
        Assert.Equal(1099m, stats.ReceivedByCurrency["eur"]);
        Assert.Equal(50m, stats.ReceivedByCurrency["usd"]);
        Assert.Equal(45960m, stats.PaidUah);
        Assert.Equal(3450m, stats.ProfitUah);
        Assert.Equal(7.5m, stats.ProfitEur);
        Assert.Equal("monobank", stats.TopBank);
        Assert.Equal(3, stats.TopBankCount);
    }

    [Fact]
    public void GetStats_ReverseTrade_ForeignTrackedFromToSide()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Reverse("eur", 5000m, 121.5m, 350m));

        var stats = store.GetStats(1L);

        Assert.Equal(121.5m, stats.ReceivedByCurrency["eur"]);
        Assert.Equal(5000m, stats.PaidUah);
    }

    [Fact]
    public void GetStats_TopBank_PicksMostFrequent()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m, bank: "privatbank"));
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m, bank: "monobank"));
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m, bank: "monobank"));

        var stats = store.GetStats(1L);

        Assert.Equal("monobank", stats.TopBank);
        Assert.Equal(2, stats.TopBankCount);
    }

    [Fact]
    public void GetPage_OldRowsMigrated_DefaultsToDraft()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        var page = store.GetPage(1L, 0, 10);

        Assert.Equal(HistoryState.Draft, page[0].State);
    }

    [Fact]
    public void GetById_OnlyReturnsRowsForOwner()
    {
        var store = CreateStore();
        var id = store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.NotNull(store.GetById(1L, id));
        Assert.Null(store.GetById(2L, id));
    }

    [Fact]
    public void GetStats_FuturePeriodFilter_ReturnsZero()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        var stats = store.GetStats(1L, DateTime.UtcNow.AddDays(1), null);
        Assert.Equal(0, stats.TotalCount);
    }

    [Fact]
    public void GetStats_LongUserId_RoundTripsCorrectly()
    {
        var store = CreateStore();
        store.Append(long.MaxValue, "@maxuser", "admin", Forward("eur", 100m, 4000m, 300m));

        var stats = store.GetStats(long.MaxValue);
        Assert.Equal(1, stats.TotalCount);
    }

    [Fact]
    public void GetStats_PerUserIsolation_NoLeakBetweenUsers()
    {
        var store = CreateStore();
        store.Append(1L, "@a", "user", Forward("eur", 100m, 4000m, 300m));
        store.Append(2L, "@b", "user", Forward("usd", 200m, 8000m, 500m));

        var s1 = store.GetStats(1L);
        var s2 = store.GetStats(2L);

        Assert.Equal(100m, s1.ReceivedByCurrency["eur"]);
        Assert.False(s1.ReceivedByCurrency.ContainsKey("usd"));
        Assert.Equal(200m, s2.ReceivedByCurrency["usd"]);
        Assert.False(s2.ReceivedByCurrency.ContainsKey("eur"));
    }

    [Fact]
    public void GetStats_PeriodBoundaryInclusiveOnSince_ExclusiveOnUntil()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        var insideOpen = store.GetStats(1L, sinceUtc: now.AddSeconds(-5), untilUtc: null);
        Assert.Equal(1, insideOpen.TotalCount);

        var afterEnd = store.GetStats(1L, sinceUtc: now.AddSeconds(-5), untilUtc: now.AddSeconds(-1));
        Assert.Equal(0, afterEnd.TotalCount);
    }

    [Fact]
    public void Clear_DoesNotAffectOtherUsers()
    {
        var store = CreateStore();
        store.Append(1L, "@a", "user", Forward("eur", 100m, 4000m, 300m));
        store.Append(2L, "@b", "user", Forward("eur", 50m, 2000m, 150m));

        store.Clear(1L);

        Assert.Equal(0, store.Count(1L));
        Assert.Equal(1, store.Count(2L));
    }

    [Fact]
    public void GetPage_OffsetBeyondEnd_ReturnsEmpty()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.Empty(store.GetPage(1L, offset: 10, take: 5));
    }

    [Fact]
    public void GetPage_NegativeOffset_TreatedAsZero()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        var page = store.GetPage(1L, offset: -3, take: 5);
        Assert.Single(page);
    }

    [Fact]
    public void GetPage_TakeZero_ReturnsEmpty()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));

        Assert.Empty(store.GetPage(1L, offset: 0, take: 0));
    }

    [Fact]
    public void Concurrent_AppendsFromDifferentUsers_AllPersisted()
    {
        var store = CreateStore();

        Parallel.For(0, 50, i =>
        {
            store.Append(userId: i + 1, "@op", "user", Forward("eur", 10m + i, 400m + i, 30m));
        });

        for (var i = 0; i < 50; i++)
            Assert.Equal(1, store.Count(i + 1));
    }

    [Fact]
    public void GetStats_TopCurrencyAndMaxTrade()
    {
        var store = CreateStore();
        store.Append(1L, "@op", "user", Forward("eur", 100m, 4000m, 300m));
        store.Append(1L, "@op", "user", Forward("usd", 250m, 10000m, 800m));
        store.Append(1L, "@op", "user", Forward("eur", 50m, 2000m, 150m));

        var stats = store.GetStats(1L);

        Assert.Equal("usd", stats.TopCurrency);
        Assert.Equal(10000m, stats.MaxTradeUah);
        Assert.Equal("usd", stats.MaxTradeFromCurrency);
        Assert.Equal(250m, stats.MaxTradeFromAmount);
    }
}
