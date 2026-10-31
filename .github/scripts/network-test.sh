#!/usr/bin/env bash
# Prueba de red de punta a punta:
#   1. Arranca un servidor dedicado headless.
#   2. Arranca 2 clientes headless que se mueven solos (bot).
#   3. Cada cliente verifica: conectado, ve a los 2 jugadores, y el servidor movió su personaje.
set -uo pipefail

PORT=7777
SECONDS_PER_CLIENT=10

"$GODOT" --headless --path game -- --server --port "$PORT" > net-server.log 2>&1 &
server_pid=$!
sleep 4

client() {
  timeout 90 "$GODOT" --headless --path game -- \
    --client --host 127.0.0.1 --port "$PORT" --bot \
    --test-seconds "$SECONDS_PER_CLIENT" --expect-players 2 > "net-client-$1.log" 2>&1
}

client 1 & c1=$!
client 2 & c2=$!
wait "$c1"; r1=$?
wait "$c2"; r2=$?

kill "$server_pid" 2>/dev/null || true
wait "$server_pid" 2>/dev/null || true

ok=0
for i in 1 2; do
  line=$(grep -h "NET_TEST_" "net-client-$i.log" | tail -1)
  if grep -q "NET_TEST_OK" "net-client-$i.log"; then
    echo "::notice title=Red cliente $i::${line#*\] }"
  else
    echo "::error title=Red cliente $i::${line:-sin resultado (ver logs)}"
    ok=1
  fi
done
echo "::notice title=Red servidor::$(grep -c ' conectado. Jugadores' net-server.log) conexiones registradas"

echo "códigos de salida: cliente1=$r1 cliente2=$r2"
[ "$r1" -eq 0 ] && [ "$r2" -eq 0 ] && [ "$ok" -eq 0 ]
