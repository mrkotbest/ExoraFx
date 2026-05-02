using ExoraFx.Api.Models;

namespace ExoraFx.Api.Services.Exchange;

public interface IConversionService
{
    ConvertResult? Convert(string fromRaw, string toRaw, decimal amount, string? bankRaw = null, decimal? marginOverride = null);

    ConvertResult? ConvertReverse(string foreignRaw, decimal targetUah, string? bankRaw = null, decimal? marginOverride = null);
}
