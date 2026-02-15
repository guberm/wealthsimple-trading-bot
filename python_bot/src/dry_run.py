import logging
from decimal import Decimal
from typing import List, Dict
from datetime import datetime
from .models import OrderRequest, OrderType, PortfolioSummary

logger = logging.getLogger(__name__)

BANNER = """
╔══════════════════════════════════════════════════════════════╗
║        *** DRY RUN — NO REAL TRADES EXECUTED ***            ║
╚══════════════════════════════════════════════════════════════╝
"""


class DryRunSimulator:
    """Simulates order execution without hitting the API."""

    def __init__(self):
        self._simulated_orders: List[dict] = []
        self._simulated_cash = Decimal("0")

    def simulate_orders(
        self,
        sell_orders: List[OrderRequest],
        buy_orders: List[OrderRequest],
        current_cash: Decimal,
        prices: Dict[str, Decimal],
    ) -> List[dict]:
        self._simulated_cash = current_cash
        self._simulated_orders = []

        logger.info(BANNER)

        # Simulate sells
        for order in sell_orders:
            price = prices.get(order.symbol, order.limit_price)
            value = price * order.quantity
            self._simulated_cash += value

            sim = {
                "action": "SELL",
                "symbol": order.symbol,
                "quantity": order.quantity,
                "price": float(price),
                "value": float(value),
                "timestamp": datetime.now().isoformat(),
            }
            self._simulated_orders.append(sim)
            logger.info(
                "[DRY RUN] Would SELL %d shares of %s @ $%.2f = $%.2f",
                order.quantity, order.symbol, price, value,
            )

        # Simulate buys
        for order in buy_orders:
            price = prices.get(order.symbol, order.limit_price)
            value = price * order.quantity

            if value > self._simulated_cash:
                logger.warning(
                    "[DRY RUN] SKIP BUY %s: need $%.2f but only $%.2f cash",
                    order.symbol, value, self._simulated_cash,
                )
                continue

            self._simulated_cash -= value

            sim = {
                "action": "BUY",
                "symbol": order.symbol,
                "quantity": order.quantity,
                "price": float(price),
                "value": float(value),
                "timestamp": datetime.now().isoformat(),
            }
            self._simulated_orders.append(sim)
            logger.info(
                "[DRY RUN] Would BUY %d shares of %s @ $%.2f = $%.2f",
                order.quantity, order.symbol, price, value,
            )

        return self._simulated_orders

    def print_simulation_report(self, summary: PortfolioSummary) -> None:
        print(BANNER)
        print(f"{'='*60}")
        print(f"  Portfolio Summary")
        print(f"{'='*60}")
        print(f"  Total Value:     ${summary.total_value:>12,.2f}")
        print(f"  Cash Balance:    ${summary.cash_balance:>12,.2f}")
        print(f"  Positions Value: ${summary.positions_value:>12,.2f}")
        print(f"  Holdings:        {summary.num_holdings:>12}")
        print()

        # Target allocations
        print(f"  {'Symbol':<12} {'Target%':>8} {'Current%':>9} {'Drift%':>7} {'Action':>6} {'Qty':>5} {'Value':>10}")
        print(f"  {'-'*12} {'-'*8} {'-'*9} {'-'*7} {'-'*6} {'-'*5} {'-'*10}")
        for t in summary.targets:
            print(
                f"  {t.symbol:<12} {float(t.target_weight)*100:>7.1f}% "
                f"{float(t.current_weight)*100:>8.1f}% "
                f"{float(t.drift_pct):>6.1f}% "
                f"{t.action:>6} {t.trade_quantity:>5} "
                f"${float(t.trade_value):>9,.2f}"
            )
        print()

        # Simulated trades
        if self._simulated_orders:
            print(f"  Simulated Trades:")
            print(f"  {'Action':<6} {'Symbol':<12} {'Qty':>5} {'Price':>10} {'Value':>12}")
            print(f"  {'-'*6} {'-'*12} {'-'*5} {'-'*10} {'-'*12}")
            total_bought = 0.0
            total_sold = 0.0
            for o in self._simulated_orders:
                print(
                    f"  {o['action']:<6} {o['symbol']:<12} {o['quantity']:>5} "
                    f"${o['price']:>9,.2f} ${o['value']:>11,.2f}"
                )
                if o["action"] == "BUY":
                    total_bought += o["value"]
                else:
                    total_sold += o["value"]
            print(f"\n  Total Sold:    ${total_sold:>12,.2f}")
            print(f"  Total Bought:  ${total_bought:>12,.2f}")
            print(f"  Cash After:    ${float(self._simulated_cash):>12,.2f}")
        else:
            print("  No trades needed — portfolio is within drift threshold.")

        print(BANNER)
