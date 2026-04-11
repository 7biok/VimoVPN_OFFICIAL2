from __future__ import annotations

import asyncio
import logging
import uuid as uuid_lib
from dataclasses import dataclass
from datetime import datetime, time, timedelta
from typing import Any
from urllib.parse import quote, urlparse

import aiohttp

from shop_bot.data_manager.database import get_host, get_key_by_email

logger = logging.getLogger(__name__)

DEFAULT_USAGE_LIMIT_GB = 1000.0
REQUEST_TIMEOUT = aiohttp.ClientTimeout(total=25)
PREFERRED_CONFIG_NAMES = (
    "Subscription link",
    "Auto",
    "Full Singbox",
    "Clash Meta",
    "Clash",
    "Full Xray",
)


class HiddifyPanelError(RuntimeError):
    pass


@dataclass(frozen=True)
class HiddifyHostConfig:
    host_name: str
    base_url: str
    api_key: str
    admin_proxy_path: str
    client_proxy_path: str
    subscription_url: str | None = None

    def admin_url(self, suffix: str = "") -> str:
        suffix = suffix.lstrip("/")
        base = f"{self.base_url}/{self.admin_proxy_path}/api/v2/admin"
        return f"{base}/{suffix}" if suffix else f"{base}/"

    def user_url(self, user_uuid: str, suffix: str = "") -> str:
        suffix = suffix.lstrip("/")
        base = f"{self.base_url}/{self.client_proxy_path}/{user_uuid}/api/v2/user"
        return f"{base}/{suffix}" if suffix else f"{base}/"

    def user_panel_url(self, user_uuid: str) -> str:
        return f"{self.base_url}/{self.client_proxy_path}/{user_uuid}/"


class HiddifyClient:
    def __init__(self, config: HiddifyHostConfig):
        self.config = config
        self._session: aiohttp.ClientSession | None = None

    async def __aenter__(self) -> "HiddifyClient":
        self._session = aiohttp.ClientSession(timeout=REQUEST_TIMEOUT)
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        if self._session:
            await self._session.close()

    @property
    def session(self) -> aiohttp.ClientSession:
        if not self._session:
            raise RuntimeError("HiddifyClient session is not initialized")
        return self._session

    async def _request_json(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        json_body: dict[str, Any] | None = None,
        ok_statuses: tuple[int, ...] = (200,),
        allow_missing: bool = False,
    ) -> Any:
        try:
            async with self.session.request(method, url, headers=headers, json=json_body) as response:
                text = await response.text()
                if allow_missing and response.status == 404:
                    return None
                if response.status not in ok_statuses:
                    raise HiddifyPanelError(
                        f"{method} {url} returned {response.status}: {text[:300]}"
                    )
                if not text.strip():
                    return None
                try:
                    return await response.json(content_type=None)
                except Exception as exc:
                    raise HiddifyPanelError(
                        f"{method} {url} returned non-JSON response: {text[:300]}"
                    ) from exc
        except asyncio.TimeoutError as exc:
            raise HiddifyPanelError(f"Timeout while calling Hiddify API: {url}") from exc
        except aiohttp.ClientError as exc:
            raise HiddifyPanelError(f"HTTP error while calling Hiddify API: {url} ({exc})") from exc

    def _admin_headers(self) -> dict[str, str]:
        return {
            "Accept": "application/json",
            "Content-Type": "application/json",
            "Hiddify-API-Key": self.config.api_key,
        }

    async def list_users(self) -> list[dict[str, Any]]:
        data = await self._request_json(
            "GET",
            self.config.admin_url("user/"),
            headers=self._admin_headers(),
            allow_missing=True,
        )
        return data or []

    async def get_user(self, user_uuid: str) -> dict[str, Any] | None:
        return await self._request_json(
            "GET",
            self.config.admin_url(f"user/{user_uuid}/"),
            headers=self._admin_headers(),
            allow_missing=True,
        )

    async def create_user(self, payload: dict[str, Any]) -> dict[str, Any]:
        return await self._request_json(
            "POST",
            self.config.admin_url("user/"),
            headers=self._admin_headers(),
            json_body=payload,
            ok_statuses=(200, 201),
        )

    async def update_user(self, user_uuid: str, payload: dict[str, Any]) -> dict[str, Any]:
        return await self._request_json(
            "PATCH",
            self.config.admin_url(f"user/{user_uuid}/"),
            headers=self._admin_headers(),
            json_body=payload,
            ok_statuses=(200,),
        )

    async def delete_user(self, user_uuid: str) -> bool:
        result = await self._request_json(
            "DELETE",
            self.config.admin_url(f"user/{user_uuid}/"),
            headers=self._admin_headers(),
            ok_statuses=(200, 204),
            allow_missing=True,
        )
        return result is None or bool(result)

    async def get_user_profile(self, user_uuid: str) -> dict[str, Any] | None:
        return await self._request_json(
            "GET",
            self.config.user_url(user_uuid, "me/"),
            allow_missing=True,
        )

    async def get_user_configs(self, user_uuid: str) -> list[dict[str, Any]]:
        data = await self._request_json(
            "GET",
            self.config.user_url(user_uuid, "all-configs/"),
            allow_missing=True,
        )
        return data or []


