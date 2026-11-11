#!/usr/bin/env bash
# One-command deploy of a SABI TUNNEL server (TrustTunnel endpoint + web panel) on AWS EC2 (dual-stack)
# with Cloudflare DNS.
#
#   export AWS_PROFILE=...            # or AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY
#   export CF_API_TOKEN=...           # Cloudflare token with Zone:DNS:Edit
#   ./deploy.sh --region us-east-2 --code oh --zone example.com [--ci-pubkey ~/.ssh/sabi-ci.pub]
#
# Creates: EC2 t4g.micro + Elastic IP + IPv6, DNS records  tt-<code>.<zone>  and  ttpanel-<code>.<zone>,
# Let's Encrypt cert, TrustTunnel endpoint, and the panel. Safe to re-run (idempotent).
set -euo pipefail
REGION=us-east-2; CODE=oh; ZONE=""; NAME_PREFIX=tt; SERVER_NAME="SABI TUNNEL"; CI_PUBKEY=""; FIRST_USER=me
TYPE=t4g.micro; DISK=12; KEY="$HOME/.ssh/tt-stack_ed25519"
while [ $# -gt 0 ]; do case "$1" in
  --region) REGION=$2; shift 2;; --code) CODE=$2; shift 2;; --zone) ZONE=$2; shift 2;;
  --server-name) SERVER_NAME=$2; shift 2;; --first-user) FIRST_USER=$2; shift 2;;
  --ci-pubkey) CI_PUBKEY=$(cat "$2"); shift 2;;
  --instance-type) TYPE=$2; shift 2;; --disk-gb) DISK=$2; shift 2;; --key) KEY=$2; shift 2;;
  *) echo "unknown arg $1"; exit 1;; esac; done
[ -n "$ZONE" ] || { echo "--zone is required"; exit 1; }
: "${CF_API_TOKEN:?export CF_API_TOKEN}"
HERE="$(cd "$(dirname "$0")" && pwd)"
TT_HOST="tt-$CODE.$ZONE"; PANEL_HOST="ttpanel-$CODE.$ZONE"
log() { printf '\033[1;36m[deploy]\033[0m %s\n' "$*"; }

[ -f "$KEY" ] || ssh-keygen -t ed25519 -N "" -f "$KEY" -C tt-stack >/dev/null
python3 -c 'import boto3' 2>/dev/null || python3 -m pip install --user -q boto3

log "AWS: creating/reusing server in $REGION"
OUT=$(python3 "$HERE/infra/aws_deploy.py" --region "$REGION" --name "tt-$CODE" \
      --instance-type "$TYPE" --disk-gb "$DISK" --pubkey "$KEY.pub")
echo "$OUT"
IP4=$(echo "$OUT" | python3 -c 'import sys,json;print(json.load(sys.stdin)["ipv4"])')
IP6=$(echo "$OUT" | python3 -c 'import sys,json;print(json.load(sys.stdin)["ipv6"] or "")')

log "Cloudflare: DNS-only records for $TT_HOST and $PANEL_HOST"
python3 "$HERE/infra/cloudflare_dns.py" --zone "$ZONE" --host "tt-$CODE" --host "ttpanel-$CODE" \
  --ipv4 "$IP4" ${IP6:+--ipv6 "$IP6"}

SSH="ssh -i $KEY -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 ubuntu@$IP4"
log "Waiting for SSH"
for i in $(seq 1 30); do $SSH true 2>/dev/null && break; sleep 5; done

log "Uploading and running installer (takes ~3-5 minutes)"
tar -C "$HERE" --exclude=.git --exclude=__pycache__ -czf - . | $SSH 'rm -rf ~/tt-stack && mkdir ~/tt-stack && tar -C ~/tt-stack -xzf -'
$SSH "sudo env TT_HOST='$TT_HOST' PANEL_HOST='$PANEL_HOST' SERVER_NAME='$SERVER_NAME' REGION='$REGION' FIRST_USER='$FIRST_USER' CI_PUBKEY='$CI_PUBKEY' bash ~/tt-stack/server/install.sh"

log "Done. Panel: https://$PANEL_HOST   (admin password: $SSH sudo cat /root/ttpanel-admin.txt)"
