#!/usr/bin/env bash
# SABI TUNNEL - deploy receiver, installed as /usr/local/sbin/sabi-deploy by server/install.sh.
#
# GitHub Actions pipes a .tar.gz bundle into it over SSH. The CI key in ~sabi-ci/.ssh/authorized_keys is
# pinned to  command="sudo -n /usr/local/sbin/sabi-deploy",restrict  so that key can run nothing else.
# A bundle can hold any of:
#   apps/manifest-part.json + the files it lists   new app builds: checked (sha256, size, build number),
#                                                  copied into the download folder and merged into
#                                                  manifest.json, which the apps poll to update themselves
#   panel/                                         the web panel: backup, copy, restart, health check and an
#                                                  automatic rollback when the new panel does not come up
#   server/sabi-deploy.sh                          a new version of this script (installed last)
set -Eeuo pipefail
shopt -s nullglob
umask 022
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin LC_ALL=C.UTF-8

MAX_MB=400
PANEL_DIR=${SABI_PANEL_DIR:-/opt/ttpanel}
DL_DIR=${SABI_DL_DIR:-/var/lib/ttpanel/downloads}
SELF=${SABI_SELF:-/usr/local/sbin/sabi-deploy}
PANEL_URL=${SABI_PANEL_URL:-http://127.0.0.1:8080}
LOCK=${SABI_LOCK:-/run/sabi-deploy.lock}

log() { printf '[sabi-deploy] %s\n' "$*"; }
die() { printf '[sabi-deploy] ERROR: %s\n' "$*" >&2; exit 1; }
trap 'printf "[sabi-deploy] ERROR: failed at line %s\n" "$LINENO" >&2' ERR

[ "$(id -u)" -eq 0 ] || die "run as root (sudo)"
exec 9>"$LOCK"
flock -w 900 9 || die "another deploy is still running"

WORK=$(mktemp -d /var/tmp/sabi-deploy.XXXXXX)
trap 'rm -rf "$WORK"' EXIT
head -c $((MAX_MB * 1048576 + 1)) > "$WORK/bundle.tgz"
SIZE=$(stat -c %s "$WORK/bundle.tgz")
[ "$SIZE" -gt 0 ] || die "empty bundle (pipe a .tar.gz into this command)"
[ "$SIZE" -le $((MAX_MB * 1048576)) ] || die "bundle is larger than $MAX_MB MB"
log "bundle: $SIZE bytes, sha256 $(sha256sum "$WORK/bundle.tgz" | cut -c1-16)"

X=$WORK/x
mkdir "$X"
tar -xzf "$WORK/bundle.tgz" -C "$X" --no-same-owner --no-same-permissions 2>"$WORK/tar.err" \
  || { cat "$WORK/tar.err" >&2; die "not a valid .tar.gz"; }
[ -z "$(find "$X" ! -type f ! -type d -print -quit)" ] || die "bundle contains links or special files"
for e in "$X"/* "$X"/.[!.]*; do
  case "${e##*/}" in apps|panel|server) ;; *) log "ignoring unknown bundle entry ${e##*/}" ;; esac
done

# ------------------------------------------------------------------ app builds
deploy_apps() {
  [ -f "$X/apps/manifest-part.json" ] || die "apps/ has no manifest-part.json"
  install -d -m 755 "$DL_DIR"
  python3 - "$X/apps" "$DL_DIR" <<'PY'
import hashlib, json, os, re, shutil, sys, time

src, dl = sys.argv[1], sys.argv[2]
NUM = {"windows": "build", "android": "code"}     # the number each app compares with its own build


def fail(msg):
    print(f"[sabi-deploy] ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


try:
    part = json.load(open(os.path.join(src, "manifest-part.json"), encoding="utf-8"))
except ValueError as e:
    fail(f"manifest-part.json: {e}")
if not isinstance(part, dict) or not part:
    fail('manifest-part.json must look like {"windows": {...}} or {"android": {...}}')
man_path = os.path.join(dl, "manifest.json")
try:
    man = json.load(open(man_path, encoding="utf-8"))
    man = man if isinstance(man, dict) else {}
except (OSError, ValueError):
    man = {}

staged = {}
for plat, p in part.items():
    if plat not in NUM or not isinstance(p, dict):
        fail(f"unknown platform {plat!r}")
    key = NUM[plat]
    ver = str(p.get("version", ""))
    if not re.fullmatch(r"\d{1,4}(\.\d{1,5}){1,3}", ver):
        fail(f"{plat}: bad version {ver!r}")
    try:
        num = int(p.get(key))
    except (TypeError, ValueError):
        fail(f"{plat}: '{key}' must be a number")
    if num <= 0:
        fail(f"{plat}: '{key}' must be positive")
    have = int((man.get(plat) or {}).get(key) or 0)
    if have > num:
        print(f"[sabi-deploy] {plat}: the server already has {key} {have}, newer than {num}; skipped")
        continue
    files = {}
    for fkey, f in (p.get("files") or {}).items():
        name = str((f or {}).get("name", ""))
        if not re.fullmatch(r"[A-Za-z0-9._-]{1,40}", str(fkey)):
            fail(f"{plat}: bad file key {fkey!r}")
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}", name) or name == "manifest.json":
            fail(f"{plat}: bad file name {name!r}")
        path = os.path.join(src, name)
        if not os.path.isfile(path):
            fail(f"{name} is listed in manifest-part.json but missing from the bundle")
        digest, size = sha256(path), os.path.getsize(path)
        if digest != str(f.get("sha256", "")).lower():
            fail(f"{name}: sha256 does not match manifest-part.json")
        if "size" in f and int(f["size"]) != size:
            fail(f"{name}: size does not match manifest-part.json")
        files[fkey] = {"name": name, "sha256": digest, "size": size}
    if not files:
        fail(f"{plat}: no files")
    entry = {k: p[k] for k in ("notes", "commit", "min_build") if k in p}
    if "notes" in entry:
        entry["notes"] = str(entry["notes"])[:500]
    entry.update({"version": ver, key: num, "files": files, "published": int(time.time())})
    staged[plat] = entry

for plat, entry in staged.items():
    for f in entry["files"].values():
        tmp = os.path.join(dl, f".{f['name']}.part")
        shutil.copyfile(os.path.join(src, f["name"]), tmp)
        os.chmod(tmp, 0o644)
        os.replace(tmp, os.path.join(dl, f["name"]))
    man[plat] = entry
    k = NUM[plat]
    print(f"[sabi-deploy] {plat} {entry['version']} ({k} {entry[k]}): "
          + ", ".join(f"{f['name']} {f['size']} B" for f in entry["files"].values()))
if staged:
    tmp = man_path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(man, fh, ensure_ascii=False, indent=1)
    os.chmod(tmp, 0o644)
    os.replace(tmp, man_path)
PY
}

# ------------------------------------------------------------------ panel
panel_up() {   # wait until the panel answers on path $1
  local i
  for i in $(seq 1 25); do
    curl -fsS -m 2 -o /dev/null "$PANEL_URL$1" 2>/dev/null && return 0
    sleep 1
  done
  return 1
}

deploy_panel() {
  local P=$X/panel B=$PANEL_DIR/.bak-deploy py=$PANEL_DIR/venv/bin/python
  [ -f "$P/app.py" ] || die "panel/ has no app.py"
  [ -x "$py" ] || die "no panel venv in $PANEL_DIR (run server/install.sh first)"
  find "$P" -name __pycache__ -prune -exec rm -rf {} +
  "$py" - "$P" <<'PY' || die "the new panel code does not compile; nothing changed"
import ast, pathlib, sys
for f in pathlib.Path(sys.argv[1]).rglob("*.py"):
    ast.parse(f.read_text(encoding="utf-8"), str(f))
PY
  if [ -f "$P/requirements.txt" ] && ! cmp -s "$P/requirements.txt" "$PANEL_DIR/requirements.txt"; then
    log "panel: requirements.txt changed, installing packages"
    "$PANEL_DIR/venv/bin/pip" install -q --disable-pip-version-check -r "$P/requirements.txt" \
      || die "pip install failed; nothing changed"
  fi
  rm -rf "$B" && mkdir -p "$B"
  tar -C "$PANEL_DIR" --exclude=./venv --exclude='./.bak*' --exclude=./__pycache__ -cf - . | tar -C "$B" -xf -
  tar -C "$P" -cf - . | tar -C "$PANEL_DIR" -xf - --no-same-owner --no-overwrite-dir
  rm -rf "$PANEL_DIR/__pycache__"
  systemctl restart ttpanel
  if panel_up /app/v1/ping; then
    log "panel: new version is up"
    return 0
  fi
  log "panel: the new version did not come up; rolling back"
  journalctl -u ttpanel -n 30 --no-pager >&2 || true
  tar -C "$B" -cf - . | tar -C "$PANEL_DIR" -xf - --no-same-owner --no-overwrite-dir
  rm -rf "$PANEL_DIR/__pycache__"
  systemctl restart ttpanel
  if panel_up /; then die "panel rolled back to the previous version"; fi
  die "panel rolled back, but it is still not answering"
}

# ------------------------------------------------------------------ this script
deploy_self() {
  local S=$X/server/sabi-deploy.sh
  bash -n "$S" || die "server/sabi-deploy.sh has a syntax error"
  if cmp -s "$S" "$SELF"; then log "deploy script: unchanged"; return 0; fi
  install -m 755 -o root -g root "$S" "$SELF.new"
  mv -f "$SELF.new" "$SELF"
  log "deploy script: updated"
}

did=""
if [ -d "$X/apps" ]; then deploy_apps; did+=" apps"; fi
if [ -d "$X/panel" ]; then deploy_panel; did+=" panel"; fi
if [ -f "$X/server/sabi-deploy.sh" ]; then deploy_self; did+=" deploy-script"; fi
[ -n "$did" ] || die "nothing to deploy (expected apps/, panel/ or server/sabi-deploy.sh)"
log "done:$did"
