#!/usr/bin/env python3
"""Upsert DNS-only (grey cloud) A/AAAA records for the TrustTunnel hosts.

TrustTunnel must NOT be proxied by Cloudflare: the CDN does not forward
HTTP CONNECT tunnels. Records are therefore created with proxied=false.

    CF_API_TOKEN=... python infra/cloudflare_dns.py --zone example.com \
        --host tt-oh --host ttpanel-oh --ipv4 1.2.3.4 --ipv6 2600::1
"""
import argparse
import json
import os
import urllib.request

API = "https://api.cloudflare.com/client/v4"


def call(method, path, body=None):
    headers = {"Content-Type": "application/json"}
    if os.environ.get("CF_API_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["CF_API_TOKEN"]
    req = urllib.request.Request(API + path, method=method, headers=headers,
                                 data=json.dumps(body).encode() if body is not None else None)
    with urllib.request.urlopen(req, timeout=30) as r:
        out = json.load(r)
    if not out.get("success"):
        raise SystemExit(out.get("errors"))
    return out["result"]


def upsert(zone_id, name, rtype, content):
    existing = call("GET", f"/zones/{zone_id}/dns_records?type={rtype}&name={name}")
    body = {"type": rtype, "name": name, "content": content, "ttl": 300, "proxied": False,
            "comment": "trusttunnel-stack"}
    if existing:
        call("PUT", f"/zones/{zone_id}/dns_records/{existing[0]['id']}", body)
        return "updated"
    call("POST", f"/zones/{zone_id}/dns_records", body)
    return "created"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--zone", required=True)
    ap.add_argument("--host", action="append", required=True, help="subdomain, repeatable")
    ap.add_argument("--ipv4")
    ap.add_argument("--ipv6")
    a = ap.parse_args()
    zone_id = call("GET", f"/zones?name={a.zone}")[0]["id"]
    for h in a.host:
        fqdn = f"{h}.{a.zone}"
        if a.ipv4:
            print(fqdn, "A", a.ipv4, upsert(zone_id, fqdn, "A", a.ipv4))
        if a.ipv6:
            print(fqdn, "AAAA", a.ipv6, upsert(zone_id, fqdn, "AAAA", a.ipv6))


if __name__ == "__main__":
    main()
