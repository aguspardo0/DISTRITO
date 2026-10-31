#!/usr/bin/env bash
# Escribe DEPLOY_SSH_KEY en un archivo usable por ssh: normaliza finales de línea y,
# si la llave quedó cifrada con la contraseña literal "" (pasa con algunas versiones de PowerShell),
# la descifra en el runner. Imprime la huella pública (no es secreta) para comparar con la de Vultr.
# Uso: prepare-key.sh <archivo-destino>
set -uo pipefail
key=$1
printf '%s\n' "$DEPLOY_SSH_KEY" | tr -d '\r' > "$key"
chmod 600 "$key"

if ! head -1 "$key" | grep -q "BEGIN"; then
  echo "::error title=Llave SSH::DEPLOY_SSH_KEY no empieza con -----BEGIN (¿se pegó la llave pública?)"
  exit 1
fi

if ssh-keygen -y -P "" -f "$key" > "$key.pub" 2>/dev/null; then
  :
elif ssh-keygen -y -P '""' -f "$key" > "$key.pub" 2>/dev/null; then
  echo "::notice title=Llave SSH::la llave tenía la contraseña literal \"\"; se quita en el runner"
  ssh-keygen -p -P '""' -N "" -f "$key" >/dev/null
else
  echo "::error title=Llave SSH::la llave está protegida con una contraseña desconocida; hay que generarla sin contraseña"
  exit 1
fi

echo "::notice title=Llave SSH::huella pública $(ssh-keygen -lf "$key.pub" | awk '{print $2}') — empieza con: $(cut -c1-40 "$key.pub")"
