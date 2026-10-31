#!/usr/bin/env bash
# Sube el release al servidor por SSH y ejecuta setup-server.sh.
# Requiere: DEPLOY_HOST, DEPLOY_SSH_KEY (secretos del repo) y build/release ya armado.
set -euo pipefail

user="${DEPLOY_USER:-ubuntu}"
domain="$(echo "$DEPLOY_HOST" | tr . -).sslip.io"
key="$RUNNER_TEMP/deploy_key"

printf '%s\n' "$DEPLOY_SSH_KEY" | tr -d '\r' > "$key"
chmod 600 "$key"
ssh_opts=(-i "$key" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=20 -o ServerAliveInterval=15)

tar -czf release.tgz -C build/release .
scp "${ssh_opts[@]}" release.tgz deploy/setup-server.sh "$user@$DEPLOY_HOST:/tmp/"
ssh "${ssh_opts[@]}" "$user@$DEPLOY_HOST" "set -e
  rm -rf /tmp/distrito-release && mkdir -p /tmp/distrito-release
  tar -xzf /tmp/release.tgz -C /tmp/distrito-release
  sudo bash /tmp/setup-server.sh /tmp/distrito-release '$domain' '${GITHUB_SHA::7}'"

echo "DEPLOY_DOMAIN=$domain" >> "$GITHUB_ENV"
