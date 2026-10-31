#!/usr/bin/env bash
# Instala o actualiza DISTRITO en el servidor (Ubuntu 22.04 o 24.04, ARM64, Oracle Cloud).
# Idempotente: se ejecuta en cada despliegue. Uso (como root):
#   setup-server.sh <carpeta-del-release> <dominio> <commit>
#
# Deja:
#   PostgreSQL local (solo 127.0.0.1)            → base "distrito"
#   Backend en 127.0.0.1:5080 (systemd)          → público solo vía Caddy/HTTPS, sin /internal
#   Servidor de juego UDP 7777 (systemd)         → habla con el backend por 127.0.0.1
#   Caddy en 80/443 con certificado automático   → https://<dominio>
# Secretos (clave de base y del servidor) se generan una sola vez en /etc/distrito/distrito.env.
set -euo pipefail

RELEASE_SRC=$1
DOMAIN=$2
COMMIT=$3
APP=/opt/distrito
ENV_FILE=/etc/distrito/distrito.env

log() { echo "[setup] $*"; }

# --- Paquetes ---
export DEBIAN_FRONTEND=noninteractive
# Caddy: si el sistema no lo trae en sus repositorios (Ubuntu 22.04), se usa el repositorio oficial de Caddy.
if ! command -v caddy >/dev/null && ! apt-cache show caddy >/dev/null 2>&1; then
  log "Agregando repositorio oficial de Caddy"
  apt-get install -y -q debian-keyring debian-archive-keyring apt-transport-https curl gnupg
  curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/gpg.key | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -q
fi
need=()
command -v psql  >/dev/null || need+=(postgresql)
command -v caddy >/dev/null || need+=(caddy)
dpkg -s iptables-persistent >/dev/null 2>&1 || need+=(iptables-persistent)
if [ ${#need[@]} -gt 0 ]; then
  log "Instalando: ${need[*]}"
  apt-get update -q
  apt-get install -y -q "${need[@]}"
fi

# --- Usuario del servicio ---
id distrito >/dev/null 2>&1 || useradd --system --home "$APP" --shell /usr/sbin/nologin distrito

# --- Secretos (una sola vez) ---
mkdir -p /etc/distrito
if [ ! -f "$ENV_FILE" ]; then
  log "Generando secretos"
  db_pass=$(openssl rand -hex 24)
  server_key=$(openssl rand -hex 32)
  cat > "$ENV_FILE" <<EOF
DB_PASSWORD=$db_pass
DISTRITO_SERVER_KEY=$server_key
ConnectionStrings__Distrito=Host=127.0.0.1;Port=5432;Database=distrito;Username=distrito;Password=$db_pass
Internal__ServerKey=$server_key
ASPNETCORE_URLS=http://127.0.0.1:5080
ASPNETCORE_ENVIRONMENT=Production
EOF
fi
chown root:distrito "$ENV_FILE"
chmod 640 "$ENV_FILE"
# shellcheck disable=SC1090
source "$ENV_FILE"

# --- PostgreSQL ---
systemctl enable --now postgresql
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='distrito'" | grep -q 1; then
  log "Creando rol de base de datos"
  sudo -u postgres psql -q -c "CREATE ROLE distrito LOGIN PASSWORD '$DB_PASSWORD'"
fi
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='distrito'" | grep -q 1; then
  log "Creando base de datos"
  sudo -u postgres createdb -O distrito distrito
fi

# --- Release ---
release="$APP/releases/$COMMIT"
log "Instalando release $COMMIT"
rm -rf "$release"
mkdir -p "$release"
cp -a "$RELEASE_SRC"/. "$release"/
chmod +x "$release/server/DistritoServer.arm64" "$release/backend/Distrito.Backend"
chown -R distrito:distrito "$APP"
ln -sfn "$release" "$APP/current"

# --- Servicios ---
cat > /etc/systemd/system/distrito-backend.service <<EOF
[Unit]
Description=DISTRITO backend (cuentas y persistencia)
After=network.target postgresql.service
Requires=postgresql.service

[Service]
User=distrito
EnvironmentFile=$ENV_FILE
WorkingDirectory=$APP/current/backend
ExecStart=$APP/current/backend/Distrito.Backend
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/systemd/system/distrito-gameserver.service <<EOF
[Unit]
Description=DISTRITO servidor de juego (barrio de prueba)
After=network.target distrito-backend.service
Wants=distrito-backend.service

[Service]
User=distrito
EnvironmentFile=$ENV_FILE
WorkingDirectory=$APP/current/server
ExecStart=$APP/current/server/DistritoServer.arm64 --headless -- --server --port 7777 --backend http://127.0.0.1:5080
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

# --- HTTPS (Caddy). La API interna no se expone hacia afuera. ---
cat > /etc/caddy/Caddyfile <<EOF
$DOMAIN {
    @internal path /internal/*
    respond @internal 404
    reverse_proxy 127.0.0.1:5080
}
EOF

# --- Firewall del sistema (las imágenes de Oracle bloquean todo salvo SSH) ---
for rule in "-p tcp --dport 80" "-p tcp --dport 443" "-p udp --dport 7777"; do
  # shellcheck disable=SC2086
  iptables -C INPUT $rule -j ACCEPT 2>/dev/null || iptables -I INPUT 1 $rule -j ACCEPT
done
netfilter-persistent save >/dev/null 2>&1 || true

systemctl daemon-reload
systemctl enable distrito-backend distrito-gameserver caddy >/dev/null
systemctl restart distrito-backend
for _ in $(seq 1 30); do curl -fs http://127.0.0.1:5080/health/ready >/dev/null && break; sleep 1; done
curl -fs http://127.0.0.1:5080/health/ready || { journalctl -u distrito-backend -n 40 --no-pager; exit 1; }
echo
systemctl restart distrito-gameserver
systemctl reload caddy || systemctl restart caddy

sleep 3
systemctl is-active --quiet distrito-gameserver || { journalctl -u distrito-gameserver -n 40 --no-pager; exit 1; }

# --- Limpieza: se conservan los últimos 3 releases ---
ls -1dt "$APP"/releases/* | tail -n +4 | xargs -r rm -rf

log "OK — https://$DOMAIN  ·  juego UDP 7777  ·  release $COMMIT"
