"""Talk to the Pinterest API v5 on behalf of the SaveSimple account.

Pinterest needs three things before it will create a pin for us:

  * an app registered on the business account (App ID + App secret),
  * a user token obtained once through the OAuth consent screen, and
  * a board to pin onto.

The token is the only secret this module touches. It is stored outside the repository
(``$XDG_CONFIG_HOME/realkevbase/pinterest-token.json``, mode 0600) so nothing sensitive
can be committed. Pinterest rotates the refresh token on every refresh, so both tokens
are written back after each exchange.

Image pins take their artwork in one of two ways: ``image_url``, where Pinterest fetches
a public URL itself, or ``image_base64``, where the bytes travel inside the request. The
second is what lets the storefront pin the PNGs the generator already rendered locally,
without publishing them first.

Usage from the other scripts::

    client = Pinterest()
    board = client.ensure_board("SaveSimple Printables")
    client.create_pin(board_id=board["id"], title=..., description=..., link=...,
                      alt_text=..., image_bytes=open(path, "rb").read())
"""

from __future__ import annotations

import base64
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request

from typing import Any, Iterator

#: Root of the v5 API, without a trailing slash.
API_ROOT = "https://api.pinterest.com/v5"

#: Where the browser is sent to approve the app.
OAUTH_PAGE = "https://www.pinterest.com/oauth/"

#: Token endpoint, shared by the authorization-code and refresh grants.
TOKEN_ENDPOINT = f"{API_ROOT}/oauth/token"

#: Everything pin creation and de-duplication need, and nothing more.
SCOPES = (
    "boards:read",
    "boards:write",
    "pins:read",
    "pins:write",
    "user_accounts:read",
)

SCOPE_STRING = ",".join(SCOPES)

#: Pinterest's documented field limits. Longer values are cut, not rejected.
TITLE_LIMIT = 100
DESCRIPTION_LIMIT = 800
LINK_LIMIT = 2048
ALT_TEXT_LIMIT = 500

#: Pin images may be 20 MB on the web; the PNGs here are under 1 MB.
MAX_IMAGE_BYTES = 20 * 1024 * 1024

#: Refresh a day early so a long run cannot expire halfway through.
REFRESH_MARGIN_SECONDS = 24 * 60 * 60

REQUEST_TIMEOUT = 30
USER_AGENT = "realkevbase-pinterest/1.0 (+https://realkevbase.com)"


class PinterestError(RuntimeError):
    """A Pinterest call failed, with the API's own status and message when it sent one."""

    def __init__(self, message: str, *, status: int | None = None, code: Any = None) -> None:
        super().__init__(message)
        self.status = status
        self.code = code


def clip(value: str, limit: int) -> str:
    """Cut a string to a Pinterest field limit.

    The cut is deliberately blunt: a limit of 100 characters means the 101st character is
    dropped even when it lands mid-word. Callers prefer keeping the whole start of a
    sentence to hunting for a word boundary.

    Args:
        value: Text to send to Pinterest.
        limit: Maximum characters Pinterest accepts for that field.

    Returns:
        ``value`` unchanged when it fits, otherwise its first ``limit`` characters.
    """
    return value[:limit]


def _config_root(environment: str, fallback: str) -> str:
    """Resolve an XDG base directory, honouring the environment variable when set."""
    root = os.environ.get(environment) or os.path.expanduser(fallback)
    return root


def default_token_path() -> str:
    """Return the default token location, outside the repository."""
    return os.path.join(_config_root("XDG_CONFIG_HOME", "~/.config"), "realkevbase", "pinterest-token.json")


def default_state_path() -> str:
    """Return the default pin ledger location, outside the repository."""
    return os.path.join(_config_root("XDG_STATE_HOME", "~/.local/state"), "realkevbase", "pinterest-pins.json")


def build_authorize_url(app_id: str, redirect_uri: str, state: str, scopes: tuple[str, ...] = SCOPES) -> str:
    """Build the consent URL a human opens once in a browser.

    Args:
        app_id: Pinterest App ID.
        redirect_uri: One of the redirect URIs registered for the app, matched exactly.
        state: Random value echoed back on the redirect, used to reject foreign callbacks.
        scopes: Permissions to request.

    Returns:
        The URL of Pinterest's consent screen.
    """
    query = urllib.parse.urlencode(
        {
            "client_id": app_id,
            "redirect_uri": redirect_uri,
            "response_type": "code",
            "scope": ",".join(scopes),
            "state": state,
        }
    )
    return f"{OAUTH_PAGE}?{query}"


