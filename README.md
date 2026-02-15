# Wealthsimple Trading Bot

Automated portfolio rebalancing bot for Wealthsimple Trade. Available in **Python** and **.NET (C#)**.

> **Disclaimer**: Wealthsimple has no official public trading API. Automated trading violates their Terms of Service and may result in account termination. Use at your own risk. This project is for **educational purposes**.

## How It Works

1. **Stock Picker** — Scores 30 curated TSX securities (15 ETFs + 15 blue-chip stocks) using Yahoo Finance data. Composite score based on momentum, Sharpe ratio, liquidity, and stability. Selects top 7.
2. **Portfolio Rebalancer** — Divides your balance equally across the selected buckets. Detects drift from target weights and generates sell/buy orders.
3. **Executor** — Sells positions that don't belong, buys to fill the buckets. Respects a 6 trades/hour rate limit (Wealthsimple enforces 7/hr server-side).
4. **Scheduler** — Runs every ~2 hours during market hours: **9:35, 11:30, 13:30, 15:30 ET**, Monday–Friday.

## Safety

- **Dry-run mode by default** — no real trades unless you explicitly enable live mode
- **Triple-gate for live trading**: config flag + confirmation flag + type `YES` at runtime prompt
- Max single trade: $5,000 CAD
- Max 20 trades/day
- Full logging to `logs/`

## Project Structure

```
wealthsimple_trading_bot/
├── python_bot/          # Python version
│   ├── config/          # settings.yaml, stock_universe.yaml
│   ├── src/             # All source modules
│   └── requirements.txt
│
└── dotnet_bot/          # .NET 8 version
    └── src/WealthsimpleTradingBot/
        ├── Api/         # Wealthsimple API client
        ├── Auth/        # Login, 2FA, token management
        ├── Strategy/    # Stock picker, rebalancer
        ├── Execution/   # Order executor, rate limiter, dry-run
        └── Scheduling/  # Quartz.NET scheduled jobs
```

## Quick Start

### Python

```bash
cd python_bot
pip install -r requirements.txt
cp .env.example .env
# Edit .env with your Wealthsimple credentials:
#   WS_EMAIL, WS_PASSWORD, WS_OTP_SECRET

# Single dry-run
python src/main.py --run-once

# Scheduled (every ~2 hours during market hours)
python src/main.py
```

### .NET

```bash
cd dotnet_bot

# Set environment variables:
#   WS__Email, WS__Password, WS__OtpSecret

# Single dry-run
dotnet run --project src/WealthsimpleTradingBot -- --run-once

# Scheduled
dotnet run --project src/WealthsimpleTradingBot
```

### Live Trading

Both versions require **all three gates** to execute real trades:

1. Set `trading.mode` to `live` (Python `settings.yaml`) or `Trading.Mode` to `Live` (.NET `appsettings.json`)
2. Set `trading.live_mode_confirmation` to `true` / `Trading.LiveModeConfirmation` to `true`
3. Pass `--live` flag and type `YES` at the confirmation prompt

```bash
# Python
python src/main.py --run-once --live

# .NET
dotnet run --project src/WealthsimpleTradingBot -- --run-once --live
```

## Configuration

### Stock Universe

Edit `python_bot/config/stock_universe.yaml` or the `StockUniverse` section in `appsettings.json`. The bot picks from this curated list of 30 TSX-listed securities:

- **15 ETFs**: XIC, XIU, ZSP, VFV, XGRO, VGRO, ZAG, XBB, VCN, ZEB, XEI, ZDV, VDY, ZRE, XRE
- **15 Stocks**: RY, TD, BNS, ENB, CNR, CP, SU, BAM, SHOP, BCE, T, MFC, CSU, ATD, WCN

### Scoring Algorithm

| Weight | Factor | Description |
|--------|--------|-------------|
| 30% | Momentum (90d) | 90-day price return |
| 25% | Sharpe Ratio | Risk-adjusted return (4% risk-free) |
| 20% | Momentum (30d) | 30-day price return |
| 15% | Liquidity | Average daily volume |
| 10% | Stability | Inverse annualized volatility |
| +10% | ETF Bonus | Applied when `prefer_etfs` is enabled |

Sector diversity enforced: max 2 securities per sector.

### Schedule

Default schedule (configurable):

| Time (ET) | Description |
|-----------|-------------|
| 9:35 AM | 5 minutes after TSX open |
| 11:30 AM | Mid-morning check |
| 1:30 PM | Afternoon check |
| 3:30 PM | 30 minutes before close |

### Key Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `num_picks` | 7 | Number of stocks/ETFs to hold (5–10) |
| `drift_threshold_pct` | 5.0% | Minimum drift before rebalancing |
| `max_single_trade_cad` | $5,000 | Cap per trade |
| `rate_limit_per_hour` | 6 | Stay under WS 7/hr limit |
| `max_daily_trades` | 20 | Daily trade cap |

## Tech Stack

### Python
- `requests` — HTTP client
- `yfinance` — Yahoo Finance market data
- `pyotp` — TOTP 2FA codes
- `apscheduler` — Cron-based scheduling
- `pydantic` — Data validation

### .NET 8
- `HttpClient` — HTTP client
- Yahoo Finance v8 chart API (direct HTTP)
- `Otp.NET` — TOTP 2FA codes
- `Quartz.NET` — Cron-based scheduling
- `Serilog` — Structured logging
- Dependency injection via `Microsoft.Extensions.Hosting`

## License

MIT
