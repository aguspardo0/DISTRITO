#!/usr/bin/env bash
# Copia de seguridad de la base «distrito» (pg_dump en formato propio de PostgreSQL), verificada y con rotación.
# Uso: distrito-backup [programada|predespliegue|manual]
#   programada    → cada 6 horas (timer de systemd); se guardan las últimas 28 (una semana)
#   predespliegue → antes de cada despliegue (puede haber migraciones); se guardan las últimas 5
#   manual        → a mano; se guardan las últimas 10
set -euo pipefail

kind="${1:-manual}"
case "$kind" in programada|predespliegue|manual) ;; *) echo "Tipo inválido: $kind" >&2; exit 2;; esac

dir=/var/backups/distrito
mkdir -p "$dir"
chmod 700 "$dir"

# Si queda poco disco, mejor avisar que llenarlo (la base y el juego lo necesitan).
free_mb=$(df -Pm "$dir" | awk 'NR==2 {print $4}')
if [ "$free_mb" -lt 300 ]; then
  echo "[backup] ERROR: quedan ${free_mb} MB libres; no se hace la copia." >&2
  exit 1
fi

stamp=$(date -u +%Y%m%d-%H%M%S)
file="$dir/distrito-$stamp-$kind.dump"
trap 'rm -f "$file.tmp"' EXIT
runuser -u postgres -- pg_dump -Fc distrito > "$file.tmp"
# Verificación: el archivo tiene que poder leerse entero (lista de objetos de la copia).
objects=$(pg_restore --list "$file.tmp" | awk '!/^;/ && NF {n++} END {print n+0}')
mv "$file.tmp" "$file"
chmod 600 "$file"

keep=$([ "$kind" = programada ] && echo 28 || ([ "$kind" = predespliegue ] && echo 5 || echo 10))
ls -1t "$dir"/distrito-*-"$kind".dump 2>/dev/null | tail -n +$((keep + 1)) | xargs -r rm -f

echo "[backup] OK $kind: $(basename "$file") ($(du -h "$file" | cut -f1), $objects objetos); en $dir quedan $(ls -1 "$dir"/*.dump | wc -l) copias"
