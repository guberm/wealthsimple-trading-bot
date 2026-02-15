import logging
from typing import List
from .models import OrderRequest, OrderResponse

logger = logging.getLogger(__name__)


class OrderService:
    """Wraps Wealthsimple order endpoints."""

    def __init__(self, client):
        self._client = client

    def place_order(self, order: OrderRequest) -> OrderResponse:
        logger.info(
            "Placing order: %s %d x %s @ $%s",
            order.order_type.value, order.quantity, order.symbol, order.limit_price,
        )
        data = self._client.post("/orders", order.to_api_payload())
        return self._parse_order(data)

    def get_orders(self) -> List[OrderResponse]:
        data = self._client.get("/orders")
        results = data.get("results", [])
        return [self._parse_order(raw) for raw in results]

    def cancel_order(self, order_id: str) -> None:
        logger.info("Cancelling order: %s", order_id)
        self._client.delete(f"/orders/{order_id}")

    def get_pending_orders(self) -> List[OrderResponse]:
        orders = self.get_orders()
        pending = [o for o in orders if o.status in ("submitted", "pending", "new")]
        logger.info("Found %d pending orders", len(pending))
        return pending

    def _parse_order(self, raw: dict) -> OrderResponse:
        return OrderResponse(
            order_id=raw.get("order_id", raw.get("id", "")),
            security_id=raw.get("security_id", ""),
            symbol=raw.get("symbol", ""),
            quantity=int(raw.get("quantity", 0)),
            order_type=raw.get("order_type", ""),
            status=raw.get("status", ""),
            limit_price=raw.get("limit_price", {}).get("amount")
            if isinstance(raw.get("limit_price"), dict)
            else raw.get("limit_price"),
            filled_at=raw.get("filled_at"),
            created_at=raw.get("created_at"),
        )
