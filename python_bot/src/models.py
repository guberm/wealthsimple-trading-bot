from pydantic import BaseModel
from decimal import Decimal
from enum import Enum
from typing import Optional, List
from datetime import datetime


class Currency(BaseModel):
    amount: Decimal
    currency: str  # "CAD" or "USD"


class Account(BaseModel):
    id: str
    account_type: str  # "ca_tfsa", "ca_rrsp", "ca_non_registered"
    buying_power: Currency
    current_balance: Currency
    net_deposits: Currency
    available_to_withdraw: Optional[Currency] = None
    status: str


class Position(BaseModel):
    security_id: str
    symbol: str
    quantity: Decimal
    market_value: Decimal
    book_value: Decimal
    currency: str
    average_price: Decimal
    current_price: Decimal
    gain_loss: Decimal
    gain_loss_pct: Decimal


class OrderType(str, Enum):
    BUY = "buy_quantity"
    SELL = "sell_quantity"


class OrderSubType(str, Enum):
    MARKET = "market"
    LIMIT = "limit"


class OrderRequest(BaseModel):
    security_id: str
    symbol: str
    quantity: int
    order_type: OrderType
    order_sub_type: OrderSubType = OrderSubType.LIMIT
    limit_price: Decimal
    time_in_force: str = "day"

    def to_api_payload(self) -> dict:
        return {
            "security_id": self.security_id,
            "quantity": self.quantity,
            "order_type": self.order_type.value,
            "order_sub_type": self.order_sub_type.value,
            "limit_price": float(self.limit_price),
            "time_in_force": self.time_in_force,
        }


class OrderResponse(BaseModel):
    order_id: str
    security_id: str
    symbol: str = ""
    quantity: int
    order_type: str
    status: str
    limit_price: Optional[Decimal] = None
    filled_at: Optional[datetime] = None
    created_at: Optional[datetime] = None


class SpotQuote(BaseModel):
    amount: Decimal
    ask: Optional[Decimal] = None
    bid: Optional[Decimal] = None
    high: Decimal
    low: Decimal
    volume: int
    quote_date: str = ""


class Security(BaseModel):
    id: str  # "sec-s-xxxx"
    symbol: str
    name: str
    exchange: str = ""
    currency: str = "CAD"
    security_type: str = ""  # "equity", "exchange_traded_fund"
    is_buyable: bool = True
    quote: Optional[SpotQuote] = None


class PortfolioTarget(BaseModel):
    symbol: str
    security_id: str
    target_weight: Decimal
    target_value: Decimal
    current_value: Decimal
    current_weight: Decimal
    drift_pct: Decimal
    action: str  # "buy", "sell", "hold"
    trade_value: Decimal
    trade_quantity: int


class PortfolioSummary(BaseModel):
    total_value: Decimal
    cash_balance: Decimal
    positions_value: Decimal
    num_holdings: int
    targets: List[PortfolioTarget]


class StockScore(BaseModel):
    symbol: str
    name: str = ""
    sector: str = ""
    market_cap: float = 0.0
    avg_volume: float = 0.0
    return_90d: float = 0.0
    return_30d: float = 0.0
    volatility: float = 0.0
    sharpe_ratio: float = 0.0
    composite_score: float = 0.0
    is_etf: bool = False
