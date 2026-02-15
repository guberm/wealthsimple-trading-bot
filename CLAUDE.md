# CLAUDE.md — Wealthsimple Trading Bot

## Project Overview

Automated portfolio rebalancing bot for Wealthsimple Trade, implemented in **two versions**: Python and .NET 8 (C#). The bot auto-picks top TSX securities, divides the portfolio into equal-weight buckets, and rebalances every ~2 hours during market hours.

**Important**: Wealthsimple has NO official public API. All API access is unofficial and violates their ToS. Dry-run mode is the default and must remain so.

## Project Structure

```
wealthsimple_trading_bot/
├── python_bot/              # Python implementation
│   ├── config/
│   │   ├── settings.yaml    # Main configuration
│   │   └── stock_universe.yaml  # 30 curated TSX securities
│   ├── src/
│   │   ├── main.py          # Entry point + orchestrator
│   │   ├── config.py        # Config loader (YAML + .env)
│   │   ├── models.py        # Pydantic data models
│   │   ├── auth.py          # WS login, 2FA (pyotp), token refresh
│   │   ├── ws_client.py     # Base HTTP client for WS API
│   │   ├── account_service.py
│   │   ├── market_data_service.py
│   │   ├── order_service.py
│   │   ├── stock_picker.py  # Yahoo Finance scoring algorithm
│   │   ├── rebalancer.py    # Equal-weight target calculation
│   │   ├── executor.py      # Live execution + rate limiter
│   │   ├── dry_run.py       # Simulated execution
│   │   └── scheduler.py     # APScheduler (4 runs/day)
│   └── requirements.txt
│
├── dotnet_bot/              # .NET 8 implementation
│   ├── WealthsimpleTradingBot.sln
│   └── src/WealthsimpleTradingBot/
│       ├── Program.cs       # Host builder, DI, Quartz.NET
│       ├── appsettings.json # All config + Serilog
│       ├── Configuration/BotSettings.cs  # Strongly-typed settings
│       ├── Models/          # Account, Position, Order, Security, Portfolio
│       ├── Auth/            # WealthsimpleAuthenticator, TokenManager
│       ├── Api/             # WsClient, AccountService, MarketDataService, OrderService
│       ├── Strategy/        # StockPicker, PortfolioRebalancer
│       ├── Execution/       # OrderExecutor, RateLimiter, DryRunSimulator
│       ├── Services/        # TradingBotService, YahooFinanceService
│       └── Scheduling/      # TradingJob (Quartz.NET)
```

## Build & Run

### Python
```bash
cd python_bot
pip install -r requirements.txt
cp .env.example .env  # fill WS_EMAIL, WS_PASSWORD, WS_OTP_SECRET
python src/main.py --run-once        # single dry-run
python src/main.py                   # scheduled mode
python src/main.py --run-once --live # live trading (requires config gates)
```

### .NET
```bash
cd dotnet_bot
dotnet restore
dotnet build src/WealthsimpleTradingBot
dotnet run --project src/WealthsimpleTradingBot -- --run-once        # single dry-run
dotnet run --project src/WealthsimpleTradingBot                      # scheduled mode
dotnet run --project src/WealthsimpleTradingBot -- --run-once --live # live trading
```

## Key Architecture Decisions

- **Direct HTTP client** — no wrapper libraries. Python uses `requests`, .NET uses `HttpClient`.
- **Yahoo Finance for screening** — `yfinance` (Python) and Yahoo v8 chart API (C#). No API key needed.
- **Curated 30-security universe** — 15 ETFs + 15 blue-chip TSX stocks. Picker selects top 7.
- **Equal-weight rebalancing** — 1/N per bucket, rebalance when drift > 5%.
- **Sells before buys** — frees cash first.
- **Limit orders** — prevents slippage.
- **Rate limiter** — sliding window, 6 trades/hr (WS enforces 7/hr).

## Wealthsimple API Reference

Base URL: `https://trade-service.wealthsimple.com`

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/auth/login` | POST | Login (email, password, otp) |
| `/auth/refresh` | POST | Refresh token |
| `/account/list` | GET | List accounts + balances |
| `/account/positions` | GET | Current holdings |
| `/securities?query=X` | GET | Search securities |
| `/securities/{id}` | GET | Security details + quote |
| `/orders` | POST | Place order |
| `/orders` | GET | List orders |
| `/orders/{id}` | DELETE | Cancel order |

Auth tokens returned in `X-Access-Token` / `X-Refresh-Token` response headers. Tokens expire ~15 min.

## Safety Rules

1. **Dry-run is always the default.** Never change this.
2. Live trading requires THREE gates: config `mode=live` + `live_mode_confirmation=true` + CLI `--live` flag + runtime `YES` prompt.
3. Credentials must NEVER be in config files — always use env vars (`.env` / `WS__*`).
4. Max 6 trades/hour, 20 trades/day, $5000 max per trade.
5. The `.env` file is gitignored and must stay that way.

## Coding Conventions

### Python
- Pydantic v2 models for all data structures
- Type hints on all functions
- `logging` module with rotating file handler
- Config via YAML + dotenv

### .NET
- C# records for immutable models
- Interfaces for all services (DI-friendly)
- `IOptions<T>` pattern for configuration
- Serilog for structured logging
- Quartz.NET for cron scheduling

## Common Tasks

- **Add a new security to the universe**: Edit `python_bot/config/stock_universe.yaml` and `dotnet_bot/src/WealthsimpleTradingBot/appsettings.json` `StockUniverse` section
- **Change schedule**: Edit `run_times` in `settings.yaml` or `CronExpressions` in `appsettings.json`
- **Adjust scoring weights**: Modify `_rank_and_score()` in `stock_picker.py` or `RankAndScore()` in `StockPicker.cs`
- **Change number of buckets**: Set `num_picks` (5–10) in config
- **Change drift threshold**: Set `drift_threshold_pct` in config (default 5%)
