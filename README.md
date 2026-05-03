# ExoraFx

> A lightweight currency-exchange service for Ukraine — REST API and a Telegram bot in a single ASP.NET Core process. Live rates from **Monobank**, **PrivatBank**, and the **National Bank of Ukraine**, with configurable margin and per-user preferences.

[![CI](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml/badge.svg)](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![Telegram Bot API](https://img.shields.io/badge/Telegram%20Bot-22.x-26A5E4)](https://core.telegram.org/bots/api)

**Live Telegram Bot:** [@exora_fx_bot](https://t.me/exora_fx_bot)

---

## Why ExoraFx

Looking at three bank apps to compare a EUR-to-UAH rate is a chore. ExoraFx aggregates them in one place, applies your personal margin, and answers in plain text — through a REST endpoint or a Telegram chat. Rates refresh in the background, conversions are persisted to SQLite, and the whole thing runs from a single executable.

## Features

- **Three banks at once** — Monobank, PrivatBank, NBU. Pick one or fall back to the best available.
- **Three currencies** — EUR, USD, PLN, all against UAH (forward, reverse, and cross-currency).
- **Configurable margin** — global default (7.7% by default), per-user override stored in SQLite, or per-request override on protected endpoints.
- **Telegram bot** — natural-language input, inline mode, callback keyboards, per-user language (ru / uk / en), conversion history.
- **REST API** — Scalar UI for interactive exploration, OpenAPI schema, IP-based rate limiting (60 req/min), security headers.
- **Persistent state** — user settings, conversion history, and bot logs stored in SQLite. No external database required.
- **Health endpoint** — uptime, last-error per bank, freshness of cached rates.

## Quick start

**Prerequisites:** [.NET SDK 10.0](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/mrkotbest/ExoraFx.git
cd ExoraFx

# Configure secrets (never commit these)
dotnet user-secrets --project ExoraFx.Api set "Telegram:BotToken" "<your-bot-token>"
dotnet user-secrets --project ExoraFx.Api set "Telegram:Admins:0" "<telegram-user-id>"
dotnet user-secrets --project ExoraFx.Api set "Api:Key" "<api-key-for-docs-in-production>"

# Run
dotnet run --project ExoraFx.Api
```

Open **http://localhost:5120/scalar/v1** for the interactive API reference. The Telegram bot starts long-polling automatically once a token is provided.

## REST API

| Method | Path | Description |
| ------ | ---- | ----------- |
| `GET` | `/rates?margin={pct}` | All supported currencies, every bank, with `official`, `yours` (margin-adjusted), `best`, `average`, age in seconds. |
| `GET` | `/rates/{currency}?margin={pct}` | One currency, every bank, sorted from highest to lowest official rate. |
| `GET` | `/convert?from={cur}&to={cur}&amount={n}&bank={mono\|privat\|nbu}&margin={pct}` | Conversion result with profit calculation. `bank` and `margin` are optional. |
| `GET` | `/health` | Per-bank cache freshness, last error, uptime, last refresh timestamp. |
| `GET` | `/ping` | Liveness probe. |

`margin` is a percent, e.g. `5` means 5%. CORS is read-only (`GET / HEAD / OPTIONS`). In Production `/openapi` and `/scalar/v1` require the `X-API-Key` header (configured via `Api:Key`); in Development they are open.

Example:

```bash
curl 'http://localhost:5120/convert?from=EUR&to=UAH&amount=100&bank=monobank&margin=5'
```

```json
{
  "from": "EUR",
  "fromAmount": 100,
  "to": "UAH",
  "toAmount": 4912.45,
  "officialRate": 51.71,
  "effectiveRate": 49.1245,
  "marginPercent": 5,
  "profitUah": 258.55,
  "bank": "monobank",
  "isStale": false
}
```

Full schema and try-it-out UI — at `/scalar/v1`.

## Telegram bot

Try it: **[@exora_fx_bot](https://t.me/exora_fx_bot)**

The bot reads short messages — first token is the amount, the rest can come in any order:

| You type | Result |
| -------- | ------ |
| `100 eur` | 100 EUR → UAH with your default bank and margin |
| `100 eur mono 8%` | pin Monobank, override margin to 8% |
| `5000 uah` | reverse: how much foreign you'd give for 5000 ₴ |
| `5к eur` | `к` and `K` mean thousands |

Words like `евро`, `грн`, `злотый` work too (with Ukrainian and English variants).

Main commands: `/help`, `/rates`, `/settings`, `/history`, `/stats`. The `/settings` menu changes language, default bank/currency/amount, personal margin, and toggles. Inline mode also works — type `@exora_fx_bot 100 eur` in any chat.

## Architecture

```
┌──────────────┐    ┌─────────────────────┐
│  Telegram    │───▶│  TelegramBotService │──┐
└──────────────┘    └─────────────────────┘  │
                                             │
                                             ├──▶ ConversionService ──▶ ExchangeRateService ──▶ Monobank
┌──────────────┐    ┌─────────────────────┐  │            │                       ▲              PrivatBank
│  HTTP client │───▶│  ExchangeController │──┘            ▼                       │                 NBU
└──────────────┘    └─────────────────────┘         SQLite (settings,    RateRefreshBackground
                                                     history, logs)        (every 240 s)
```

`RateRefreshBackgroundService` polls every provider in parallel every 240 seconds and stores the snapshot in `IMemoryCache`. Providers are stateless — adding a new bank means implementing `IRateProvider` and registering one line in `Program.cs`.

## Tech stack

- **ASP.NET Core 10.0** — Web API host with hosted services
- **Telegram.Bot 22.x** — long-polling client
- **Microsoft.Data.Sqlite** — embedded persistence
- **Scalar.AspNetCore** — OpenAPI reference UI
- **xUnit + Moq** — unit tests with stubbed `HttpMessageHandler`

## Project layout

```
ExoraFx/
├── ExoraFx.Api/          # Web API + Telegram bot host
├── ExoraFx.Api.Tests/    # Unit tests
└── scripts/              # Production launchers (Windows)
```

## Running in production (Windows)

The `scripts/` folder contains a self-contained launcher:

- `run-bot.bat` — waits for DNS, kills stale instances, builds in Release, runs with `ASPNETCORE_ENVIRONMENT=Production`.
- `run-bot-hidden.vbs` — same thing without a console window. Drop a shortcut into `shell:startup` for boot-time autostart.

## Tests

```bash
dotnet test
```

Provider tests use a stubbed `HttpMessageHandler` — no live network calls. SQLite tests run against temporary files, not in-memory shared cache.

## License

MIT — see [LICENSE.txt](LICENSE.txt).

Author: [@mrkotbest](https://github.com/mrkotbest) · Live Telegram Bot: [@exora_fx_bot](https://t.me/exora_fx_bot)
