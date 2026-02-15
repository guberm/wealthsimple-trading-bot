#!/usr/bin/env python3
"""Wealthsimple Trading Bot — Main entry point and orchestrator."""

import argparse
import logging
import sys
import os
from decimal import Decimal
from logging.handlers import RotatingFileHandler
from pathlib import Path

# Add parent dir to path so we can import src modules
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.config import AppConfig
from src.auth import WealthsimpleAuthenticator, TokenManager
from src.ws_client import WealthsimpleClient
from src.account_service import AccountService
from src.market_data_service import MarketDataService
from src.order_service import OrderService
from src.stock_picker import StockPicker
from src.rebalancer import PortfolioRebalancer
from src.executor import OrderExecutor, RateLimiter
from src.dry_run import DryRunSimulator
from src.scheduler import TradingScheduler

logger = logging.getLogger("trading_bot")


class TradingBot:
    """Main orchestrator for the trading bot pipeline."""

    def __init__(self, config: AppConfig, live_override: bool = False):
        self._config = config
        self._live_override = live_override

        # Auth
        self._auth = WealthsimpleAuthenticator(
            base_url=config.base_url,
            email=config.ws_email,
            password=config.ws_password,
            otp_secret=config.ws_otp_secret,
        )
        self._token_mgr = TokenManager(self._auth)

        # API
        self._client = WealthsimpleClient(config.base_url, self._token_mgr)
        self._account_svc = AccountService(self._client)
        self._market_svc = MarketDataService(self._client)
        self._order_svc = OrderService(self._client)

        # Strategy
        self._stock_picker = StockPicker(config)
        self._rebalancer = PortfolioRebalancer(config)

        # Execution
        self._rate_limiter = RateLimiter(
            max_requests=config.safety.get("rate_limit_per_hour", 6),
            window_seconds=config.safety.get("rate_limit_window_seconds", 3600),
        )
        self._executor = OrderExecutor(self._order_svc, self._rate_limiter, config)
        self._simulator = DryRunSimulator()

    @property
    def is_live(self) -> bool:
        return self._config.is_live_mode and self._live_override

    def run_pipeline(self) -> None:
        logger.info("=" * 60)
        logger.info("TRADING PIPELINE START — mode=%s", "LIVE" if self.is_live else "DRY RUN")
        logger.info("=" * 60)

        try:
            # 1. Authenticate
            logger.info("Step 1: Authenticating with Wealthsimple...")
            self._token_mgr.ensure_authenticated()

            # 2. Get account and positions
            logger.info("Step 2: Fetching account and positions...")
            account = self._account_svc.get_account_by_type(self._config.account_type)
            if not account:
                logger.error("Account type '%s' not found!", self._config.account_type)
                return

            positions = self._account_svc.get_positions(account.id)
            cash_balance = account.buying_power.amount
            total_value = self._account_svc.get_total_portfolio_value(
                account.id, positions
            )

            logger.info(
                "Account: %s | Cash: $%.2f | Positions: %d | Total: $%.2f",
                account.account_type, cash_balance, len(positions), total_value,
            )

            # 3. Pick stocks
            logger.info("Step 3: Running stock picker...")
            picks = self._stock_picker.pick_stocks()
            if not picks:
                logger.error("Stock picker returned no picks!")
                return

            selected_symbols = [p.symbol for p in picks]
            logger.info("Selected: %s", ", ".join(selected_symbols))

            # 4. Resolve security IDs and prices from Wealthsimple
            logger.info("Step 4: Resolving security IDs and prices...")
            security_ids = self._market_svc.bulk_resolve_securities(selected_symbols)
            prices = self._market_svc.get_bulk_quotes(selected_symbols)

            # 5. Calculate targets and orders
            logger.info("Step 5: Calculating portfolio targets...")
            summary = self._rebalancer.calculate_targets(
                selected_stocks=picks,
                current_positions=positions,
                cash_balance=cash_balance,
                security_prices=prices,
                security_ids=security_ids,
            )
            sell_orders, buy_orders = self._rebalancer.generate_orders(summary)

            # 6. Execute or simulate
            if self.is_live:
                logger.info("Step 6: LIVE EXECUTION")
                if not self._confirm_live_trading(sell_orders, buy_orders):
                    logger.info("Live trading cancelled by user")
                    return
                results = self._executor.execute_orders(sell_orders, buy_orders)
                exec_summary = self._executor.get_execution_summary()
                logger.info("Execution summary: %s", exec_summary)
            else:
                logger.info("Step 6: DRY RUN SIMULATION")
                self._simulator.simulate_orders(
                    sell_orders, buy_orders, cash_balance, prices
                )
                self._simulator.print_simulation_report(summary)

            logger.info("TRADING PIPELINE COMPLETE")

        except Exception as e:
            logger.exception("Pipeline failed: %s", e)

    def _confirm_live_trading(self, sell_orders, buy_orders) -> bool:
        print("\n" + "!" * 60)
        print("  WARNING: LIVE TRADING MODE")
        print("!" * 60)
        print(f"\n  Sell orders: {len(sell_orders)}")
        for o in sell_orders:
            print(f"    SELL {o.quantity} x {o.symbol} @ ${o.limit_price}")
        print(f"\n  Buy orders: {len(buy_orders)}")
        for o in buy_orders:
            print(f"    BUY  {o.quantity} x {o.symbol} @ ${o.limit_price}")
        print(f"\n  Type 'YES' to proceed or anything else to cancel:")

        try:
            response = input("  > ").strip()
            return response == "YES"
        except (EOFError, KeyboardInterrupt):
            return False


