using ExoraFx.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Security;

public sealed class AllowMarginOverrideAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ActionArguments.TryGetValue("margin", out var marginObj) || marginObj is null)
            return;

        var api = ApiKeyHelper.GetSettings(context);
        if (string.IsNullOrWhiteSpace(api.Key))
        {
            context.ActionArguments["margin"] = null;
            return;
        }

        if (!ApiKeyHelper.HeaderMatches(context, api))
            context.Result = new UnauthorizedObjectResult(new { error = $"Valid '{api.HeaderName}' required to override margin" });
    }
}

public sealed class RequireApiKeyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var api = ApiKeyHelper.GetSettings(context);
        if (string.IsNullOrWhiteSpace(api.Key))
        {
            context.Result = new ObjectResult(new { error = "Admin endpoint disabled: no API key configured" })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };

            return;
        }

        if (!ApiKeyHelper.HeaderMatches(context, api))
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing or invalid '{api.HeaderName}' header" });
    }
}

internal static class ApiKeyHelper
{
    internal static ApiSettings GetSettings(ActionExecutingContext context) =>
        context.HttpContext.RequestServices.GetRequiredService<IOptions<ApiSettings>>().Value;

    internal static bool HeaderMatches(ActionExecutingContext context, ApiSettings api) =>
        string.Equals(context.HttpContext.Request.Headers[api.HeaderName].FirstOrDefault(), api.Key, StringComparison.Ordinal);
}
