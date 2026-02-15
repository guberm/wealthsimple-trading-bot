import logging
from decimal import Decimal
from typing import Optional, Dict, List
from .models import Security, SpotQuote

logger = logging.getLogger(__name__)


class MarketDataService:
    """Wraps Wealthsimple security search and quote endpoints."""

    def __init__(self, client):
        self._client = client
        self._cache: Dict[str, Security] = {}

    def search_security(self, symbol: str) -> Optional[Security]:
        if symbol in self._cache:
            return self._cache[symbol]

        # Strip .TO suffix for WS search
        query = symbol.replace(".TO", "")
        data = self._client.get("/securities", params={"query": query})
        results = data.get("results", [])

        for raw in results:
            raw_symbol = raw.get("stock", {}).get("symbol", "")
            if raw_symbol.upper() == query.upper() or raw_symbol.upper() == symbol.upper():
                sec = self._parse_security(raw)
                self._cache[symbol] = sec
                return sec

        # If exact match not found, take first result
        if results:
            sec = self._parse_security(results[0])
            self._cache[symbol] = sec
            return sec

        logger.warning("Security not found: %s", symbol)
        return None

    def get_security_by_id(self, security_id: str) -> Optional[Security]:
        data = self._client.get(f"/securities/{security_id}")
        return self._parse_security(data) if data else None

    def get_quote(self, symbol: str) -> Decimal:
        sec = self.search_security(symbol)
        if sec and sec.quote:
            return sec.quote.amount
        return Decimal("0")

    def resolve_security_id(self, symbol: str) -> str:
        sec = self.search_security(symbol)
        if sec:
            return sec.id
        raise ValueError(f"Could not resolve security ID for {symbol}")

    def bulk_resolve_securities(self, symbols: List[str]) -> Dict[str, str]:
        result = {}
        for symbol in symbols:
            try:
                result[symbol] = self.resolve_security_id(symbol)
            except ValueError:
                logger.warning("Could not resolve: %s", symbol)
        logger.info("Resolved %d/%d securities", len(result), len(symbols))
        return result

    def get_bulk_quotes(self, symbols: List[str]) -> Dict[str, Decimal]:
        result = {}
        for symbol in symbols:
            price = self.get_quote(symbol)
            if price > 0:
                result[symbol] = price
        return result

    def _parse_security(self, raw: dict) -> Security:
        stock = raw.get("stock", raw)
        quote_data = raw.get("quote", {})
        quote = None
        if quote_data:
            quote = SpotQuote(
                amount=Decimal(str(quote_data.get("amount", 0))),
                ask=Decimal(str(quote_data.get("ask", 0))) if quote_data.get("ask") else None,
                bid=Decimal(str(quote_data.get("bid", 0))) if quote_data.get("bid") else None,
                high=Decimal(str(quote_data.get("high", 0))),
                low=Decimal(str(quote_data.get("low", 0))),
                volume=int(quote_data.get("volume", 0)),
                quote_date=str(quote_data.get("quote_date", "")),
            )

        return Security(
            id=raw.get("id", stock.get("id", "")),
            symbol=stock.get("symbol", ""),
            name=stock.get("name", ""),
            exchange=stock.get("primary_exchange", ""),
            currency=stock.get("currency", "CAD"),
            security_type=stock.get("security_type", ""),
            is_buyable=stock.get("is_buyable", True),
            quote=quote,
        )