@dataclass(frozen=True)
class MarzbanHostConfig:
    host_name: str
    base_url: str
    username: str
    password: str
    subscription_url: str | None = None

    def api_url(self, suffix: str = "") -> str:
        suffix = suffix.lstrip("/")
        base = f"{self.base_url}/api"
        return f"{base}/{suffix}" if suffix else f"{base}/"


class MarzbanClient:
    def __init__(self, config: MarzbanHostConfig):
        self.config = config
        self._session: aiohttp.ClientSession | None = None
        self._access_token: str | None = None

    async def __aenter__(self) -> "MarzbanClient":
        self._session = aiohttp.ClientSession(timeout=REQUEST_TIMEOUT)
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        if self._session:
            await self._session.close()

    @property
    def session(self) -> aiohttp.ClientSession:
        if not self._session:
            raise RuntimeError("MarzbanClient session is not initialized")
        return self._session

    async def _request_json(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        json_body: dict[str, Any] | None = None,
        data_body: dict[str, Any] | None = None,
        ok_statuses: tuple[int, ...] = (200,),
        allow_missing: bool = False,
    ) -> Any:
        try:
            async with self.session.request(
                method,
                url,
                headers=headers,
                json=json_body,
                data=data_body,
            ) as response:
                text = await response.text()
                if allow_missing and response.status == 404:
                    return None
                if response.status not in ok_statuses:
                    raise HiddifyPanelError(
                        f"{method} {url} returned {response.status}: {text[:300]}"
                    )
                if not text.strip():
                    return None
                try:
                    return await response.json(content_type=None)
                except Exception as exc:
                    raise HiddifyPanelError(
                        f"{method} {url} returned non-JSON response: {text[:300]}"
                    ) from exc
        except asyncio.TimeoutError as exc:
            raise HiddifyPanelError(f"Timeout while calling Marzban API: {url}") from exc
        except aiohttp.ClientError as exc:
            raise HiddifyPanelError(f"HTTP error while calling Marzban API: {url} ({exc})") from exc

    async def _ensure_token(self) -> str:
        if self._access_token:
            return self._access_token
        payload = {
            "username": self.config.username,
            "password": self.config.password,
            "grant_type": "password",
        }
        token_data = await self._request_json(
            "POST",
            self.config.api_url("admin/token"),
            data_body=payload,
            ok_statuses=(200,),
        )
        token = str((token_data or {}).get("access_token") or "").strip()
        if not token:
            raise HiddifyPanelError("Marzban token response does not include access_token")
        self._access_token = token
        return token

    async def _auth_headers(self) -> dict[str, str]:
        token = await self._ensure_token()
        return {
            "Accept": "application/json",
            "Authorization": f"Bearer {token}",
        }

    async def list_users(self) -> list[dict[str, Any]]:
        data = await self._request_json(
            "GET",
            self.config.api_url("users"),
            headers=await self._auth_headers(),
            allow_missing=True,
        )
        if isinstance(data, dict):
            users = data.get("users") or []
            return users if isinstance(users, list) else []
        return data if isinstance(data, list) else []

    async def get_user(self, username: str) -> dict[str, Any] | None:
        if not username:
            return None
        return await self._request_json(
            "GET",
            self.config.api_url(f"user/{quote(username, safe='')}"),
            headers=await self._auth_headers(),
            allow_missing=True,
        )

    async def create_user(self, payload: dict[str, Any]) -> dict[str, Any]:
        return await self._request_json(
            "POST",
            self.config.api_url("user"),
            headers=await self._auth_headers(),
            json_body=payload,
            ok_statuses=(200, 201),
        )

    async def update_user(self, username: str, payload: dict[str, Any]) -> dict[str, Any]:
        return await self._request_json(
            "PUT",
            self.config.api_url(f"user/{quote(username, safe='')}"),
            headers=await self._auth_headers(),
            json_body=payload,
            ok_statuses=(200,),
        )

    async def delete_user(self, username: str) -> bool:
        result = await self._request_json(
            "DELETE",
            self.config.api_url(f"user/{quote(username, safe='')}"),
            headers=await self._auth_headers(),
            ok_statuses=(200, 204),
            allow_missing=True,
        )
        return result is None or bool(result)

    async def get_inbounds(self) -> dict[str, Any]:
        data = await self._request_json(
            "GET",
            self.config.api_url("inbounds"),
            headers=await self._auth_headers(),
            allow_missing=True,
        )
        return data if isinstance(data, dict) else {}


