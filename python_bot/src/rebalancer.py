import logging
from decimal import Decimal, ROUND_DOWN
from typing import List, Dict, Tuple
from .models import (
    Position, OrderRequest, OrderType, OrderSubType,
    PortfolioTarget, PortfolioSummary, StockScore,
)
from .config import AppConfig

logger = logging.getLogger(__name__)


class PortfolioRebalancer:
    """Calculates equal-weight targets and generates rebalancing orders."""

    def __init__(self, config: AppConfig):
        self._config = config

    def calculate_targets(
        self,
        selected_stocks: List[StockScore],
        current_positions: List[Position],
        cash_balance: Decimal,
        security_prices: Dict[str, Decimal],
        security_ids: Dict[str, str],
    ) -> PortfolioSummary:
        selected_symbols = [s.symbol for s in selected_stocks]
        positions_value = sum(p.market_value for p in current_positions)
        total_value = cash_balance + positions_value

        if total_value <= 0:
            logger.warning("Total portfolio value is $0 or negative")
            return PortfolioSummary(
                total_value=total_value,
                cash_balance=cash_balance,
                positions_value=positions_value,
                num_holdings=len(current_positions),
                targets=[],
            )

        num_buckets = len(selected_symbols)
        target_weight = Decimal("1") / Decimal(str(num_buckets))
        target_value_per = total_value * target_weight

        # Map current positions by symbol
        pos_map: Dict[str, Position] = {p.symbol: p for p in current_positions}

        drift_threshold = Decimal(
            str(self._config.rebalancer.get("drift_threshold_pct", 5.0))
        )
        min_trade = Decimal(
            str(self._config.rebalancer.get("min_trade_value_cad", 1.0))
        )
        max_trade = Decimal(
            str(self._config.safety.get("max_single_trade_cad", 5000.0))
        )

        targets = []

        # Targets for selected symbols
        for symbol in selected_symbols:
            current_pos = pos_map.get(symbol)
            price = security_prices.get(symbol, Decimal("0"))
            sec_id = security_ids.get(symbol, "")

            if price <= 0:
                logger.warning("No price for %s, skipping target", symbol)
                continue

            current_value = current_pos.market_value if current_pos else Decimal("0")
            current_weight = current_value / total_value if total_value > 0 else Decimal("0")
            drift_pct = (
                abs(current_weight - target_weight) / target_weight * 100
                if target_weight > 0
                else Decimal("0")
            )

            trade_value = target_value_per - current_value
            abs_trade = abs(trade_value)

            if drift_pct < drift_threshold or abs_trade < min_trade:
                action = "hold"
                trade_qty = 0
            elif trade_value > 0:
                action = "buy"
                capped = min(abs_trade, max_trade)
                trade_qty = int((capped / price).to_integral_value(rounding=ROUND_DOWN))
            else:
                action = "sell"
                capped = min(abs_trade, max_trade)
                trade_qty = int((capped / price).to_integral_value(rounding=ROUND_DOWN))

            targets.append(
                PortfolioTarget(
                    symbol=symbol,
                    security_id=sec_id,
                    target_weight=target_weight,
                    target_value=target_value_per,
                    current_value=current_value,
                    current_weight=current_weight,
                    drift_pct=drift_pct,
                    action=action,
                    trade_value=abs_trade,
                    trade_quantity=trade_qty,
                )
            )

        # Add sell targets for positions NOT in the selected list (liquidate)
        for pos in current_positions:
            if pos.symbol not in selected_symbols and pos.quantity > 0:
                price = pos.current_price if pos.current_price > 0 else Decimal("1")
                targets.append(
                    PortfolioTarget(
                        symbol=pos.symbol,
                        security_id=pos.security_id,
                        target_weight=Decimal("0"),
                        target_value=Decimal("0"),
                        current_value=pos.market_value,
                        current_weight=pos.market_value / total_value if total_value > 0 else Decimal("0"),
                        drift_pct=Decimal("100"),
                        action="sell",
                        trade_value=pos.market_value,
                        trade_quantity=int(pos.quantity),
                    )
                )

        logger.info(
            "Portfolio: total=$%.2f, cash=$%.2f, %d targets (%d buckets)",
            total_value, cash_balance, len(targets), num_buckets,
        )

        return PortfolioSummary(
            total_value=total_value,
            cash_balance=cash_balance,
            positions_value=positions_value,
            num_holdings=len(current_positions),
            targets=targets,
        )

    def generate_orders(
        self, summary: PortfolioSummary
    ) -> Tuple[List[OrderRequest], List[OrderRequest]]:
        sell_orders = []
        buy_orders = []

        for target in summary.targets:
            if target.action == "hold" or target.trade_quantity < 1:
                continue

            if target.action == "sell":
                sell_orders.append(
                    OrderRequest(
                        security_id=target.security_id,
                        symbol=target.symbol,
                        quantity=target.trade_quantity,
                        order_type=OrderType.SELL,
                        order_sub_type=OrderSubType.LIMIT,
                        limit_price=target.current_value / target.trade_quantity
                        if target.trade_quantity > 0
                        else Decimal("0"),
                    )
                )
            elif target.action == "buy":
                price = (
                    target.current_value / target.trade_quantity
                    if target.trade_quantity > 0 and target.current_value > 0
                    else target.target_value / target.trade_quantity
                    if target.trade_quantity > 0
                    else Decimal("0")
                )
                buy_orders.append(
                    OrderRequest(
                        security_id=target.security_id,
                        symbol=target.symbol,
                        quantity=target.trade_quantity,
                        order_type=OrderType.BUY,
                        order_sub_type=OrderSubType.LIMIT,
                        limit_price=price,
                    )
                )

        logger.info(
            "Generated %d sell orders, %d buy orders", len(sell_orders), len(buy_orders)
        )
        return sell_orders, buy_orders