def setup_logging(config: AppConfig) -> None:
    log_cfg = config.logging_config
    level = getattr(logging, log_cfg.get("level", "INFO").upper(), logging.INFO)
    log_file = log_cfg.get("file", "logs/trading_bot.log")
    max_bytes = log_cfg.get("max_size_mb", 10) * 1024 * 1024
    backup_count = log_cfg.get("backup_count", 5)

    # Ensure log directory exists
    Path(log_file).parent.mkdir(parents=True, exist_ok=True)

    formatter = logging.Formatter(
        "%(asctime)s | %(levelname)-8s | %(name)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    # Console handler
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setFormatter(formatter)
    console_handler.setLevel(level)

    # File handler
    file_handler = RotatingFileHandler(
        log_file, maxBytes=max_bytes, backupCount=backup_count
    )
    file_handler.setFormatter(formatter)
    file_handler.setLevel(level)

    root_logger = logging.getLogger()
    root_logger.setLevel(level)
    root_logger.addHandler(console_handler)
    root_logger.addHandler(file_handler)


def main():
    parser = argparse.ArgumentParser(description="Wealthsimple Trading Bot")
    parser.add_argument(
        "--run-once", action="store_true",
        help="Run pipeline once and exit (no scheduling)",
    )
    parser.add_argument(
        "--live", action="store_true",
        help="Enable live trading (ALSO requires config confirmation)",
    )
    parser.add_argument(
        "--config-dir", default="config",
        help="Path to config directory (default: config/)",
    )
    args = parser.parse_args()

    # Change to script directory so relative paths work
    os.chdir(Path(__file__).resolve().parent.parent)

    config = AppConfig.load(args.config_dir)
    setup_logging(config)

    logger.info("Wealthsimple Trading Bot starting...")
    logger.info("Mode: %s", "LIVE" if (config.is_live_mode and args.live) else "DRY RUN")

    bot = TradingBot(config, live_override=args.live)

    if args.run_once:
        bot.run_pipeline()
    else:
        scheduler = TradingScheduler(config, bot.run_pipeline)
        logger.info("Starting scheduler...")
        scheduler.start()


if __name__ == "__main__":
    main()
