using ExoraFx.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Services.Storage;

public sealed class UserDefaultsResolver(IUserSettingsStore store, IOptions<ExchangeSettings> exchangeSettings) : IUserDefaultsResolver
{
    private const string FallbackCurrency = "eur";
    private const decimal FallbackAmount = 100m;

    private readonly ExchangeSettings _exchange = exchangeSettings.Value;

    public decimal Margin(long? userId) =>
        UserSettingsLimits.ClampMargin(store.Get(userId).MarginPercent ?? _exchange.MarginPercent);

    public string Bank(long? userId) =>
        store.Get(userId).DefaultBank ?? _exchange.DefaultBank;

    public string Currency(long? userId) =>
        store.Get(userId).DefaultCurrency ?? FallbackCurrency;

    public decimal Amount(long? userId) =>
        store.Get(userId).DefaultAmount ?? FallbackAmount;

    public string? Language(long? userId) =>
        store.Get(userId).Language;

    public bool HistoryEnabled(long? userId) =>
        store.Get(userId).HistoryEnabled ?? true;

    public bool ShowBestHint(long? userId) =>
        store.Get(userId).ShowBestHint ?? true;
}
