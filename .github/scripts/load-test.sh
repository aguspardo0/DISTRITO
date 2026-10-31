#!/usr/bin/env bash
# Pruebas de escala (modo invitado, sin backend):
#   1. Interés de red: dos bots se alejan y al final cada uno solo se ve a sí mismo.
#   2. Carga: servidor dedicado + N bots que caminan, con métricas del servidor y ping de cada cliente.
# Uso: load-test.sh 10 20
set -uo pipefail
fail=0
note()  { echo "::notice title=$1::$2"; }
error() { echo "::error title=$1::$2"; fail=1; }
num()   { grep -o "$1=[0-9.]*" | cut -d= -f2; }

client() { # log puerto args...
  local log=$1 port=$2; shift 2
  timeout 120 "$GODOT" --headless --path game -- --client --host 127.0.0.1 --port "$port" "$@" > "$log" 2>&1
}

# ---------- 1. Interés de red ----------
PORT=7801
"$GODOT" --headless --path game -- --server --port $PORT --interest-radius 12 > interest-server.log 2>&1 &
srv=$!
sleep 4
client interest-a.log $PORT --bot-goto -8,12 --test-seconds 20 --expect-players 2 --expect-visible-end 1 & a=$!
client interest-b.log $PORT --bot-goto 12,-5 --test-seconds 20 --expect-players 2 --expect-visible-end 1 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
for x in a b; do
  line=$(grep -h NET_TEST_ "interest-$x.log" | tail -1)
  if grep -q NET_TEST_OK "interest-$x.log"; then note "Interés de red ($x)" "${line#*\] }"; else error "Interés de red ($x)" "${line:-sin resultado}"; fi
done

# ---------- 2. Carga ----------
for n in "$@"; do
  PORT=$((7810 + n))
  "$GODOT" --headless --path game -- --server --port $PORT --metrics-interval 5 > "load-server-$n.log" 2>&1 &
  srv=$!
  sleep 4
  pids=()
  for i in $(seq 1 "$n"); do
    client "load-$n-$i.log" $PORT --bot --test-seconds 35 --expect-players "$n" & pids+=($!)
    sleep 0.3
  done
  for p in "${pids[@]}"; do wait "$p"; done
  kill $srv 2>/dev/null; wait $srv 2>/dev/null

  ok=$(grep -l NET_TEST_OK load-$n-*.log 2>/dev/null | wc -l)
  pings=$(cat load-$n-*.log | grep -h NET_TEST_ | num ping_ms_prom)
  pmax=$(cat load-$n-*.log | grep -h NET_TEST_ | num ping_ms_max | sort -n | tail -1)
  pavg=$(echo "$pings" | awk '{s+=$1; c++} END {if (c) printf "%.0f", s/c; else print "?"}')
  full=$(grep "METRICS jugadores=$n " "load-server-$n.log")
  phys=$(echo "$full" | num fisica_ms_max | sort -n | tail -1)
  ram=$(echo "$full" | num ram_mb | sort -n | tail -1)
  sent=$(echo "$full" | num enviado_kbps | sort -n | tail -1)
  recv=$(echo "$full" | num recibido_kbps | sort -n | tail -1)
  per=$(awk -v s="${sent:-0}" -v n="$n" 'BEGIN {printf "%.1f", s/n}')
  summary="clientes OK ${ok}/${n} · ping prom ${pavg} ms, máx ${pmax:-?} ms · servidor: física máx ${phys:-?} ms/tick, RAM ${ram:-?} MB, envía ${sent:-?} kbps (${per} por jugador), recibe ${recv:-?} kbps"
  if [ "$ok" -eq "$n" ]; then note "Carga ${n} jugadores" "$summary"; else
    grep -h NET_TEST_FAIL load-$n-*.log | head -2 | while IFS= read -r l; do echo "::warning title=Carga ${n}::${l#*\] }"; done
    error "Carga ${n} jugadores" "$summary"
  fi
done

exit $fail
