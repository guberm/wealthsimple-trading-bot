import time
import logging
import requests
import pyotp
from typing import Optional, Tuple

logger = logging.getLogger(__name__)


class AuthenticationError(Exception):
    pass


class WealthsimpleAuthenticator:
    """Handles login, 2FA, and token lifecycle with the Wealthsimple Trade API."""

    def __init__(self, base_url: str, email: str, password: str, otp_secret: str = ""):
        self._base_url = base_url.rstrip("/")
        self._email = email
        self._password = password
        self._otp_secret = otp_secret
        self._access_token: Optional[str] = None
        self._refresh_token: Optional[str] = None
        self._session = requests.Session()

    def login(self) -> Tuple[str, str]:
        url = f"{self._base_url}/auth/login"
        payload = {"email": self._email, "password": self._password}

        # First attempt without OTP
        logger.info("Attempting login for %s", self._email)
        resp = self._session.post(url, json=payload)

        # If 2FA is required, the API returns 401 with x-wealthsimple-otp header
        if resp.status_code == 401 and self._otp_secret:
            otp = self._generate_otp()
            payload["otp"] = otp
            logger.info("2FA required, retrying with OTP")
            resp = self._session.post(url, json=payload)

        if resp.status_code not in (200, 201):
            raise AuthenticationError(
                f"Login failed with status {resp.status_code}: {resp.text}"
            )

        self._access_token = resp.headers.get("X-Access-Token", "")
        self._refresh_token = resp.headers.get("X-Refresh-Token", "")

        if not self._access_token:
            # Some responses include tokens in the JSON body
            data = resp.json()
            self._access_token = data.get("access_token", "")
            self._refresh_token = data.get("refresh_token", "")

        if not self._access_token:
            raise AuthenticationError("No access token received from login response")

        logger.info("Login successful")
        return self._access_token, self._refresh_token

    def _generate_otp(self) -> str:
        if not self._otp_secret:
            raise AuthenticationError("OTP secret not configured but 2FA is required")
        totp = pyotp.TOTP(self._otp_secret)
        return totp.now()

    def refresh(self) -> str:
        if not self._refresh_token:
            raise AuthenticationError("No refresh token available")

        url = f"{self._base_url}/auth/refresh"
        resp = self._session.post(url, json={"refresh_token": self._refresh_token})

        if resp.status_code not in (200, 201):
            raise AuthenticationError(
                f"Token refresh failed with status {resp.status_code}: {resp.text}"
            )

        self._access_token = resp.headers.get("X-Access-Token", self._access_token)
        self._refresh_token = resp.headers.get("X-Refresh-Token", self._refresh_token)
        logger.info("Token refreshed successfully")
        return self._access_token

    @property
    def access_token(self) -> str:
        if not self._access_token:
            raise AuthenticationError("Not authenticated. Call login() first.")
        return self._access_token

    @property
    def is_authenticated(self) -> bool:
        return bool(self._access_token)


class TokenManager:
    """Wraps Authenticator for automatic token refresh before expiry."""

    TOKEN_LIFETIME_SECONDS = 900  # ~15 minutes observed
    REFRESH_MARGIN_SECONDS = 120  # refresh 2 min before expiry

    def __init__(self, authenticator: WealthsimpleAuthenticator):
        self._auth = authenticator
        self._last_auth_time: float = 0

    def get_valid_token(self) -> str:
        if not self._auth.is_authenticated:
            self.ensure_authenticated()
        elif self._needs_refresh():
            try:
                self._auth.refresh()
                self._last_auth_time = time.time()
                logger.info("Token proactively refreshed")
            except AuthenticationError:
                logger.warning("Refresh failed, performing full re-login")
                self._auth.login()
                self._last_auth_time = time.time()
        return self._auth.access_token

    def ensure_authenticated(self) -> None:
        self._auth.login()
        self._last_auth_time = time.time()

    def _needs_refresh(self) -> bool:
        elapsed = time.time() - self._last_auth_time
        return elapsed > (self.TOKEN_LIFETIME_SECONDS - self.REFRESH_MARGIN_SECONDS)