def _looks_like_uuid(value: str | None) -> bool:
    if not value:
        return False
    try:
        uuid_lib.UUID(str(value).strip())
        return True
    except Exception:
        return False


def _normalize_base_url(raw_url: str) -> tuple[str, str | None, str | None]:
    raw_url = (raw_url or "").strip()
    if not raw_url:
        raise HiddifyPanelError("Host URL is empty")

    parsed = urlparse(raw_url if "://" in raw_url else f"https://{raw_url}")
    if not parsed.netloc:
        raise HiddifyPanelError(f"Invalid host URL: {raw_url}")

    segments = [segment for segment in parsed.path.split("/") if segment]
    derived_proxy_path: str | None = None
    derived_api_key: str | None = None

    if len(segments) >= 2 and _looks_like_uuid(segments[-1]):
        derived_proxy_path = segments[-2]
        derived_api_key = segments[-1]
    elif len(segments) == 1:
        derived_proxy_path = segments[0]

    return f"{parsed.scheme}://{parsed.netloc}".rstrip("/"), derived_proxy_path, derived_api_key


def _resolve_host_config(host_data: dict[str, Any]) -> HiddifyHostConfig:
    host_name = str(host_data.get("host_name") or "").strip()
    base_url, derived_proxy_path, derived_api_key = _normalize_base_url(str(host_data.get("host_url") or ""))

    raw_api_key = (
        host_data.get("host_api_key")
        or derived_api_key
        or (host_data.get("host_username") if _looks_like_uuid(host_data.get("host_username")) else None)
    )
    raw_admin_proxy_path = (
        host_data.get("host_proxy_path")
        or derived_proxy_path
        or (str(host_data.get("host_pass") or "").strip().strip("/") or None)
    )
    raw_client_proxy_path = (
        host_data.get("host_client_proxy_path")
        or None
    )

    api_key = str(raw_api_key or "").strip()
    admin_proxy_path = str(raw_admin_proxy_path or "").strip().strip("/")
    client_proxy_path = str(raw_client_proxy_path or admin_proxy_path).strip().strip("/")

    if not admin_proxy_path:
        raise HiddifyPanelError(
            f"Host '{host_name}' is missing Hiddify proxy path. Fill 'host_proxy_path' or use a full admin URL."
        )
    if not api_key or not _looks_like_uuid(api_key):
        raise HiddifyPanelError(
            f"Host '{host_name}' is missing a valid Hiddify API key (admin UUID)."
        )

    subscription_url = (str(host_data.get("subscription_url") or "").strip() or None)
    return HiddifyHostConfig(
        host_name=host_name,
        base_url=base_url,
        api_key=api_key,
        admin_proxy_path=admin_proxy_path,
        client_proxy_path=client_proxy_path,
        subscription_url=subscription_url,
    )


def _resolve_marzban_config(host_data: dict[str, Any]) -> MarzbanHostConfig:
    host_name = str(host_data.get("host_name") or "").strip()
    base_url, _derived_proxy_path, _derived_api_key = _normalize_base_url(str(host_data.get("host_url") or ""))
    username = str(host_data.get("host_username") or "").strip()
    password = str(host_data.get("host_pass") or "")
    if not username:
        raise HiddifyPanelError(
            f"Host '{host_name}' is missing Marzban admin username."
        )
    if not password:
        raise HiddifyPanelError(
            f"Host '{host_name}' is missing Marzban admin password."
        )
    subscription_url = (str(host_data.get("subscription_url") or "").strip() or None)
    return MarzbanHostConfig(
        host_name=host_name,
        base_url=base_url,
        username=username,
        password=password,
        subscription_url=subscription_url,
    )


def _get_host_panel_type(host_data: dict[str, Any] | None) -> str:
    panel_type = str((host_data or {}).get("panel_type") or "").strip().lower()
    if panel_type in ("marzban", "hiddify"):
        return panel_type
    return "hiddify"


async def probe_host_panel_connection(
    *,
    panel_type: str,
    host_name: str,
    host_url: str,
    host_username: str | None = None,
    host_pass: str | None = None,
    host_api_key: str | None = None,
    host_proxy_path: str | None = None,
    host_client_proxy_path: str | None = None,
) -> tuple[bool, str]:
    """Validate panel connectivity using provided credentials and API endpoint."""
    normalized_panel_type = str(panel_type or "hiddify").strip().lower()
    if normalized_panel_type not in ("hiddify", "marzban"):
        normalized_panel_type = "hiddify"

    host_data: dict[str, Any] = {
        "host_name": host_name,
        "host_url": host_url,
        "host_username": host_username,
        "host_pass": host_pass,
        "host_api_key": host_api_key,
        "host_proxy_path": host_proxy_path,
        "host_client_proxy_path": host_client_proxy_path,
        "panel_type": normalized_panel_type,
    }

    try:
        if normalized_panel_type == "marzban":
            config = _resolve_marzban_config(host_data)
            async with MarzbanClient(config) as client:
                await client.list_users()
            return True, "ok"

        config = _resolve_host_config(host_data)
        async with HiddifyClient(config) as client:
            await client.list_users()
        return True, "ok"
    except Exception as exc:
        return False, str(exc)


