import logging
import requests
from typing import Dict, Any, Optional

logger = logging.getLogger(__name__)


class WealthsimpleAPIError(Exception):
    def __init__(self, status_code: int, message: str):
        self.status_code = status_code
        super().__init__(f"WS API error {status_code}: {message}")


class WealthsimpleClient:
    """Base HTTP client for the Wealthsimple Trade API."""

    def __init__(self, base_url: str, token_manager):
        self._base_url = base_url.rstrip("/")
        self._token_manager = token_manager
        self._session = requests.Session()

    def get(self, path: str, params: Optional[Dict[str, str]] = None) -> Dict[str, Any]:
        url = f"{self._base_url}{path}"
        headers = self._get_headers()
        logger.debug("GET %s params=%s", url, params)

        resp = self._session.get(url, headers=headers, params=params)

        # Retry once on 401 with refreshed token
        if resp.status_code == 401:
            logger.info("Got 401, refreshing token and retrying")
            headers = self._get_headers(force_refresh=True)
            resp = self._session.get(url, headers=headers, params=params)

        return self._handle_response(resp)

    def post(self, path: str, json_body: Dict[str, Any]) -> Dict[str, Any]:
        url = f"{self._base_url}{path}"
        headers = self._get_headers()
        logger.debug("POST %s body=%s", url, json_body)

        resp = self._session.post(url, headers=headers, json=json_body)

        if resp.status_code == 401:
            logger.info("Got 401, refreshing token and retrying")
            headers = self._get_headers(force_refresh=True)
            resp = self._session.post(url, headers=headers, json=json_body)

        return self._handle_response(resp)

    def delete(self, path: str) -> None:
        url = f"{self._base_url}{path}"
        headers = self._get_headers()
        logger.debug("DELETE %s", url)

        resp = self._session.delete(url, headers=headers)

        if resp.status_code == 401:
            headers = self._get_headers(force_refresh=True)
            resp = self._session.delete(url, headers=headers)

        if resp.status_code not in (200, 204):
            raise WealthsimpleAPIError(resp.status_code, resp.text)

    def _get_headers(self, force_refresh: bool = False) -> Dict[str, str]:
        if force_refresh:
            self._token_manager.ensure_authenticated()
        token = self._token_manager.get_valid_token()
        return {"Authorization": token}

    def _handle_response(self, resp: requests.Response) -> Dict[str, Any]:
        if resp.status_code not in (200, 201):
            raise WealthsimpleAPIError(resp.status_code, resp.text)

        try:
            data = resp.json()
        except ValueError:
            raise WealthsimpleAPIError(resp.status_code, "Invalid JSON response")

        logger.debug("Response: status=%d", resp.status_code)
        return data
