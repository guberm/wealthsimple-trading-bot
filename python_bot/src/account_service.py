import logging
from decimal import Decimal
from typing import List, Optional
from .models import Account, Position, Currency

logger = logging.getLogger(__name__)


class AccountService:
    """Wraps Wealthsimple account-related endpoints."""

    def __init__(self, client):
        self._client = client

    def get_accounts(self) -> List[Account]:
        data = self._client.get("/account/list")
        results = data.get("results", [])
        accounts = []
        for raw in results:
            try:
                account = Account(
                    id=raw["id"],
                    account_type=raw.get("account_type", ""),
                    buying_power=Currency(
                        amount=Decimal(str(raw.get("buying_power", {}).get("amount", 0))),
                        currency=raw.get("buying_power", {}).get("currency", "CAD"),
                    ),
                    current_balance=Currency(
                        amount=Decimal(str(raw.get("current_balance", {}).get("amount", 0))),
                        currency=raw.get("current_balance", {}).get("currency", "CAD"),
                    ),
                    net_deposits=Currency(
                        amount=Decimal(str(raw.get("net_deposits", {}).get("amount", 0))),
                        currency=raw.get("net_deposits", {}).get("currency", "CAD"),
                    ),
                    status=raw.get("status", ""),
                )
                accounts.append(account)
            except (KeyError, TypeError) as e:
                logger.warning("Failed to parse account: %s — %s", raw.get("id", "?"), e)
        logger.info("Found %d accounts", len(accounts))
        return accounts

    def get_account_by_type(self, account_type: str) -> Optional[Account]:
        accounts = self.get_accounts()
        for acc in accounts:
            if acc.account_type == account_type:
                return acc
        logger.warning("No account found with type: %s", account_type)
        return None

    def get_positions(self, account_id: Optional[str] = None) -> List[Position]:
        params = {}
        if account_id:
            params["account_id"] = account_id
        data = self._client.get("/account/positions", params=params)
        results = data.get("results", [])
        positions = []
        for raw in results:
            try:
                stock = raw.get("stock", {})
                quote = raw.get("quote", {})
                quantity = Decimal(str(raw.get("quantity", 0)))
                current_price = Decimal(str(quote.get("amount", 0)))
                market_value = quantity * current_price
                book_value = Decimal(str(raw.get("book_value", {}).get("amount", 0)))

                pos = Position(
                    security_id=raw.get("id", raw.get("security_id", "")),
                    symbol=stock.get("symbol", ""),
                    quantity=quantity,
                    market_value=market_value,
                    book_value=book_value,
                    currency=stock.get("currency", "CAD"),
                    average_price=Decimal(str(raw.get("entry_price", {}).get("amount", 0))),
                    current_price=current_price,
                    gain_loss=market_value - book_value,
                    gain_loss_pct=(
                        ((market_value - book_value) / book_value * 100)
                        if book_value > 0
                        else Decimal("0")
                    ),
                )
                positions.append(pos)
            except (KeyError, TypeError, ZeroDivisionError) as e:
                logger.warning("Failed to parse position: %s", e)
        logger.info("Found %d positions", len(positions))
        return positions

    def get_buying_power(self, account_id: str) -> Decimal:
        accounts = self.get_accounts()
        for acc in accounts:
            if acc.id == account_id:
                return acc.buying_power.amount
        return Decimal("0")

    def get_total_portfolio_value(
        self, account_id: str, positions: Optional[List[Position]] = None
    ) -> Decimal:
        cash = self.get_buying_power(account_id)
        if positions is None:
            positions = self.get_positions(account_id)
        positions_value = sum(p.market_value for p in positions)
        total = cash + positions_value
        logger.info(
            "Portfolio value: cash=$%.2f + positions=$%.2f = total=$%.2f",
            cash, positions_value, total,
        )
        return total