def login_to_host(host_url: str, username: str, password: str, inbound_id: int):
    try:
        config = _resolve_host_config(
            {
                "host_name": host_url,
                "host_url": host_url,
                "host_api_key": username,
                "host_proxy_path": password,
                "host_username": username,
                "host_pass": password,
                "host_inbound_id": inbound_id,
            }
        )
        return config, None
    except Exception as exc:
        logger.error("Hiddify host config resolution failed for '%s': %s", host_url, exc)
        return None, None


def _user_expiry_datetime(user_data: dict[str, Any] | None) -> datetime | None:
    if not user_data:
        return None

    # Marzban stores expiration as UTC timestamp in seconds.
    expire_raw = user_data.get("expire")
    if expire_raw is not None:
        try:
            expire_seconds = int(expire_raw)
            if expire_seconds > 0:
                return datetime.fromtimestamp(expire_seconds)
            return None
        except Exception:
            pass

    # Common ms-based fallback.
    expiry_ms = user_data.get("expiry_timestamp_ms") or user_data.get("expiry_time")
    if expiry_ms is not None:
        try:
            return datetime.fromtimestamp(int(expiry_ms) / 1000)
        except Exception:
            pass

    # Hiddify-style start_date + package_days.
    start_date_raw = user_data.get("start_date")
    if not start_date_raw:
        return None

    try:
        start_date = datetime.fromisoformat(str(start_date_raw)).date()
        package_days = int(user_data.get("package_days") or 0)
    except Exception:
        return None

    expiry_date = start_date + timedelta(days=max(package_days, 0))
    return datetime.combine(expiry_date, time(23, 59, 59))


def get_user_expiry_timestamp_ms(user_data: dict[str, Any] | None) -> int | None:
    expiry_dt = _user_expiry_datetime(user_data)
    if not expiry_dt:
        return None
    return int(expiry_dt.timestamp() * 1000)


def _choose_connection_link(config_items: list[dict[str, Any]], profile_url: str | None) -> str | None:
    for preferred_name in PREFERRED_CONFIG_NAMES:
        for item in config_items:
            if str(item.get("name") or "").strip() == preferred_name and item.get("link"):
                return str(item["link"]).strip()

    for item in config_items:
        link = str(item.get("link") or "").strip()
        if link:
            return link

    return (profile_url or "").strip() or None


def _apply_subscription_override(
    template: str | None,
    *,
    user_uuid: str,
    profile_url: str | None,
    derived_link: str | None,
    username: str | None = None,
) -> str | None:
    if not template:
        return derived_link or profile_url

    result = template
    replacements = {
        "{uuid}": user_uuid,
        "{user_uuid}": user_uuid,
        "{token}": user_uuid,
        "{username}": username or user_uuid,
        "{profile_url}": profile_url or "",
        "{subscription_url}": derived_link or "",
    }
    for placeholder, value in replacements.items():
        result = result.replace(placeholder, value)
    return result


def _build_fallback_link(config: HiddifyHostConfig, user_uuid: str) -> str:
    return config.user_panel_url(user_uuid)


def _compute_target_expiry(
    existing_user: dict[str, Any] | None,
    *,
    days_to_add: int | None,
    expiry_timestamp_ms: int | None,
) -> datetime:
    now = datetime.now()
    if expiry_timestamp_ms is not None:
        return datetime.fromtimestamp(int(expiry_timestamp_ms) / 1000)
    if days_to_add is None:
        raise HiddifyPanelError("Either days_to_add or expiry_timestamp_ms must be provided")

    current_expiry = _user_expiry_datetime(existing_user)
    is_active = True
    if existing_user:
        if "enable" in existing_user:
            is_active = bool(existing_user.get("enable"))
        elif "status" in existing_user:
            is_active = str(existing_user.get("status") or "").strip().lower() in ("active", "on_hold", "limited")
    if current_expiry and is_active and current_expiry > now:
        return current_expiry + timedelta(days=days_to_add)
    return now + timedelta(days=days_to_add)


