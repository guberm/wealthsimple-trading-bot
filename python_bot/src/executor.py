import time
import logging
from collections import deque
from typing import List
from .models import OrderRequest, OrderResponse

logger = logging.getLogger(__name__)


class RateLimiter:
    """Enforces Wealthsimple's 7 trades/hour rate limit using a sliding window."""

    def __init__(self, max_requests: int = 6, window_seconds: int = 3600):
        self._max_requests = max_requests
        self._window_seconds = window_seconds
        self._timestamps: deque = deque()

    def acquire(self) -> None:
        self._prune_expired()
        while len(self._timestamps) >= self._max_requests:
            oldest = self._timestamps[0]
            wait_time = self._window_seconds - (time.time() - oldest) + 1
            logger.warning(
                "Rate limit reached (%d/%d). Waiting %.0fs...",
                len(self._timestamps), self._max_requests, wait_time,
            )
            time.sleep(max(wait_time, 1))
            self._prune_expired()

        self._timestamps.append(time.time())

    def can_proceed(self) -> bool:
        self._prune_expired()
        return len(self._timestamps) < self._max_requests

    @property
    def remaining_capacity(self) -> int:
        self._prune_expired()
        return max(0, self._max_requests - len(self._timestamps))

    def _prune_expired(self) -> None:
        cutoff = time.time() - self._window_seconds
        while self._timestamps and self._timestamps[0] < cutoff:
            self._timestamps.popleft()


class OrderExecutor:
    """Executes orders against Wealthsimple, respecting rate limits and safety guards."""

    def __init__(self, order_service, rate_limiter: RateLimiter, config):
        self._order_service = order_service
        self._rate_limiter = rate_limiter
        self._config = config
        self._executed_orders: List[OrderResponse] = []
        self._daily_trade_count = 0

    def execute_orders(
        self,
        sell_orders: List[OrderRequest],
        buy_orders: List[OrderRequest],
    ) -> List[OrderResponse]:
        max_daily = self._config.safety.get("max_daily_trades", 20)
        results = []

        # Execute sells first to free cash
        logger.info("Executing %d sell orders...", len(sell_orders))
        for order in sell_orders:
            if self._daily_trade_count >= max_daily:
                logger.warning("Daily trade limit reached (%d), stopping", max_daily)
                break
            if not self._validate_order(order):
                continue
            resp = self._execute_single(order)
            if resp:
                results.append(resp)

        # Brief pause between sells and buys
        if sell_orders and buy_orders:
            logger.info("Pausing 5s between sells and buys...")
            time.sleep(5)

        # Execute buys
        logger.info("Executing %d buy orders...", len(buy_orders))
        for order in buy_orders:
            if self._daily_trade_count >= max_daily:
                logger.warning("Daily trade limit reached (%d), stopping", max_daily)
                break
            if not self._validate_order(order):
                continue
            resp = self._execute_single(order)
            if resp:
                results.append(resp)

        self._executed_orders.extend(results)
        logger.info(
            "Execution complete: %d/%d orders executed",
            len(results), len(sell_orders) + len(buy_orders),
        )
        return results

    def _execute_single(self, order: OrderRequest) -> OrderResponse | None:
        try:
            self._rate_limiter.acquire()
            resp = self._order_service.place_order(order)
            self._daily_trade_count += 1
            logger.info(
                "ORDER EXECUTED: %s %d x %s @ $%s — status=%s",
                order.order_type.value, order.quantity,
                order.symbol, order.limit_price, resp.status,
            )
            return resp
        except Exception as e:
            logger.error("Order failed for %s: %s", order.symbol, e)
            return None

    def _validate_order(self, order: OrderRequest) -> bool:
        max_trade = self._config.safety.get("max_single_trade_cad", 5000)
        trade_value = float(order.limit_price) * order.quantity

        if trade_value > max_trade:
            logger.warning(
                "Order value $%.2f exceeds max $%.2f for %s",
                trade_value, max_trade, order.symbol,
            )
            return False
        if order.quantity < 1:
            logger.warning("Invalid quantity %d for %s", order.quantity, order.symbol)
            return False
        return True

    def get_execution_summary(self) -> dict:
        successful = [o for o in self._executed_orders if o.status != "rejected"]
        return {
            "total_orders": len(self._executed_orders),
            "successful": len(successful),
            "failed": len(self._executed_orders) - len(successful),
            "daily_trades_used": self._daily_trade_count,
        }