def media_source(
    *,
    image_bytes: bytes | None = None,
    image_url: str | None = None,
    content_type: str = "image/png",
) -> dict[str, Any]:
    """Describe the pin's artwork to Pinterest.

    Exactly one of ``image_bytes`` or ``image_url`` must be given. Bytes are sent inline,
    which is how a locally rendered PNG reaches Pinterest without being published first.

    Args:
        image_bytes: Raw image file contents.
        image_url: Public URL Pinterest can download the image from.
        content_type: MIME type of ``image_bytes``; Pinterest accepts JPEG and PNG inline.

    Returns:
        The ``media_source`` object for a pin request.

    Raises:
        ValueError: When neither or both image sources are given.
        PinterestError: When the image exceeds Pinterest's 20 MB limit.
    """
    if (image_bytes is None) == (image_url is None):
        raise ValueError("give exactly one of image_bytes or image_url")

    if image_bytes is not None:
        if len(image_bytes) > MAX_IMAGE_BYTES:
            raise PinterestError(
                f"image is {len(image_bytes) / 1024 / 1024:.1f} MB; Pinterest accepts at most 20 MB"
            )
        return {
            "source_type": "image_base64",
            "content_type": content_type,
            "data": base64.b64encode(image_bytes).decode("ascii"),
        }

    return {"source_type": "image_url", "url": image_url}


def _http(
    method: str,
    url: str,
    *,
    body: bytes | None = None,
    headers: dict[str, str] | None = None,
    timeout: int = REQUEST_TIMEOUT,
) -> tuple[int, bytes, Any]:
    """Send one HTTP request and return its status, body and headers, even on 4xx/5xx."""
    request = urllib.request.Request(url, data=body, headers=headers or {}, method=method)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, response.read(), response.headers
    except urllib.error.HTTPError as error:
        return error.code, error.read(), error.headers
    except urllib.error.URLError as error:
        raise PinterestError(f"could not reach Pinterest: {error.reason}") from error


def _decode(raw: bytes) -> Any:
    """Parse a JSON body, falling back to text so error pages stay readable."""
    if not raw:
        return {}
    try:
        return json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return {"message": raw.decode("utf-8", "replace").strip()[:500]}


def _describe(status: int, payload: Any) -> str:
    """Turn an error payload into one line for the operator."""
    if isinstance(payload, dict):
        message = payload.get("message") or payload.get("error") or payload
        code = payload.get("code")
        return f"HTTP {status} ({code}): {message}" if code else f"HTTP {status}: {message}"
    return f"HTTP {status}: {payload}"


def _retry_after(headers: Any) -> float | None:
    """Read the ``Retry-After`` header in seconds, when Pinterest sends one."""
    value = headers.get("Retry-After") if headers is not None else None
    if not value:
        return None
    try:
        return float(value)
    except ValueError:
        return None


class TokenStore:
    """Loads and saves the OAuth token, keeping the file private and the writes atomic."""

    def __init__(self, path: str | None = None) -> None:
        self.path = os.path.abspath(
            os.path.expanduser(path or os.environ.get("PINTEREST_TOKEN_FILE") or default_token_path())
        )

    def load(self) -> dict[str, Any]:
        """Return the stored token, or an empty dict when the file does not exist yet."""
        try:
            with open(self.path, encoding="utf-8") as handle:
                return json.load(handle)
        except FileNotFoundError:
            return {}
        except json.JSONDecodeError as error:
            raise PinterestError(f"token file {self.path} is not valid JSON: {error}") from error

    def save(self, token: dict[str, Any]) -> None:
        """Write the token with owner-only permissions, replacing the file atomically."""
        directory = os.path.dirname(self.path)
        os.makedirs(directory, mode=0o700, exist_ok=True)
        temporary = f"{self.path}.tmp"
        with open(temporary, "w", encoding="utf-8") as handle:
            json.dump(token, handle, indent=2, sort_keys=True)
            handle.write("\n")
        os.chmod(temporary, 0o600)
        os.replace(temporary, self.path)


