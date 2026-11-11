#!/usr/bin/env python3
"""Deploy the SABI TUNNEL feed mirror: a read-only Cloudflare Worker in front of the panel's /app/* routes.

    CF_API_TOKEN=... python3 infra/cloudflare_mirror.py --account <id> --zone example.com \
        --hostname sabitunnel.example.com --origin https://ttpanel-oh.example.com

Then add the mirror to the panel config so the apps learn it:  "feed_urls": ["https://<hostname>/app/v1/feed"]
(token needs Workers Scripts:Edit + Workers Routes/Custom Domains:Edit + Zone:Read).
"""
import argparse, json, os, pathlib, urllib.request, urllib.error, uuid

API = "https://api.cloudflare.com/client/v4"


def cf(method, path, body=None, raw=None, ctype="application/json"):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(API + path, data=data, method=method, headers={
        "Authorization": "Bearer " + os.environ["CF_API_TOKEN"], "Content-Type": ctype})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            res = json.loads(r.read())
    except urllib.error.HTTPError as e:
        res = json.loads(e.read() or b"{}")
    if not res.get("success"):
        raise SystemExit(f"{method} {path}: {res.get('errors')}")
    return res["result"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--account", required=True)
    ap.add_argument("--zone", required=True)
    ap.add_argument("--hostname", required=True)
    ap.add_argument("--origin", required=True, help="panel URL, e.g. https://ttpanel-oh.example.com")
    ap.add_argument("--name", default="sabi-tunnel-mirror")
    a = ap.parse_args()
    js = (pathlib.Path(__file__).parent / "cloudflare-mirror" / "worker.js").read_text()
    js = js.replace('const ORIGIN = "https://ttpanel-oh.sahebkarami.ir";', f"const ORIGIN = {json.dumps(a.origin.rstrip('/'))};")
    b = "----sabi" + uuid.uuid4().hex
    meta = {"main_module": "worker.js", "compatibility_date": "2024-11-01"}
    body = (f'--{b}\r\nContent-Disposition: form-data; name="metadata"; filename="metadata.json"\r\n'
            f"Content-Type: application/json\r\n\r\n{json.dumps(meta)}\r\n"
            f'--{b}\r\nContent-Disposition: form-data; name="worker.js"; filename="worker.js"\r\n'
            f"Content-Type: application/javascript+module\r\n\r\n{js}\r\n--{b}--\r\n").encode()
    cf("PUT", f"/accounts/{a.account}/workers/scripts/{a.name}", raw=body, ctype=f"multipart/form-data; boundary={b}")
    zone = cf("GET", f"/zones?name={a.zone}")[0]["id"]
    cf("PUT", f"/accounts/{a.account}/workers/domains",
       {"environment": "production", "hostname": a.hostname, "service": a.name, "zone_id": zone})
    print(f"mirror: https://{a.hostname}/app/v1/feed  ->  {a.origin}")


if __name__ == "__main__":
    main()
