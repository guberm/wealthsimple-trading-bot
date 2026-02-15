import logging
import math
import yfinance as yf
import pandas as pd
import numpy as np
from typing import List, Dict
from .models import StockScore
from .config import AppConfig

logger = logging.getLogger(__name__)

RISK_FREE_RATE = 0.04  # 4% Canadian risk-free rate


class StockPicker:
    """Screens a curated stock universe using Yahoo Finance data and selects
    the top N securities based on a composite scoring algorithm."""

    def __init__(self, config: AppConfig):
        self._config = config
        self._etf_symbols = set(config.get_etf_symbols())

    def pick_stocks(self, num_picks: int = 0) -> List[StockScore]:
        if num_picks == 0:
            num_picks = self._config.stock_picker.get("num_picks", 7)

        universe = self._config.get_stock_universe()
        lookback = self._config.stock_picker.get("lookback_days", 90)
        logger.info(
            "Picking top %d from %d securities (lookback=%d days)",
            num_picks, len(universe), lookback,
        )

        # 1. Fetch historical data
        hist_data = self._fetch_market_data(universe, lookback)

        # 2. Calculate metrics for each symbol
        scores: List[StockScore] = []
        for symbol in universe:
            if symbol not in hist_data or hist_data[symbol].empty:
                logger.warning("No data for %s, skipping", symbol)
                continue
            try:
                score = self._calculate_metrics(symbol, hist_data[symbol])
                scores.append(score)
            except Exception as e:
                logger.warning("Error calculating metrics for %s: %s", symbol, e)

        # 3. Filter
        scores = self._apply_filters(scores)
        if not scores:
            logger.error("No securities passed filters!")
            return []

        # 4. Compute composite scores using percentile ranking
        scores = self._rank_and_score(scores)

        # 5. Sort by composite score descending
        scores.sort(key=lambda s: s.composite_score, reverse=True)

        # 6. Apply sector diversity
        if self._config.stock_picker.get("sector_diversity", True):
            max_per = self._config.stock_picker.get("max_per_sector", 2)
            scores = self._apply_sector_diversity(scores, max_per)

        # 7. Take top N
        picks = scores[:num_picks]
        logger.info("Selected %d stocks:", len(picks))
        for i, s in enumerate(picks, 1):
            logger.info(
                "  %d. %s (score=%.4f, 90d=%.2f%%, sharpe=%.2f, sector=%s)",
                i, s.symbol, s.composite_score,
                s.return_90d * 100, s.sharpe_ratio, s.sector,
            )
        return picks

    def _fetch_market_data(
        self, symbols: List[str], lookback_days: int
    ) -> Dict[str, pd.DataFrame]:
        logger.info("Fetching %d-day history for %d symbols...", lookback_days, len(symbols))
        result = {}
        try:
            # Download all at once for efficiency
            period = f"{lookback_days}d"
            data = yf.download(symbols, period=period, group_by="ticker", progress=False)

            if len(symbols) == 1:
                result[symbols[0]] = data
            else:
                for symbol in symbols:
                    try:
                        df = data[symbol].dropna()
                        if not df.empty:
                            result[symbol] = df
                    except (KeyError, TypeError):
                        logger.debug("No data column for %s", symbol)
        except Exception as e:
            logger.error("Bulk download failed: %s. Falling back to individual.", e)
            for symbol in symbols:
                try:
                    ticker = yf.Ticker(symbol)
                    df = ticker.history(period=f"{lookback_days}d")
                    if not df.empty:
                        result[symbol] = df
                except Exception as ex:
                    logger.debug("Failed to fetch %s: %s", symbol, ex)

        logger.info("Got data for %d/%d symbols", len(result), len(symbols))
        return result

    def _calculate_metrics(self, symbol: str, df: pd.DataFrame) -> StockScore:
        close = df["Close"]
        volume = df["Volume"]

        # Returns
        return_90d = (close.iloc[-1] / close.iloc[0]) - 1 if len(close) > 1 else 0.0
        # ~22 trading days for 30 calendar days
        idx_30d = max(0, len(close) - 22)
        return_30d = (close.iloc[-1] / close.iloc[idx_30d]) - 1 if len(close) > idx_30d else 0.0

        # Volatility
        daily_returns = close.pct_change().dropna()
        volatility = daily_returns.std() * math.sqrt(252) if len(daily_returns) > 1 else 999.0

        # Sharpe ratio
        annualized_return = return_90d * (365.0 / 90.0)
        sharpe = (annualized_return - RISK_FREE_RATE) / volatility if volatility > 0 else 0.0

        # Volume
        avg_volume = volume.mean() if len(volume) > 0 else 0.0

        # Get ticker info for market cap and sector
        market_cap = 0.0
        sector = "ETF" if symbol in self._etf_symbols else "Unknown"
        try:
            info = yf.Ticker(symbol).info
            market_cap = info.get("marketCap", 0) or 0
            if symbol not in self._etf_symbols:
                sector = info.get("sector", "Unknown") or "Unknown"
        except Exception:
            pass

        is_etf = symbol in self._etf_symbols

        return StockScore(
            symbol=symbol,
            name=symbol,
            sector=sector,
            market_cap=float(market_cap),
            avg_volume=float(avg_volume),
            return_90d=float(return_90d),
            return_30d=float(return_30d),
            volatility=float(volatility),
            sharpe_ratio=float(sharpe),
            composite_score=0.0,  # computed later via ranking
            is_etf=is_etf,
        )

    def _apply_filters(self, scores: List[StockScore]) -> List[StockScore]:
        min_cap = self._config.stock_picker.get("min_market_cap_millions", 500) * 1e6
        min_vol = self._config.stock_picker.get("min_avg_volume", 100000)

        filtered = []
        for s in scores:
            # ETFs often don't report market cap — skip that check for ETFs
            if not s.is_etf and s.market_cap > 0 and s.market_cap < min_cap:
                logger.debug("Filtered %s: market cap $%.0f < $%.0f", s.symbol, s.market_cap, min_cap)
                continue
            if s.avg_volume < min_vol:
                logger.debug("Filtered %s: avg volume %.0f < %.0f", s.symbol, s.avg_volume, min_vol)
                continue
            filtered.append(s)

        logger.info("After filters: %d/%d passed", len(filtered), len(scores))
        return filtered

    def _rank_and_score(self, scores: List[StockScore]) -> List[StockScore]:
        n = len(scores)
        if n == 0:
            return scores

        def percentile_rank(values: List[float]) -> List[float]:
            sorted_vals = sorted(enumerate(values), key=lambda x: x[1])
            ranks = [0.0] * n
            for rank_idx, (orig_idx, _) in enumerate(sorted_vals):
                ranks[orig_idx] = rank_idx / max(n - 1, 1)
            return ranks

        mom90_ranks = percentile_rank([s.return_90d for s in scores])
        sharpe_ranks = percentile_rank([s.sharpe_ratio for s in scores])
        mom30_ranks = percentile_rank([s.return_30d for s in scores])
        vol_ranks = percentile_rank([s.avg_volume for s in scores])
        # Inverse volatility — lower vol = higher rank
        inv_vol_ranks = percentile_rank([-s.volatility for s in scores])

        prefer_etfs = self._config.stock_picker.get("prefer_etfs", True)

        updated = []
        for i, s in enumerate(scores):
            composite = (
                0.30 * mom90_ranks[i]
                + 0.25 * sharpe_ranks[i]
                + 0.20 * mom30_ranks[i]
                + 0.15 * vol_ranks[i]
                + 0.10 * inv_vol_ranks[i]
            )
            if prefer_etfs and s.is_etf:
                composite += 0.10

            updated.append(s.model_copy(update={"composite_score": composite}))

        return updated

    def _apply_sector_diversity(
        self, scores: List[StockScore], max_per_sector: int = 2
    ) -> List[StockScore]:
        sector_counts: Dict[str, int] = {}
        diverse = []
        for s in scores:
            count = sector_counts.get(s.sector, 0)
            if count < max_per_sector:
                diverse.append(s)
                sector_counts[s.sector] = count + 1
            else:
                logger.debug(
                    "Sector cap: skipping %s (sector=%s, already %d)",
                    s.symbol, s.sector, count,
                )
        return diverse