def _build_user_payload(
    user_uuid: str,
    email: str,
    target_expiry_dt: datetime,
    existing_user: dict[str, Any] | None,
    minimum_package_days: int = 0,
) -> dict[str, Any]:
    today = datetime.now().date()
    target_date = target_expiry_dt.date()
    package_days = max((target_date - today).days, int(minimum_package_days or 0), 0)

    payload: dict[str, Any] = {
        "uuid": user_uuid,
        "name": email,
        "usage_limit_GB": float(existing_user.get("usage_limit_GB") or DEFAULT_USAGE_LIMIT_GB) if existing_user else DEFAULT_USAGE_LIMIT_GB,
        "package_days": package_days,
        "mode": str(existing_user.get("mode") or "no_reset") if existing_user else "no_reset",
        "start_date": today.isoformat(),
        "enable": target_date >= today,
    }

    if existing_user and existing_user.get("current_usage_GB") is not None:
        payload["current_usage_GB"] = existing_user["current_usage_GB"]
    if existing_user and existing_user.get("added_by_uuid"):
        payload["added_by_uuid"] = existing_user["added_by_uuid"]

    return payload


async def _find_existing_hiddify_user(
    client: HiddifyClient,
    *,
    email: str,
    stored_uuid: str | None,
) -> dict[str, Any] | None:
    if stored_uuid:
        user = await client.get_user(stored_uuid)
        if user:
            return user

    users = await client.list_users()
    for user in users:
        if str(user.get("name") or "").strip() == email:
            return user
    return None


def _normalize_marzban_user(user_data: dict[str, Any] | None) -> dict[str, Any] | None:
    if not isinstance(user_data, dict):
        return None
    normalized = dict(user_data)
    username = str(normalized.get("username") or normalized.get("name") or "").strip()
    if username:
        normalized["username"] = username
        normalized.setdefault("name", username)
        normalized.setdefault("uuid", username)
    expire_raw = normalized.get("expire")
    try:
        expire_seconds = int(expire_raw)
        if expire_seconds > 0:
            normalized.setdefault("expiry_timestamp_ms", expire_seconds * 1000)
    except Exception:
        pass
    return normalized


async def _find_existing_marzban_user(
    client: MarzbanClient,
    *,
    email: str,
    stored_identifier: str | None,
) -> dict[str, Any] | None:
    if stored_identifier:
        user = _normalize_marzban_user(await client.get_user(stored_identifier))
        if user:
            return user

    user_by_email = _normalize_marzban_user(await client.get_user(email))
    if user_by_email:
        return user_by_email

    users = await client.list_users()
    for user in users:
        normalized = _normalize_marzban_user(user)
        if not normalized:
            continue
        username = str(normalized.get("username") or "").strip()
        name = str(normalized.get("name") or "").strip()
        if username == email or name == email:
            return normalized
    return None


def _extract_marzban_inbounds(raw_inbounds: dict[str, Any]) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    if not isinstance(raw_inbounds, dict):
        return result

    for protocol, items in raw_inbounds.items():
        protocol_name = str(protocol or "").strip().lower()
        if not protocol_name or not isinstance(items, list):
            continue
        tags: list[str] = []
        for item in items:
            tag = ""
            if isinstance(item, dict):
                tag = str(item.get("tag") or "").strip()
            elif isinstance(item, str):
                tag = item.strip()
            if tag and tag not in tags:
                tags.append(tag)
        if tags:
            result[protocol_name] = tags
    return result


def _select_default_marzban_profile(
    inbounds_by_protocol: dict[str, list[str]],
) -> tuple[dict[str, Any], dict[str, list[str]]]:
    for preferred in ("vless", "trojan", "vmess", "shadowsocks"):
        tags = inbounds_by_protocol.get(preferred) or []
        if tags:
            return {preferred: {}}, {preferred: tags}
    for protocol, tags in inbounds_by_protocol.items():
        if tags:
            return {protocol: {}}, {protocol: tags}
    return {}, {}


def _build_marzban_user_payload(
    username: str,
    target_expiry_dt: datetime,
    existing_user: dict[str, Any] | None,
    inbounds_by_protocol: dict[str, list[str]],
    *,
    include_username: bool,
) -> dict[str, Any]:
    default_proxies, default_inbounds = _select_default_marzban_profile(inbounds_by_protocol)
    proxies = (existing_user or {}).get("proxies") or default_proxies
    inbounds = (existing_user or {}).get("inbounds") or default_inbounds
    if not proxies:
        raise HiddifyPanelError(
            "Marzban panel has no enabled inbounds/protocols for user creation."
        )

    expire_seconds = int(target_expiry_dt.timestamp())
    payload: dict[str, Any] = {
        "status": "active" if target_expiry_dt > datetime.now() else "disabled",
        "expire": expire_seconds if expire_seconds > 0 else 0,
        "data_limit": int((existing_user or {}).get("data_limit") or 0),
        "data_limit_reset_strategy": str(
            (existing_user or {}).get("data_limit_reset_strategy") or "no_reset"
        ),
        "proxies": proxies,
        "inbounds": inbounds,
        "note": (existing_user or {}).get("note"),
    }
    if include_username:
        payload["username"] = username
    return payload


