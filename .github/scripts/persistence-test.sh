#!/usr/bin/env bash
# Prueba de punta a punta del hito "el progreso se guarda":
#   1. Backend (con PostgreSQL) + servidor de juego con cuentas.
#   2. Ronda 1: dos bots crean cuenta y personaje, se mueven y se desconectan.
#   3. Ronda 2: los mismos bots vuelven y deben aparecer donde quedaron.
#      Además: un duplicado del bot A debe ser rechazado ("ya está conectado").
#   4. Un cliente sin cuenta debe ser rechazado por el servidor con cuentas.
set -uo pipefail

BACKEND_URL="http://127.0.0.1:5080"
PORT=7790
export DISTRITO_SERVER_KEY="clave-ci-$RANDOM$RANDOM"
fail=0

note()  { echo "::notice title=$1::$2"; }
error() { echo "::error title=$1::$2"; fail=1; }

# --- Backend ---
ConnectionStrings__Distrito="$DISTRITO_TEST_DB" \
Internal__ServerKey="$DISTRITO_SERVER_KEY" \
ASPNETCORE_URLS="$BACKEND_URL" \
  dotnet backend/Distrito.Backend/bin/Debug/net10.0/Distrito.Backend.dll > persist-backend.log 2>&1 &
backend_pid=$!

for _ in $(seq 1 30); do
  curl -fs "$BACKEND_URL/health/ready" > /dev/null && break
  sleep 1
done
if ! curl -fs "$BACKEND_URL/health/ready"; then
  error "Backend" "no arrancó: $(tail -5 persist-backend.log | tr '\n' ' ')"
  exit 1
fi
echo

# --- Servidor de juego con cuentas ---
"$GODOT" --headless --path game -- --server --port "$PORT" --backend "$BACKEND_URL" > persist-server.log 2>&1 &
server_pid=$!
sleep 4

bot() { # nombre_log cuenta personaje segundos [args extra...]
  local log=$1 account=$2 character=$3 seconds=$4; shift 4
  timeout 90 "$GODOT" --headless --path game -- \
    --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
    --account "$account" --password "clave-de-bot-123" --character "$character" \
    --bot --test-seconds "$seconds" --expect-players 2 "$@" > "$log" 2>&1
}

result_line() { grep -h "NET_TEST_" "$1" | tail -1; }
final_pos()   { result_line "$1" | grep -o 'pos=[-0-9.]*,[-0-9.]*' | cut -d= -f2; }

# --- Ronda 1: moverse y salir ---
bot persist-r1-a.log bot_alfa "Bot Alfa" 12 & a=$!
bot persist-r1-b.log bot_beta "Bot Beta" 12 & b=$!
wait $a; ra=$?; wait $b; rb=$?
for x in a b; do
  line=$(result_line "persist-r1-$x.log")
  if grep -q NET_TEST_OK "persist-r1-$x.log"; then note "Ronda 1 bot $x" "${line#*\] }"; else error "Ronda 1 bot $x" "${line:-sin resultado}"; fi
done
pos_a=$(final_pos persist-r1-a.log); pos_b=$(final_pos persist-r1-b.log)
sleep 3 # el servidor guarda al detectar la desconexión

saved=$(PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -At -F ' ' \
  -c "SELECT name, round(pos_x::numeric,2), round(pos_z::numeric,2) FROM distrito.characters ORDER BY name")
note "Base de datos" "posiciones guardadas: $(echo "$saved" | tr '\n' ';')"

# --- Ronda 2: volver y aparecer en el mismo lugar; un duplicado debe ser rechazado ---
if [ -n "$pos_a" ] && [ -n "$pos_b" ]; then
  bot persist-r2-a.log bot_alfa "Bot Alfa" 8 --expect-spawn "$pos_a" & a=$!
  bot persist-r2-b.log bot_beta "Bot Beta" 8 --expect-spawn "$pos_b" & b=$!
  sleep 3
  bot persist-dup.log bot_alfa "Bot Alfa" 4 & d=$!
  wait $a; wait $b; wait $d
  for x in a b; do
    line=$(result_line "persist-r2-$x.log")
    if grep -q NET_TEST_OK "persist-r2-$x.log"; then note "Ronda 2 bot $x (esperado $( [ $x = a ] && echo "$pos_a" || echo "$pos_b"))" "${line#*\] }"; else error "Ronda 2 bot $x" "${line:-sin resultado}"; fi
  done
  if grep -q "ya está conectado" persist-dup.log; then note "Duplicado" "rechazado correctamente"; else error "Duplicado" "el mismo personaje pudo entrar dos veces"; fi
else
  error "Ronda 2" "no se obtuvo la posición final de la ronda 1"
fi

# --- Cliente sin cuenta contra servidor con cuentas ---
timeout 60 "$GODOT" --headless --path game -- --client --host 127.0.0.1 --port "$PORT" --test-seconds 4 > persist-guest.log 2>&1
if grep -q "requiere iniciar sesión" persist-guest.log; then note "Invitado" "rechazado correctamente"; else error "Invitado" "el servidor con cuentas aceptó un cliente sin sesión"; fi

note "Servidor" "$(grep -c 'Guardado ' persist-server.log) guardados registrados"

kill "$server_pid" "$backend_pid" 2>/dev/null || true
exit $fail
