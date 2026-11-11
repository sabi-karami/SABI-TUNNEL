"""Minimal tt://? deep-link encoder / decoder (see TrustTunnel DEEP_LINK.md).

Emits format version 1 so that every released TrustTunnel-based app (SABI TUNNEL included) can import it.
"""
import base64

TAG_VERSION, TAG_HOST, TAG_ADDR, TAG_IPV6, TAG_USER, TAG_PASS, TAG_PROTO, TAG_NAME, TAG_DNS = (
    0x00, 0x01, 0x02, 0x04, 0x05, 0x06, 0x09, 0x0C, 0x0D)


def _varint(v: int) -> bytes:
    if v <= 0x3F:
        return v.to_bytes(1, "big")
    if v <= 0x3FFF:
        return (v | 0x4000).to_bytes(2, "big")
    if v <= 0x3FFFFFFF:
        return (v | 0x80000000).to_bytes(4, "big")
    return (v | 0xC000000000000000).to_bytes(8, "big")


def _read_varint(b: bytes, i: int):
    if i >= len(b):
        raise ValueError("link is cut off")
    n = 1 << (b[i] >> 6)
    if i + n > len(b):
        raise ValueError("truncated varint")
    v = b[i] & 0x3F
    for k in range(1, n):
        v = (v << 8) | b[i + k]
    return v, i + n


def _tlv(tag: int, value: bytes) -> bytes:
    return _varint(tag) + _varint(len(value)) + value


def make_deeplink(hostname, addresses, username, password, name=None,
                  dns_upstreams=None, protocol="http2", has_ipv6=True, version=1):
    buf = _tlv(TAG_VERSION, _varint(version))
    buf += _tlv(TAG_HOST, hostname.encode())
    for addr in addresses:
        buf += _tlv(TAG_ADDR, addr.encode())
    if not has_ipv6:
        buf += _tlv(TAG_IPV6, b"\x00")
    buf += _tlv(TAG_USER, username.encode())
    buf += _tlv(TAG_PASS, password.encode())
    if protocol == "http3":
        buf += _tlv(TAG_PROTO, b"\x02")
    if name:
        buf += _tlv(TAG_NAME, name.encode())
    if dns_upstreams:
        arr = b"".join(_varint(len(d.encode())) + d.encode() for d in dns_upstreams)
        buf += _tlv(TAG_DNS, arr)
    return "tt://?" + base64.urlsafe_b64encode(buf).rstrip(b"=").decode()


def _payload(link: str) -> bytes:
    s = (link or "").strip()
    if not s.lower().startswith("tt:"):
        raise ValueError("link must start with tt://")
    q = s[s.index("?") + 1:] if "?" in s else ""
    q = q.strip().rstrip("/").replace("+", "-").replace("/", "_").rstrip("=")
    if not q or len(q) % 4 == 1:
        raise ValueError("link is cut off")
    return base64.urlsafe_b64decode(q + "=" * (-len(q) % 4))


def _items(b: bytes):
    out, i = [], 0
    while i < len(b):
        tag, i = _read_varint(b, i)
        ln, i = _read_varint(b, i)
        if i + ln > len(b):
            raise ValueError("link is cut off")
        out.append((tag, b[i:i + ln]))
        i += ln
    return out


def parse_deeplink(link: str) -> dict:
    """Returns {"host", "addresses", "user", "name", "version"}; raises ValueError for broken links."""
    items = _items(_payload(link))
    info = {"host": "", "addresses": [], "user": "", "name": "", "version": 1}
    for tag, val in items:
        if tag == TAG_VERSION and val:
            info["version"] = _read_varint(val, 0)[0]
        elif tag == TAG_HOST:
            info["host"] = val.decode(errors="replace")
        elif tag == TAG_ADDR:
            info["addresses"].append(val.decode(errors="replace"))
        elif tag == TAG_USER:
            info["user"] = val.decode(errors="replace")
        elif tag == TAG_NAME:
            info["name"] = val.decode(errors="replace")
    if not info["host"] or not info["user"]:
        raise ValueError("link has no host name or user")
    return info


def with_name(link: str, name: str) -> str:
    """The same link with its display name (tag 0x0C) replaced in place; every other field is kept byte for byte."""
    items, done = [], False
    for t, v in _items(_payload(link)):
        if t == TAG_NAME:
            if name and not done:
                items.append((TAG_NAME, name.encode()))
            done = True
            continue
        items.append((t, v))
    if name and not done:
        items.append((TAG_NAME, name.encode()))
    buf = b"".join(_tlv(t, v) for t, v in items)
    return "tt://?" + base64.urlsafe_b64encode(buf).rstrip(b"=").decode()