def _build_marzban_fallback_link(config: MarzbanHostConfig, username: str) -> str:
    return f"{config.base_url}/sub/{quote(username, safe='')}"


def _build_marzban_connection_string(
    config: MarzbanHostConfig,
    user_data: dict[str, Any] | None,
    *,
    username: str,
) -> str | None:
    normalized = _normalize_marzban_user(user_data) or {}
    sub_url = str(normalized.get("subscription_url") or "").strip() or None
    links = normalized.get("links") if isinstance(normalized.get("links"), list) else []
    first_link = None
    for link in links or []:
        text = str(link or "").strip()
        if text:
            first_link = text
            break
    base_link = sub_url or first_link or _build_marzban_fallback_link(config, username)
    return _apply_subscription_override(
        config.subscription_url,
        user_uuid=username,
        username=username,
        profile_url=sub_url,
        derived_link=base_link,
    ) or base_link


def _coerce_bytes_value(value: Any) -> int | None:
    if value is None or value == "":
        return None
    try:
        numeric = float(value)
    except Exception:
        return None
    if numeric < 0:
        return None
    return int(numeric)


def _gb_to_bytes(value: Any) -> int | None:
    if value is None or value == "":
        return None
    try:
        numeric = float(value)
    except Exception:
        return None
    if numeric < 0:
        return None
    return int(numeric * 1024 * 1024 * 1024)


def _first_not_none(*values: Any) -> Any:
    for value in values:
        if value is not None:
            return value
    return None


async def _get_connection_details(
    client: HiddifyClient,
    user_uuid: str,
) -> tuple[str | None, str | None]:
    configs_result, profile_result = await asyncio.gather(
        client.get_user_configs(user_uuid),
        client.get_user_profile(user_uuid),
        return_exceptions=True,
    )

    config_items = configs_result if isinstance(configs_result, list) else []
    profile_data = profile_result if isinstance(profile_result, dict) else {}
    profile_url = str(profile_data.get("profile_url") or "").strip() or None
    derived_link = _choose_connection_link(config_items, profile_url)
    connection_string = _apply_subscription_override(
        client.config.subscription_url,
        user_uuid=user_uuid,
        profile_url=profile_url,
        derived_link=derived_link,
    )
    return connection_string or _build_fallback_link(client.config, user_uuid), profile_url


async def get_key_runtime_state(key_data: dict[str, Any]) -> dict[str, Any] | None:
    host_name = str(key_data.get("host_name") or "").strip()
    if not host_name:
        return None

    host_data = get_host(host_name)
    if not host_data:
        return None

    identifier = str(
        key_data.get("xui_client_uuid")
        or key_data.get("key_email")
        or ""
    ).strip()
    if not identifier:
        return None

    panel_type = _get_host_panel_type(host_data)
    key_email = str(key_data.get("key_email") or "").strip()
    try:
        if panel_type == "marzban":
            config = _resolve_marzban_config(host_data)
            async with MarzbanClient(config) as client:
                user = _normalize_marzban_user(await client.get_user(identifier))
                if not user and key_email and key_email != identifier:
                    user = _normalize_marzban_user(await client.get_user(key_email))
                if not user:
                    return None
                username = str((user or {}).get("username") or identifier).strip() or identifier
                connection_string = _build_marzban_connection_string(config, user, username=username)
                return {
                    "panel_type": "marzban",
                    "host_name": host_name,
                    "connection_string": connection_string or _build_marzban_fallback_link(config, username),
                    "expiry_timestamp_ms": get_user_expiry_timestamp_ms(user),
                    "used_bytes": _coerce_bytes_value(user.get("used_traffic")),
                    "limit_bytes": _coerce_bytes_value(user.get("data_limit")),
                    "is_enabled": str(user.get("status") or "").strip().lower() == "active",
                    "status": str(user.get("status") or "").strip().lower() or "unknown",
                    "server_url": config.base_url,
                }

        config = _resolve_host_config(host_data)
        async with HiddifyClient(config) as client:
            user = await client.get_user(identifier)
            if not user and key_email:
                user = await _find_existing_hiddify_user(client, email=key_email, stored_uuid=identifier)
            if not user:
                return None
            user_uuid = str(user.get("uuid") or identifier).strip() or identifier
            connection_string, profile_url = await _get_connection_details(client, user_uuid)
            return {
                "panel_type": "hiddify",
                "host_name": host_name,
                "connection_string": connection_string or profile_url or _build_fallback_link(config, user_uuid),
                "expiry_timestamp_ms": get_user_expiry_timestamp_ms(user),
                "used_bytes": _first_not_none(
                    _gb_to_bytes(user.get("current_usage_GB")),
                    _coerce_bytes_value(user.get("current_usage_bytes")),
                    _coerce_bytes_value(user.get("current_usage_B")),
                ),
                "limit_bytes": _first_not_none(
                    _gb_to_bytes(user.get("usage_limit_GB")),
                    _coerce_bytes_value(user.get("usage_limit_bytes")),
                    _coerce_bytes_value(user.get("usage_limit_B")),
                ),
                "is_enabled": bool(user.get("enable", True)),
                "status": "active" if bool(user.get("enable", True)) else "disabled",
                "server_url": config.base_url,
            }
    except Exception as exc:
        logger.error(
            "Failed to fetch runtime state for key %s on host '%s': %s",
            key_data.get("key_id"),
            host_name,
            exc,
            exc_info=True,
        )
        return None


