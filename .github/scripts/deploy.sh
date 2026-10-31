#!/usr/bin/env bash
# Sube el release al servidor por SSH y ejecuta setup-server.sh.
# Requiere: DEPLOY_HOST, DEPLOY_SSH_KEY (secretos del repo) y build/release ya armado.
set -euo pipefail

host="${TARGET_HOST:-$DEPLOY_HOST}"
user="${TARGET_USER:-${DEPLOY_USER:-ubuntu}}"
domain="$(echo "$host" | tr . -).sslip.io"
key="$RUNNER_TEMP/deploy_key"

"$(dirname "$0")/prepare-key.sh" "$key"
ssh_opts=(-i "$key" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=20 -o ServerAliveInterval=15)

tar -czf release.tgz -C build/release .
scp "${ssh_opts[@]}" release.tgz deploy/setup-server.sh "$user@$host:/tmp/"
ssh "${ssh_opts[@]}" "$user@$host" "set -e
  rm -rf /tmp/distrito-release && mkdir -p /tmp/distrito-release
  tar -xzf /tmp/release.tgz -C /tmp/distrito-release
  sudo bash /tmp/setup-server.sh /tmp/distrito-release '$domain' '${GITHUB_SHA::7}'"

echo "DEPLOY_DOMAIN=$domain" >> "$GITHUB_ENV"
