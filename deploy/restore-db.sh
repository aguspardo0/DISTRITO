#!/usr/bin/env bash
# Restaura una copia de la base «distrito». Para el juego y el backend mientras tanto y, antes de pisar nada,
# hace una copia «manual» del estado actual (por si hay que volver atrás).
# Uso: distrito-restore /var/backups/distrito/distrito-AAAAMMDD-HHMMSS-<tipo>.dump
#      distrito-restore --ultima      (la copia más reciente)
set -euo pipefail

dir=/var/backups/distrito
file="${1:-}"
if [ "$file" = "--ultima" ]; then
  file=$(ls -1t "$dir"/*.dump 2>/dev/null | head -1)
fi
if [ -z "$file" ] || [ ! -f "$file" ]; then
  echo "Uso: distrito-restore <archivo.dump> | --ultima"
  echo "Copias disponibles (la más nueva arriba):"
  ls -1t "$dir"/*.dump 2>/dev/null | head -20 || true
  exit 2
fi
pg_restore --list "$file" >/dev/null || { echo "El archivo no es una copia válida: $file" >&2; exit 1; }

echo "[restore] Copia de seguridad del estado actual antes de restaurar…"
/usr/local/sbin/distrito-backup manual

echo "[restore] Parando el juego y el backend…"
systemctl stop distrito-gameserver distrito-backend

echo "[restore] Restaurando $(basename "$file")…"
runuser -u postgres -- dropdb --if-exists distrito
runuser -u postgres -- createdb -O distrito distrito
# La copia es de root (600): se la pasa por entrada estándar para que postgres no necesite leer el archivo.
runuser -u postgres -- pg_restore --no-owner --role=distrito --exit-on-error -d distrito < "$file"

echo "[restore] Levantando el backend y el juego…"
systemctl start distrito-backend
for _ in $(seq 1 30); do curl -fs http://127.0.0.1:5080/health/ready >/dev/null && break; sleep 1; done
systemctl start distrito-gameserver
echo "[restore] OK: la base quedó como en $(basename "$file")."
