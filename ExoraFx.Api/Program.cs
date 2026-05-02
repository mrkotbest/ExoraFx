using ExoraFx.Api.Configuration;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Middleware;
using ExoraFx.Api.Persistence;
using ExoraFx.Api.Providers;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);

var tokenLength = (builder.Configuration["Telegram:BotToken"] ?? "").Length;
Console.WriteLine($"[startup] Telegram:BotToken length = {tokenLength} (appsettings / user-secrets / env)");

builder.Services.Configure<ExchangeSettings>(builder.Configuration.GetSection(ExchangeSettings.SectionName));
builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection(TelegramSettings.SectionName));
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(ApiSettings.SectionName));
builder.Services.Configure<RateLimitingSettings>(builder.Configuration.GetSection(RateLimitingSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection(StorageSettings.SectionName));
builder.Services.Configure<LoggingSettings>(builder.Configuration.GetSection(LoggingSettings.SectionName));

builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsSettings.PolicyName, policy =>
    {
        var origins = corsSettings.AllowedOrigins;
        if (origins.Contains("*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins([.. origins]);
        }

        policy.AllowAnyHeader().WithMethods("GET", "HEAD", "OPTIONS");
    });
});

var rateLimitSettings = builder.Configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>() ?? new RateLimitingSettings();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitSettings.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitSettings.WindowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });
});

builder.Services.AddHttpClient("Monobank", c => { c.BaseAddress = new Uri("https://api.monobank.ua/"); c.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddHttpClient("PrivatBank", c => { c.BaseAddress = new Uri("https://api.privatbank.ua/"); c.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddHttpClient("Nbu", c => { c.BaseAddress = new Uri("https://bank.gov.ua/"); c.Timeout = TimeSpan.FromSeconds(10); });

builder.Services.AddSingleton<IRateProvider, MonobankRateProvider>();
builder.Services.AddSingleton<IRateProvider, PrivatBankProvider>();
builder.Services.AddSingleton<IRateProvider, NbuRateProvider>();

builder.Services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
builder.Services.AddSingleton<SchemaInitializer>();

builder.Services.AddSingleton<IExchangeRateService, ExchangeRateService>();
builder.Services.AddSingleton<IConversionService, ConversionService>();
builder.Services.AddSingleton<ITelegramBotClientProvider, TelegramBotClientProvider>();
builder.Services.AddSingleton<IBotLogService, BotLogService>();
builder.Services.AddSingleton<IBotLocalizer, BotLocalizer>();
builder.Services.AddSingleton<IUserSettingsStore, SqliteUserSettingsStore>();
builder.Services.AddSingleton<IUserDefaultsResolver, UserDefaultsResolver>();
builder.Services.AddSingleton<IConversionHistoryStore, SqliteConversionHistoryStore>();
builder.Services.AddSingleton<BotInputParser>();
builder.Services.AddSingleton<BotMessageRenderer>();
builder.Services.AddSingleton<BotKeyboards>();
builder.Services.AddSingleton<PromptStateStore>();

builder.Services.AddHostedService<RateRefreshBackgroundService>();
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

app.Services.GetRequiredService<SchemaInitializer>().Initialize();

app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHttpsRedirection();
    app.UseHsts();

    var apiKey = builder.Configuration[$"{ApiSettings.SectionName}:Key"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            var protectedPath = path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase);
            if (protectedPath && ctx.Request.Headers["X-API-Key"].ToString() != apiKey)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("API key required");
                return;
            }

            await next();
        });
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
}

app.UseCors(CorsSettings.PolicyName);
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/ping", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));

app.Run();