class Pinterest:
    """A small client for the handful of v5 endpoints the shop actually uses."""

    def __init__(
        self,
        token_path: str | None = None,
        *,
        app_id: str | None = None,
        app_secret: str | None = None,
        timeout: int = REQUEST_TIMEOUT,
    ) -> None:
        self.store = TokenStore(token_path)
        self.app_id = app_id
        self.app_secret = app_secret
        self.timeout = timeout
        self._token: dict[str, Any] | None = None

    @classmethod
    def has_token(cls, token_path: str | None = None) -> bool:
        """Report whether a token file exists, without validating it."""
        return bool(TokenStore(token_path).load().get("access_token"))

    # -- tokens ---------------------------------------------------------------------

    @property
    def token(self) -> dict[str, Any]:
        """The stored token, read once per run."""
        if self._token is None:
            self._token = self.store.load()
        return self._token

    def _credentials(self) -> tuple[str, str]:
        """Return the app credentials needed to exchange or refresh a token."""
        app_id = self.app_id or os.environ.get("PINTEREST_APP_ID")
        app_secret = self.app_secret or os.environ.get("PINTEREST_APP_SECRET")
        if not app_id or not app_secret:
            raise PinterestError(
                "PINTEREST_APP_ID and PINTEREST_APP_SECRET must be set to authorise or refresh the token."
            )
        return app_id, app_secret

    def _token_request(self, fields: dict[str, str]) -> dict[str, Any]:
        """Post one grant to the token endpoint using HTTP Basic authentication."""
        app_id, app_secret = self._credentials()
        basic = base64.b64encode(f"{app_id}:{app_secret}".encode()).decode("ascii")
        status, raw, _ = _http(
            "POST",
            TOKEN_ENDPOINT,
            body=urllib.parse.urlencode(fields).encode("ascii"),
            headers={
                "Authorization": f"Basic {basic}",
                "Content-Type": "application/x-www-form-urlencoded",
                "Accept": "application/json",
                "User-Agent": USER_AGENT,
            },
            timeout=self.timeout,
        )
        payload = _decode(raw)
        if status != 200:
            raise PinterestError(f"Pinterest rejected the token request: {_describe(status, payload)}", status=status)
        return payload

    def _store_response(self, payload: dict[str, Any], *, account: str | None = None) -> None:
        """Persist a token response, keeping the previous refresh token when none is sent."""
        now = int(time.time())
        token = dict(self.token)
        token["access_token"] = payload["access_token"]
        token["expires_at"] = now + int(payload.get("expires_in", 2592000))
        token["updated_at"] = now
        token["scope"] = payload.get("scope", token.get("scope", ""))
        if payload.get("refresh_token"):
            token["refresh_token"] = payload["refresh_token"]
        if payload.get("refresh_token_expires_in"):
            token["refresh_expires_at"] = now + int(payload["refresh_token_expires_in"])
        if account:
            token["account"] = account
        self.store.save(token)
        self._token = token

    def record_account(self, username: str) -> None:
        """Remember which account the token belongs to, so a wrong login is obvious later."""
        token = dict(self.token)
        token["account"] = username
        self.store.save(token)
        self._token = token

    def exchange_code(self, code: str, redirect_uri: str) -> dict[str, Any]:
        """Swap the one-time authorization code for an access token and store it."""
        payload = self._token_request(
            {"grant_type": "authorization_code", "code": code, "redirect_uri": redirect_uri}
        )
        self._store_response(payload)
        return payload

    def refresh(self) -> dict[str, Any]:
        """Renew the access token, keeping the rotated refresh token."""
        refresh_token = self.token.get("refresh_token")
        if not refresh_token:
            raise PinterestError(
                f"no refresh token in {self.store.path}; run scripts/pinterest_auth.py again."
            )
        payload = self._token_request({"grant_type": "refresh_token", "refresh_token": refresh_token})
        self._store_response(payload)
        return payload

    def access_token(self, *, force: bool = False) -> str:
        """Return a usable access token, refreshing shortly before it expires."""
        token = self.token
        if not token.get("access_token"):
            raise PinterestError(f"no Pinterest token at {self.store.path}; run scripts/pinterest_auth.py first.")

        expires_at = int(token.get("expires_at", 0))
        if force or (expires_at and expires_at - time.time() < REFRESH_MARGIN_SECONDS):
            if token.get("refresh_token"):
                self.refresh()
        return self.token["access_token"]

    # -- requests -------------------------------------------------------------------

    def _call(
        self,
        method: str,
        path: str,
        *,
        body: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
        attempts: int = 3,
        expect: tuple[int, ...] = (200, 201),
    ) -> Any:
        """Call an endpoint, retrying throttling and outages and refreshing once on 401."""
        url = f"{API_ROOT}{path}"
        if params:
            url = f"{url}?{urllib.parse.urlencode(params)}"

        data = None if body is None else json.dumps(body).encode("utf-8")
        refreshed = False

        for attempt in range(1, attempts + 1):
            headers = {
                "Authorization": f"Bearer {self.access_token()}",
                "Accept": "application/json",
                "User-Agent": USER_AGENT,
            }
            if data is not None:
                headers["Content-Type"] = "application/json"

            status, raw, response_headers = _http(method, url, body=data, headers=headers, timeout=self.timeout)
            payload = _decode(raw)

            if status in expect:
                return payload

            if status == 401 and not refreshed and self.token.get("refresh_token"):
                self.refresh()
                refreshed = True
                continue

            if status == 429 and attempt < attempts:
                delay = _retry_after(response_headers)
                if delay is None:
                    delay = 60.0
                delay = min(max(delay, 0.0), 300.0)
                print(f"Pinterest rate limit reached; waiting {delay:.0f}s before retrying")
                time.sleep(delay)
                continue

            if status >= 500 and attempt < attempts:
                time.sleep(min(2**attempt, 20))
                continue

            raise PinterestError(
                f"{method} {path} failed: {_describe(status, payload)}",
                status=status,
                code=payload.get("code") if isinstance(payload, dict) else None,
            )

        raise PinterestError(f"{method} {path} still failing after {attempts} attempts")

    def _paged(self, path: str, *, page_size: int = 100) -> Iterator[dict[str, Any]]:
        """Walk every page of a list endpoint, following Pinterest's bookmark cursor."""
        bookmark = None
        while True:
            params = {"page_size": page_size}
            if bookmark:
                params["bookmark"] = bookmark
            payload = self._call("GET", path, params=params)
            yield from payload.get("items", [])
            bookmark = payload.get("bookmark")
            if not bookmark:
                return

    # -- account, boards, pins ------------------------------------------------------

    def get_account(self) -> dict[str, Any]:
        """Return the account the token belongs to."""
        return self._call("GET", "/user_account")

    def list_boards(self) -> Iterator[dict[str, Any]]:
        """Yield every board the token can see."""
        yield from self._paged("/boards")

    def find_board(self, name: str) -> dict[str, Any] | None:
        """Return the board with this name, comparing case-insensitively."""
        wanted = name.strip().casefold()
        for board in self.list_boards():
            if str(board.get("name", "")).strip().casefold() == wanted:
                return board
        return None

    def create_board(self, name: str, description: str = "") -> dict[str, Any]:
        """Create a public board and return it."""
        return self._call("POST", "/boards", body={"name": name, "description": description})

    def ensure_board(self, name: str, *, create: bool = False, description: str = "") -> dict[str, Any]:
        """Find the board by name, creating it only when explicitly allowed."""
        board = self.find_board(name)
        if board:
            return board
        if not create:
            raise PinterestError(
                f"no board named {name!r} on this account; pass --create-board to create it"
            )
        return self.create_board(name, description)

    def list_board_pins(self, board_id: str) -> Iterator[dict[str, Any]]:
        """Yield every pin saved on one board."""
        yield from self._paged(f"/boards/{board_id}/pins")

    def all_pins(self) -> Iterator[tuple[dict[str, Any], dict[str, Any]]]:
        """Yield ``(board, pin)`` for every pin on every board, for account-wide de-duplication."""
        for board in self.list_boards():
            try:
                for pin in self.list_board_pins(str(board["id"])):
                    yield board, pin
            except PinterestError as error:
                print(f"skipped board {board.get('name')!r}: {error}")
                continue

    def create_pin(
        self,
        *,
        board_id: str,
        title: str,
        description: str,
        link: str,
        alt_text: str,
        image_bytes: bytes | None = None,
        image_url: str | None = None,
    ) -> dict[str, Any]:
        """Create one image pin and return Pinterest's representation of it."""
        body = {
            "board_id": board_id,
            "title": clip(title, TITLE_LIMIT),
            "description": clip(description, DESCRIPTION_LIMIT),
            "link": clip(link, LINK_LIMIT),
            "alt_text": clip(alt_text, ALT_TEXT_LIMIT),
            "media_source": media_source(image_bytes=image_bytes, image_url=image_url),
        }
        return self._call("POST", "/pins", body=body)
