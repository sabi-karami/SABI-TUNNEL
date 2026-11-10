"""Minimal tt://? deep-link encoder (see TrustTunnel DEEP_LINK.md).

Emits format version 1 so that every released TrustTunnel app can import it.
"""
import base64


def _varint(v: int) -> bytes:
    if v <= 0x3F:
        return v.to_bytes(1, "big")
    if v <= 0x3FFF:
        return (v | 0x4000).to_bytes(2, "big")
    if v <= 0x3FFFFFFF:
        return (v | 0x80000000).to_bytes(4, "big")
    return (v | 0xC000000000000000).to_bytes(8, "big")


def _tlv(tag: int, value: bytes) -> bytes:
    return _varint(tag) + _varint(len(value)) + value


def make_deeplink(hostname, addresses, username, password, name=None,
                  dns_upstreams=None, protocol="http2", has_ipv6=True, version=1):
    buf = _tlv(0x00, _varint(version))
    buf += _tlv(0x01, hostname.encode())
    for addr in addresses:
        buf += _tlv(0x02, addr.encode())
    if not has_ipv6:
        buf += _tlv(0x04, b"\x00")
    buf += _tlv(0x05, username.encode())
    buf += _tlv(0x06, password.encode())
    if protocol == "http3":
        buf += _tlv(0x09, b"\x02")
    if name:
        buf += _tlv(0x0C, name.encode())
    if dns_upstreams:
        arr = b"".join(_varint(len(d.encode())) + d.encode() for d in dns_upstreams)
        buf += _tlv(0x0D, arr)
    return "tt://?" + base64.urlsafe_b64encode(buf).rstrip(b"=").decode()
