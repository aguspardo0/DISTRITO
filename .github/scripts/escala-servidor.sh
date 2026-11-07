#!/usr/bin/env bash
# Corre EN EL SERVIDOR (por SSH) durante la prueba de escala real.
# Levanta una partida aparte, en modo invitado, con la versión desplegada; no toca la partida oficial (UDP 7777)
# ni la base de datos. Abre el puerto solo durante la prueba y lo cierra al terminar, pase lo que pase.
# Uso: escala-servidor.sh <puerto> <segundos> <max-jugadores>
set -uo pipefail
port=$1 seconds=$2 max=$3

cleanup() {
  if command -v ufw >/dev/null && ufw status | grep -q "Status: active"; then ufw delete allow "$port/udp" >/dev/null 2>&1; fi
  while iptables -C INPUT -p udp --dport "$port" -j ACCEPT 2>/dev/null; do iptables -D INPUT -p udp --dport "$port" -j ACCEPT; done
  echo "ESCALA_PUERTO_CERRADO $port"
}
trap cleanup EXIT

if command -v ufw >/dev/null && ufw status | grep -q "Status: active"; then ufw allow "$port/udp" >/dev/null; fi
iptables -C INPUT -p udp --dport "$port" -j ACCEPT 2>/dev/null || iptables -I INPUT 1 -p udp --dport "$port" -j ACCEPT

release=$(readlink -f /opt/distrito/current)
cpu=$(lscpu | awk -F: '/Model name/ {gsub(/^ +/, "", $2); print $2; exit}')
echo "ESCALA_MAQUINA nucleos=$(nproc) ram_mb=$(free -m | awk '/Mem/ {print $2}') cpu=\"${cpu}\" version=$(basename "$release")"
echo "ESCALA_OFICIAL $(systemctl is-active distrito-gameserver 2>/dev/null) (la partida oficial sigue en el 7777)"

mkdir -p /tmp/distrito-escala
cd "$release/server"
HOME=/tmp/distrito-escala timeout "$seconds" ./DistritoServer --headless -- --server --port "$port" \
  --max-players "$max" --metrics-interval 5 2>&1 | grep --line-buffered -E "METRICS|Servidor dedicado|ERROR|rror"
echo "ESCALA_FIN"