async def _create_or_update_key_on_hiddify(
    host_name: str,
    host_data: dict[str, Any],
    email: str,
    days_to_add: int | None,
    expiry_timestamp_ms: int | None,
    minimum_package_days: int,
) -> dict[str, Any] | None:
    config = _resolve_host_config(host_data)
    stored_key = get_key_by_email(email) or {}
    stored_uuid = str(stored_key.get("xui_client_uuid") or "").strip() or None

    async with HiddifyClient(config) as client:
        existing_user = await _find_existing_hiddify_user(client, email=email, stored_uuid=stored_uuid)
        target_expiry_dt = _compute_target_expiry(
            existing_user,
            days_to_add=days_to_add,
            expiry_timestamp_ms=expiry_timestamp_ms,
        )
        user_uuid = str((existing_user or {}).get("uuid") or stored_uuid or uuid_lib.uuid4())
        payload = _build_user_payload(
            user_uuid,
            email,
            target_expiry_dt,
            existing_user,
            minimum_package_days=minimum_package_days,
        )

        if existing_user:
            response_user = await client.update_user(user_uuid, payload)
        else:
            response_user = await client.create_user(payload)

        final_user = response_user or await client.get_user(user_uuid) or payload
        final_expiry_ms = get_user_expiry_timestamp_ms(final_user) or int(target_expiry_dt.timestamp() * 1000)
        connection_string, _profile_url = await _get_connection_details(client, user_uuid)
        return {
            "client_uuid": user_uuid,
            "email": email,
            "expiry_timestamp_ms": final_expiry_ms,
            "connection_string": connection_string or _build_fallback_link(config, user_uuid),
            "host_name": host_name,
        }


async def _create_or_update_key_on_marzban(
    host_name: str,
    host_data: dict[str, Any],
    email: str,
    days_to_add: int | None,
    expiry_timestamp_ms: int | None,
) -> dict[str, Any] | None:
    config = _resolve_marzban_config(host_data)
    stored_key = get_key_by_email(email) or {}
    stored_identifier = str(stored_key.get("xui_client_uuid") or "").strip() or None

    async with MarzbanClient(config) as client:
        existing_user = await _find_existing_marzban_user(
            client,
            email=email,
            stored_identifier=stored_identifier,
        )
        target_expiry_dt = _compute_target_expiry(
            existing_user,
            days_to_add=days_to_add,
            expiry_timestamp_ms=expiry_timestamp_ms,
        )

        username = str((existing_user or {}).get("username") or stored_identifier or email).strip() or email
        inbounds_by_protocol = _extract_marzban_inbounds(await client.get_inbounds())
        payload = _build_marzban_user_payload(
            username=username,
            target_expiry_dt=target_expiry_dt,
            existing_user=existing_user,
            inbounds_by_protocol=inbounds_by_protocol,
            include_username=not bool(existing_user),
        )

        if existing_user:
            response_user = await client.update_user(username, payload)
        else:
            response_user = await client.create_user(payload)

        final_user = _normalize_marzban_user(response_user) or _normalize_marzban_user(await client.get_user(username)) or payload
        final_username = str((final_user or {}).get("username") or username).strip() or username
        final_expiry_ms = get_user_expiry_timestamp_ms(final_user) or int(target_expiry_dt.timestamp() * 1000)
        connection_string = _build_marzban_connection_string(config, final_user, username=final_username)
        return {
            "client_uuid": final_username,
            "email": email,
            "expiry_timestamp_ms": final_expiry_ms,
            "connection_string": connection_string or _build_marzban_fallback_link(config, final_username),
            "host_name": host_name,
        }


async def create_or_update_key_on_host(
    host_name: str,
    email: str,
    days_to_add: int | None = None,
    expiry_timestamp_ms: int | None = None,
    minimum_package_days: int = 0,
) -> dict[str, Any] | None:
    host_data = get_host(host_name)
    if not host_data:
        logger.error("Host '%s' not found in the database.", host_name)
        return None

    panel_type = _get_host_panel_type(host_data)
    try:
        if panel_type == "marzban":
            return await _create_or_update_key_on_marzban(
                host_name=host_name,
                host_data=host_data,
                email=email,
                days_to_add=days_to_add,
                expiry_timestamp_ms=expiry_timestamp_ms,
            )
        return await _create_or_update_key_on_hiddify(
            host_name=host_name,
            host_data=host_data,
            email=email,
            days_to_add=days_to_add,
            expiry_timestamp_ms=expiry_timestamp_ms,
            minimum_package_days=minimum_package_days,
        )
    except Exception as exc:
        logger.error(
            "Failed to create/update %s user '%s' on host '%s': %s",
            panel_type,
            email,
            host_name,
            exc,
            exc_info=True,
        )
        return None


