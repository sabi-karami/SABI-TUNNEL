#!/usr/bin/env bash
# Takes screenshots of the app on an Android emulator (used by CI).
# Usage: screens.sh <apk> <outdir>
set -uo pipefail
APK="$1"; OUT="$2"; PKG=com.sabi.trusttunnel
mkdir -p "$OUT"
DEMO_LINK='tt://?AAEBAQ5kZS5leGFtcGxlLmNvbQIQMjAzLjAuMTEzLjEwOjQ0MwUEZGVtbwYNZGVtby1wYXNzLTEyMwkBAgwS2YHYsdin2YbaqdmB2YjYsdiq'

# Emulator-only: dismiss "System UI isn't responding" style dialogs.
unstick() { for _ in 1 2 3; do tap_text '=Wait' >/dev/null 2>&1 || return 0; sleep 2; done; }
shot() { sleep "${2:-3}"; unstick; adb exec-out screencap -p > "$OUT/$1.png"; dump "$1"; echo "shot $1"; }
dump() { timeout 25 adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb pull /sdcard/ui.xml "$OUT/$1.xml" >/dev/null 2>&1 || true; }

# Taps the first node whose text or content-desc contains one of the given strings.
tap_text() {
  timeout 25 adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1
  timeout 15 adb pull /sdcard/ui.xml /tmp/ui.xml >/dev/null 2>&1 || return 1
  local xy
  xy=$(python3 - "$@" <<'PY'
import re, sys, xml.etree.ElementTree as ET
wanted = sys.argv[1:]
root = ET.parse('/tmp/ui.xml').getroot()
for w in wanted:
    exact = w.startswith('=')
    w = w.lstrip('=')
    for n in root.iter('node'):
        labels = [(n.get('text') or '').strip(), (n.get('content-desc') or '').strip()]
        if (w in labels) if exact else any(w in l for l in labels):
            x1, y1, x2, y2 = map(int, re.findall(r'\d+', n.get('bounds')))
            print((x1 + x2) // 2, (y1 + y2) // 2); sys.exit(0)
sys.exit(1)
PY
) || { echo "no node for: $*"; return 1; }
  echo "tap $* at $xy"; adb shell input tap $xy
}

adb shell settings put global window_animation_scale 1
# Let the emulator settle so System UI is responsive before the app starts.
sleep 40; unstick
adb shell settings put system screen_off_timeout 600000
adb install -r -g "$APK" || exit 1
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
shot 00-splash 3
shot 01-home-empty 25

adb shell am start -a android.intent.action.VIEW -d "$DEMO_LINK" "$PKG" >/dev/null 2>&1
shot 02-import-link 8
tap_text '=افزودن' '=ذخیره' || true
shot 03-home-server 6

# Tap the tunnel portal (connect). Android then asks for VPN permission.
tap_text '=اتصال' || true
shot 04-vpn-permission 4
tap_text '=OK' '=Allow' '=تأیید' || true
shot 05-connecting 2
shot 06-connecting-later 6

tap_text 'برگه ۲ از ۳' && shot 07-routing 3
tap_text 'برگه ۳ از ۳' && shot 08-settings 3
tap_text 'برگه ۱ از ۳' && shot 09-servers-again 3

adb logcat -d -t 400 > "$OUT/logcat.txt" 2>/dev/null || true
ls -la "$OUT"
