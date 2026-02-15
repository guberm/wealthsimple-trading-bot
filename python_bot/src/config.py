import os
import yaml
import logging
from pathlib import Path
from typing import List, Dict, Any
from dotenv import load_dotenv

logger = logging.getLogger(__name__)


class ConfigError(Exception):
    pass


class AppConfig:
    """Loads and validates all configuration from YAML + environment variables."""

    def __init__(self, settings: Dict[str, Any], universe: Dict[str, Any]):
        self._settings = settings
        self._universe = universe

    @staticmethod
    def load(config_dir: str = "config") -> "AppConfig":
        load_dotenv()
        config_path = Path(config_dir)

        settings_file = config_path / "settings.yaml"
        if not settings_file.exists():
            raise ConfigError(f"Settings file not found: {settings_file}")
        with open(settings_file, "r") as f:
            settings = yaml.safe_load(f)

        universe_file = config_path / "stock_universe.yaml"
        if not universe_file.exists():
            raise ConfigError(f"Stock universe file not found: {universe_file}")
        with open(universe_file, "r") as f:
            universe = yaml.safe_load(f)

        config = AppConfig(settings, universe)
        config.validate()
        return config

    def validate(self) -> None:
        if not self.ws_email:
            raise ConfigError("WS_EMAIL environment variable is required")
        if not self.ws_password:
            raise ConfigError("WS_PASSWORD environment variable is required")

        num_picks = self.stock_picker.get("num_picks", 7)
        if not 5 <= num_picks <= 10:
            raise ConfigError(f"num_picks must be between 5 and 10, got {num_picks}")

    # --- Wealthsimple credentials (from env) ---

    @property
    def ws_email(self) -> str:
        return os.environ.get("WS_EMAIL", "")

    @property
    def ws_password(self) -> str:
        return os.environ.get("WS_PASSWORD", "")

    @property
    def ws_otp_secret(self) -> str:
        return os.environ.get("WS_OTP_SECRET", "")

    # --- Config sections ---

    @property
    def wealthsimple(self) -> Dict[str, Any]:
        return self._settings.get("wealthsimple", {})

    @property
    def base_url(self) -> str:
        return self.wealthsimple.get("base_url", "https://trade-service.wealthsimple.com")

    @property
    def trading(self) -> Dict[str, Any]:
        return self._settings.get("trading", {})

    @property
    def schedule(self) -> Dict[str, Any]:
        return self._settings.get("schedule", {})

    @property
    def stock_picker(self) -> Dict[str, Any]:
        return self._settings.get("stock_picker", {})

    @property
    def rebalancer(self) -> Dict[str, Any]:
        return self._settings.get("rebalancer", {})

    @property
    def safety(self) -> Dict[str, Any]:
        return self._settings.get("safety", {})

    @property
    def logging_config(self) -> Dict[str, Any]:
        return self._settings.get("logging", {})

    # --- Derived properties ---

    @property
    def is_live_mode(self) -> bool:
        return (
            self.trading.get("mode", "dry_run") == "live"
            and self.trading.get("live_mode_confirmation", False) is True
        )

    @property
    def account_type(self) -> str:
        return self.trading.get("account_type", "ca_tfsa")

    def get_stock_universe(self) -> List[str]:
        symbols = []
        for item in self._universe.get("etfs", []):
            symbols.append(item["symbol"])
        for item in self._universe.get("stocks", []):
            symbols.append(item["symbol"])
        return symbols

    def get_etf_symbols(self) -> List[str]:
        return [item["symbol"] for item in self._universe.get("etfs", [])]
