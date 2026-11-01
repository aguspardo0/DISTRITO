#!/usr/bin/env bash
# Escribe por la salida estándar la copia de seguridad más reciente de la base, CIFRADA con age para las llaves
# públicas de /etc/distrito/backup-recipients.txt (vienen de deploy/backup-recipients.txt del repo).
# La copia sin cifrar nunca sale del servidor: solo quien tenga una de las llaves privadas puede abrirla.
# Uso (lo llama la CI por SSH): distrito-backup-export > distrito-<fecha>.dump.age
#      distrito-backup-export --nombre   → solo imprime el nombre de la copia que se exportaría
set -euo pipefail

dir=/var/backups/distrito
recipients=/etc/distrito/backup-recipients.txt

file=$(ls -1t "$dir"/*.dump 2>/dev/null | head -1 || true)
[ -n "$file" ] || { echo "No hay copias en $dir" >&2; exit 1; }
if [ "${1:-}" = "--nombre" ]; then basename "$file"; exit 0; fi

if [ ! -f "$recipients" ] || ! grep -q '^age1' "$recipients"; then
  echo "No hay llaves públicas en $recipients: no se exporta nada" >&2
  exit 3
fi
command -v age >/dev/null || { echo "Falta age en el servidor" >&2; exit 1; }
pg_restore --list "$file" >/dev/null || { echo "La copia $file no es válida" >&2; exit 1; }

# age acepta comentarios (#) y líneas vacías en el archivo de destinatarios.
age -R "$recipients" < "$file"
