namespace ExoraFx.Api.Providers;

public interface IRateProvider
{
    string Name { get; }

    Task<Dictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken = default);
}
