# ExoraFx

> A lightweight currency-exchange service for Ukraine — REST API and a Telegram bot in a single ASP.NET Core process. Live rates from **Monobank**, **PrivatBank**, and the **National Bank of Ukraine**, with configurable margin and per-user preferences.

[![CI](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml/badge.svg)](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![Telegram Bot API](https://img.shields.io/badge/Telegram%20Bot-22.x-26A5E4)](https://core.telegram.org/bots/api)

**Live bot:** [@exora_fx_bot](https://t.me/exora_fx_bot)

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

## Telegram bot

Try it: **[@exora_fx_bot](https://t.me/exora_fx_bot)**

### Calculation syntax

The bot reads short messages, no fixed grammar. The first token is the amount, the rest can come in any order — currency, bank, margin override.

| You type | Result |
| -------- | ------ |
| `100 eur` | 100 EUR → UAH with your default bank and margin |
| `100 eur mono` | …pinned to Monobank (`mono` / `privat` / `nbu`) |
| `100 eur 8%` | …with margin override on this calculation only |
| `100 eur mono 8%` | bank + margin in one go |
| `100 eur usd` | cross-currency: EUR → USD via UAH |
| `eur` | calculation on your default amount from `/settings` |
| `5000 uah` | reverse: how much foreign you'd give for 5000 ₴ |
| `5к eur` / `1.5K usd` | `к` and `K` mean thousands |

The amount also accepts plain words: `100 евро`, `200 долларов`, `5000 грн`, `300 злотых` (and Ukrainian / English variants).

### Commands

| Command | What it does |
| ------- | ------------ |
| `/help` | Cheat sheet of inputs and commands |
| `/rates` | Live rate table for every supported currency, with the best bank highlighted (🏆) |
| `/table` | Pre-built grid of common amounts (5, 10, 20, 50, …, 5000) for one currency and bank |
| `/scenario` | Margin grid (1% — 15%) for your default amount and currency — handy when planning a trade |
| `/history` | Recent conversions, paginated. Each entry can be marked ⏳ draft → 🟢 done. |
| `/stats` | Volume, profit, top bank, top currency, biggest trade — over today / 7 days / 30 days / all time |
| `/settings` | Inline menu to change language, default bank/currency/amount, personal margin, history toggle, and the «🏆 best bank» hint |
| `/whoami` | Your Telegram id and role |

### Inline mode

Type `@exora_fx_bot 100 eur` in any chat (group, DM with a friend) — the bot suggests the calculation as an inline result, ready to send. Useful for sharing a rate without forwarding a screenshot.

### Per-user state

Language, default bank, default currency, default amount, personal margin, history-recording toggle, and the «better bank» hint all live in SQLite per Telegram user id. Set them once in `/settings`, they survive restarts.

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
  "from": "EUR", "fromAmount": 100,
  "to": "UAH",  "toAmount": 4912.45,
  "officialRate": 51.71, "effectiveRate": 49.1245,
  "marginPercent": 5, "profitUah": 258.55,
  "bank": "monobank", "isStale": false
}
```

Full schema and try-it-out UI — at `/scalar/v1`.

## Architecture

```
   Telegram  ─▶  TelegramBotService              HTTP  ─▶  ExchangeController
                 (long-polling, parser)                    (REST + Scalar UI)
                         │                                          │
                         └──────────────────┬───────────────────────┘
                                            ▼
                                   ConversionService
                                            │
                                            ▼
                                  ExchangeRateService   ◀──  RateRefreshBackground
                                  (IMemoryCache)             (every 240s, parallel)
                                            │
                            ┌───────────────┼───────────────┐
                            ▼               ▼               ▼
                         Monobank        PrivatBank        NBU
                       api.monobank.ua  api.privatbank.ua  bank.gov.ua

           SQLite  ─  user settings · conversion history · bot logs
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

Author: [@mrkotbest](https://github.com/mrkotbest) · Live bot: [@exora_fx_bot](https://t.me/exora_fx_bot)
