using ExoraFx.Api.Configuration;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using ExoraFx.Api.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Tests.Services;

public class UserSettingsStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ExchangeSettings _exchange = new()
    {
        MarginPercent = 7.7m,
        DefaultBank = "monobank",
        SupportedCurrencies = ["eur", "usd", "pln"],
    };

    public UserSettingsStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.db");
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

    private SqliteUserSettingsStore CreateStore() =>
        new(_factory, new BotLocalizer(), Options.Create(_exchange));

    [Fact]
    public void Get_UnknownUser_ReturnsAllNullFields()
    {
        var store = CreateStore();

        var result = store.Get(42L);

        Assert.Null(result.Language);
        Assert.Null(result.MarginPercent);
        Assert.Null(result.DefaultBank);
        Assert.Null(result.DefaultCurrency);
        Assert.Null(result.DefaultAmount);
    }

    [Fact]
    public void TrySetMargin_ValidValue_PersistsAcrossInstances()
    {
        var store1 = CreateStore();
        Assert.True(store1.TrySetMargin(1L, 12.5m));

        var store2 = CreateStore();
        Assert.Equal(12.5m, store2.Get(1L).MarginPercent);
    }

    [Fact]
    public void TrySetMargin_OutOfRange_ReturnsFalseAndDoesNotPersist()
    {
        var store = CreateStore();

        Assert.False(store.TrySetMargin(1L, -1m));
        Assert.False(store.TrySetMargin(1L, 51m));
        Assert.Null(store.Get(1L).MarginPercent);
    }

    [Fact]
    public void TrySetLanguage_Supported_StoresNormalized()
    {
        var store = CreateStore();

        Assert.True(store.TrySetLanguage(1L, "ua"));
        Assert.Equal("uk", store.Get(1L).Language);
    }

    [Fact]
    public void TrySetLanguage_Unsupported_ReturnsFalse()
    {
        var store = CreateStore();

        Assert.False(store.TrySetLanguage(1L, "fr"));
        Assert.Null(store.Get(1L).Language);
    }

    [Fact]
    public void TrySetBank_Mono_StoresNormalized()
    {
        var store = CreateStore();

        Assert.True(store.TrySetBank(1L, "mono"));
        Assert.Equal("monobank", store.Get(1L).DefaultBank);
    }

    [Fact]
    public void TrySetBank_Average_RejectsBecauseNotInWhitelist()
    {
        var store = CreateStore();

        Assert.False(store.TrySetBank(1L, "average"));
    }

    [Fact]
    public void TrySetCurrency_KnownAndSupported_Stores()
    {
        var store = CreateStore();

        Assert.True(store.TrySetCurrency(1L, "USD"));
        Assert.Equal("usd", store.Get(1L).DefaultCurrency);
    }

    [Fact]
    public void TrySetCurrency_NotSupported_ReturnsFalse()
    {
        var store = CreateStore();

        Assert.False(store.TrySetCurrency(1L, "uah"));
        Assert.False(store.TrySetCurrency(1L, "gbp"));
    }

    [Fact]
    public void TrySetAmount_Positive_Stores()
    {
        var store = CreateStore();

        Assert.True(store.TrySetAmount(1L, 250m));
        Assert.Equal(250m, store.Get(1L).DefaultAmount);
    }

    [Fact]
    public void TrySetAmount_ZeroOrNegative_ReturnsFalse()
    {
        var store = CreateStore();

        Assert.False(store.TrySetAmount(1L, 0m));
        Assert.False(store.TrySetAmount(1L, -10m));
    }

    [Fact]
    public void PerUserIsolation_DifferentUsersDoNotShare()
    {
        var store = CreateStore();
        store.TrySetMargin(1L, 5m);
        store.TrySetMargin(2L, 12m);

        Assert.Equal(5m, store.Get(1L).MarginPercent);
        Assert.Equal(12m, store.Get(2L).MarginPercent);
    }

    [Fact]
    public void ResetField_ClearsOnlyTargetField()
    {
        var store = CreateStore();
        store.TrySetMargin(1L, 5m);
        store.TrySetBank(1L, "privat");

        store.ResetField(1L, UserSettingsField.MarginPercent);

        var result = store.Get(1L);
        Assert.Null(result.MarginPercent);
        Assert.Equal("privatbank", result.DefaultBank);
    }

    [Fact]
    public void Reset_ClearsPreferencesButKeepsIdentity()
    {
        var store = CreateStore();
        store.RecordIdentity(1L, "@operator", "admin");
        store.TrySetMargin(1L, 5m);
        store.TrySetCurrency(1L, "usd");
        store.SetHistoryEnabled(1L, false);
        store.SetShowBestHint(1L, false);

        store.Reset(1L);

        var result = store.Get(1L);
        Assert.Null(result.MarginPercent);
        Assert.Null(result.DefaultCurrency);
        Assert.Null(result.HistoryEnabled);
        Assert.Null(result.ShowBestHint);
        Assert.Equal("@operator", result.UserName);
        Assert.Equal("admin", result.UserRole);
    }

    [Fact]
    public void SetShowBestHint_PersistsValue()
    {
        var store = CreateStore();

        store.SetShowBestHint(1L, false);
        Assert.False(store.Get(1L).ShowBestHint);

        store.SetShowBestHint(1L, true);
        Assert.True(store.Get(1L).ShowBestHint);
    }

    [Fact]
    public void ShowBestHint_DefaultsToNullForNewUser()
    {
        var store = CreateStore();
        Assert.Null(store.Get(1L).ShowBestHint);
    }

    [Fact]
    public void Update_OverwritesPreviousValue()
    {
        var store = CreateStore();
        store.TrySetMargin(1L, 5m);
        store.TrySetMargin(1L, 12m);

        Assert.Equal(12m, store.Get(1L).MarginPercent);
    }

    [Fact]
    public void Get_ReturnsFreshDataAfterMultipleUpdates_OnSameInstance()
    {
        var store = CreateStore();

        Assert.True(store.TrySetMargin(1L, 6m));
        Assert.Equal(6m, store.Get(1L).MarginPercent);

        Assert.True(store.TrySetAmount(1L, 555m));
        var second = store.Get(1L);
        Assert.Equal(6m, second.MarginPercent);
        Assert.Equal(555m, second.DefaultAmount);
    }

    [Fact]
    public void RecordIdentity_PersistsNameAndRole()
    {
        var store = CreateStore();
        store.RecordIdentity(1L, "@operator", "admin");

        var settings = store.Get(1L);
        Assert.Equal("@operator", settings.UserName);
        Assert.Equal("admin", settings.UserRole);
    }

    [Fact]
    public void RecordIdentity_PreservesOtherFields()
    {
        var store = CreateStore();
        store.TrySetMargin(1L, 8m);
        store.RecordIdentity(1L, "@op", "user");

        var settings = store.Get(1L);
        Assert.Equal(8m, settings.MarginPercent);
        Assert.Equal("@op", settings.UserName);
        Assert.Equal("user", settings.UserRole);
    }

    [Fact]
    public void HistoryEnabled_DefaultsToNullForNewUser()
    {
        var store = CreateStore();
        Assert.Null(store.Get(1L).HistoryEnabled);
    }

    [Fact]
    public void SetHistoryEnabled_PersistsValue()
    {
        var store = CreateStore();

        store.SetHistoryEnabled(1L, false);
        Assert.False(store.Get(1L).HistoryEnabled);

        store.SetHistoryEnabled(1L, true);
        Assert.True(store.Get(1L).HistoryEnabled);
    }

    [Fact]
    public void SetHistoryEnabled_PreservesOtherFields()
    {
        var store = CreateStore();
        store.TrySetMargin(1L, 12m);
        store.SetHistoryEnabled(1L, false);

        var settings = store.Get(1L);
        Assert.Equal(12m, settings.MarginPercent);
        Assert.False(settings.HistoryEnabled);
    }
}
