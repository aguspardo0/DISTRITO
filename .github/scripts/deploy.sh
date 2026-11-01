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
scp "${ssh_opts[@]}" release.tgz deploy/setup-server.sh deploy/backup-db.sh deploy/restore-db.sh "$user@$host:/tmp/"
ssh "${ssh_opts[@]}" "$user@$host" "set -e
  rm -rf /tmp/distrito-release && mkdir -p /tmp/distrito-release
  tar -xzf /tmp/release.tgz -C /tmp/distrito-release
  sudo bash /tmp/setup-server.sh /tmp/distrito-release '$domain' '${GITHUB_SHA::7}'"

# Estado de las copias de seguridad (queda como aviso en el resumen de la CI).
backups=$(ssh "${ssh_opts[@]}" "$user@$host" "systemctl is-active distrito-backup.timer; sudo ls -1t /var/backups/distrito | head -3; sudo du -sh /var/backups/distrito | cut -f1" || true)
echo "::notice title=Copias de seguridad::$(echo "$backups" | tr '\n' ' ')"
echo "$backups" | grep -q '^active$' || { echo "::error::El timer de copias de seguridad no está activo"; exit 1; }
echo "$backups" | grep -q '\.dump$' || { echo "::error::No hay ninguna copia de seguridad en el servidor"; exit 1; }

echo "DEPLOY_DOMAIN=$domain" >> "$GITHUB_ENV"