async def get_key_details_from_host(key_data: dict[str, Any]) -> dict[str, Any] | None:
    host_name = str(key_data.get("host_name") or "").strip()
    if not host_name:
        logger.error("Key %s has no host_name.", key_data.get("key_id"))
        return None

    host_data = get_host(host_name)
    if not host_data:
        logger.error("Host '%s' not found while loading key details.", host_name)
        return None

    panel_type = _get_host_panel_type(host_data)
    identifier = str(
        key_data.get("xui_client_uuid")
        or key_data.get("key_email")
        or ""
    ).strip()
    if not identifier:
        logger.error("Key %s has no stored user identifier.", key_data.get("key_id"))
        return None

    try:
        if panel_type == "marzban":
            config = _resolve_marzban_config(host_data)
            async with MarzbanClient(config) as client:
                user = _normalize_marzban_user(await client.get_user(identifier))
                if not user:
                    email = str(key_data.get("key_email") or "").strip()
                    if email and email != identifier:
                        user = _normalize_marzban_user(await client.get_user(email))
                username = str((user or {}).get("username") or identifier).strip() or identifier
                connection_string = _build_marzban_connection_string(config, user, username=username)
                return {
                    "connection_string": connection_string or _build_marzban_fallback_link(config, username),
                }

        config = _resolve_host_config(host_data)
        async with HiddifyClient(config) as client:
            connection_string, profile_url = await _get_connection_details(client, identifier)
            return {
                "connection_string": connection_string or profile_url or _build_fallback_link(config, identifier)
            }
    except Exception as exc:
        logger.error(
            "Failed to fetch key details for key %s from host '%s': %s",
            key_data.get("key_id"),
            host_name,
            exc,
            exc_info=True,
        )
        return None


async def delete_client_on_host(host_name: str, client_email: str) -> bool:
    host_data = get_host(host_name)
    if not host_data:
        logger.error("Host '%s' not found while deleting user '%s'.", host_name, client_email)
        return False

    key_data = get_key_by_email(client_email) or {}
    stored_uuid = str(key_data.get("xui_client_uuid") or "").strip() or None
    panel_type = _get_host_panel_type(host_data)

    try:
        if panel_type == "marzban":
            config = _resolve_marzban_config(host_data)
            async with MarzbanClient(config) as client:
                user = await _find_existing_marzban_user(
                    client,
                    email=client_email,
                    stored_identifier=stored_uuid,
                )
                if not user:
                    logger.info("User '%s' is already absent on host '%s'.", client_email, host_name)
                    return True
                username = str(user.get("username") or client_email).strip()
                deleted = await client.delete_user(username)
                if deleted:
                    logger.info("User '%s' deleted from host '%s'.", client_email, host_name)
                return deleted

        config = _resolve_host_config(host_data)
        async with HiddifyClient(config) as client:
            user = await _find_existing_hiddify_user(client, email=client_email, stored_uuid=stored_uuid)
            if not user:
                logger.info("User '%s' is already absent on host '%s'.", client_email, host_name)
                return True
            deleted = await client.delete_user(str(user.get("uuid")))
            if deleted:
                logger.info("User '%s' deleted from host '%s'.", client_email, host_name)
            return deleted
    except Exception as exc:
        logger.error(
            "Failed to delete %s user '%s' from host '%s': %s",
            panel_type,
            client_email,
            host_name,
            exc,
            exc_info=True,
        )
        return False


async def list_users_on_host(host_name: str) -> list[dict[str, Any]] | None:
    host_data = get_host(host_name)
    if not host_data:
        logger.error("Host '%s' not found while listing panel users.", host_name)
        return None

    panel_type = _get_host_panel_type(host_data)
    try:
        if panel_type == "marzban":
            config = _resolve_marzban_config(host_data)
            async with MarzbanClient(config) as client:
                users = await client.list_users()
                normalized: list[dict[str, Any]] = []
                for user in users:
                    fixed = _normalize_marzban_user(user)
                    if fixed:
                        normalized.append(fixed)
                return normalized

        config = _resolve_host_config(host_data)
        async with HiddifyClient(config) as client:
            return await client.list_users()
    except Exception as exc:
        logger.error("Failed to list %s users for host '%s': %s", panel_type, host_name, exc, exc_info=True)
        return None
