#!/usr/bin/env bash
# Ejecuta un comando; si falla, publica sus líneas de error como anotaciones de GitHub
# (visibles en la página del run sin abrir los logs completos).
set -uo pipefail
log=$(mktemp)
"$@" 2>&1 | tee "$log"
code=${PIPESTATUS[0]}
if [ "$code" -ne 0 ]; then
  # Pruebas fallidas: nombre + mensaje real (las líneas que siguen a "Error Message:").
  grep -A2 "Error Message:" "$log" | grep -vE "Error Message:|^--$" | head -6 | while IFS= read -r line; do
    echo "::error title=Prueba fallida::${line:0:900}"
  done
  grep -E "error|Error|ERROR|failed|Failed" "$log" | grep -v "^\s*$" | sort -u | head -8 | while IFS= read -r line; do
    echo "::error title=$(basename "$1") $2::${line:0:900}"
  done
fi
exit "$code"
