# ExoraFx

> A lightweight currency-exchange service for Ukraine — REST API and a Telegram bot in a single ASP.NET Core process. Live rates from **Monobank**, **PrivatBank**, and the **National Bank of Ukraine**, with configurable margin and per-user preferences.

[![CI](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml/badge.svg)](https://github.com/mrkotbest/ExoraFx/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![Telegram Bot API](https://img.shields.io/badge/Telegram%20Bot-22.x-26A5E4)](https://core.telegram.org/bots/api)

---

## Why ExoraFx

Looking at three bank apps to compare a EUR-to-UAH rate is a chore. ExoraFx aggregates them in one place, applies your personal margin, and answers in plain text — through a REST endpoint or a Telegram chat. Rates refresh in the background, conversions are cached in SQLite, and the whole thing runs from a single executable.

## Features

- **Three banks at once** — Monobank, PrivatBank, NBU. Pick one or fall back to the best available.
- **Three currencies** — EUR, USD, PLN, all against UAH (forward and reverse conversions).
- **Configurable margin** — global default, per-user override, or per-request override on protected endpoints.
- **Telegram bot** — natural-language input (`100 EUR from monobank`), inline mode, callback keyboards, per-user language (en / uk / ru).
- **REST API** — Scalar UI for interactive exploration, OpenAPI schema, IP-based rate limiting, security headers.
- **Persistent state** — user settings and conversion history stored in SQLite, no external database required.
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

Open **http://localhost:5120/scalar/v1** for the interactive API reference. The Telegram bot starts polling automatically once a token is provided.

## REST API

| Method | Path | Description |
| ------ | ---- | ----------- |
| `GET` | `/rates` | All currencies from all banks. |
| `GET` | `/rates/{currency}` | One currency, all banks, with best/average. |
| `GET` | `/convert?from=EUR&to=UAH&amount=100` | Convert with optional `bank` and `margin`. |
| `GET` | `/health` | Cache freshness and last-error per bank. |
| `GET` | `/ping` | Liveness probe. |

CORS is read-only (`GET / HEAD / OPTIONS`). In production, `/openapi` and `/scalar` require an `X-API-Key` header.

## Telegram bot

Talk to the bot the way you would to a colleague:

```
100 eur to uah
500 usd from privatbank
2000 uah to pln
```

Inline keyboards let users pin a default bank, change the language, set a personal margin, or pull up recent conversion history. State is cached in SQLite, so preferences survive restarts.

## Architecture at a glance

```
┌──────────────┐    ┌─────────────────────┐
│  Telegram    │───▶│  TelegramBotService │──┐
└──────────────┘    └─────────────────────┘  │
                                             ├─▶ ConversionService ─▶ ExchangeRateService ─▶ ┌──────────┐
┌──────────────┐    ┌─────────────────────┐  │             │                                 │ Monobank │
│  HTTP client │───▶│  ExchangeController │──┘             │                                 │ Privat   │
└──────────────┘    └─────────────────────┘                ▼                                 │   NBU    │
                                                  SQLite (settings, history)                 └──────────┘
```

A `RateRefreshBackgroundService` polls every provider in parallel every 240 s and stores the snapshot in `IMemoryCache`. Providers are stateless — adding a new bank means implementing `IRateProvider` and registering it in `Program.cs`.

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

Author: [@mrkotbest](https://github.com/mrkotbest)
