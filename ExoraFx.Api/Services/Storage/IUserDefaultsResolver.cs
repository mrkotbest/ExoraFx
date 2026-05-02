namespace ExoraFx.Api.Services.Storage;

public interface IUserDefaultsResolver
{
    decimal Margin(long? userId);

    string Bank(long? userId);

    string Currency(long? userId);

    decimal Amount(long? userId);

    string? Language(long? userId);

    bool HistoryEnabled(long? userId);

    bool ShowBestHint(long? userId);
}
