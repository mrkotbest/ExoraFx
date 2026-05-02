using ExoraFx.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ExoraFx.Api.Controllers;

[ApiController]
[Route("admin")]
[RequireApiKey]
public sealed class AdminController(IExchangeRateService rates, ILogger<AdminController> logger) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        logger.LogInformation("Manual refresh triggered");
        await rates.RefreshAllAsync(ct);
        return Ok(rates.GetHealth());
    }
}
