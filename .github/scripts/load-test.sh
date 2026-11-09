#!/usr/bin/env bash
# Pruebas de escala (modo invitado, sin backend):
#   1. Interés de red: dos bots se alejan y al final cada uno solo se ve a sí mismo.
#   2. Red mala: latencia, variación y pérdida simuladas con netem.
#   3. Carga: servidor dedicado + N bots que caminan, con métricas del servidor y ping de cada cliente.
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
client interest-a.log $PORT --bot-goto -8,4 --test-seconds 20 --expect-players 2 --expect-visible-end 1 --expect-map-players 2 & a=$!
client interest-b.log $PORT --bot-goto 12,-5 --test-seconds 20 --expect-players 2 --expect-visible-end 1 --expect-map-players 2 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
for x in a b; do
  line=$(grep -h NET_TEST_ "interest-$x.log" | tail -1)
  if grep -q NET_TEST_OK "interest-$x.log"; then note "Interés de red ($x)" "${line#*\] }"; else error "Interés de red ($x)" "${line:-sin resultado}"; fi
done

# ---------- 1b. Colisiones: un bot intenta atravesar un edificio, otro salir del barrio ----------
# El Galpón Moretti ocupa x en [-28,-8] y z en [-27,-11]: el bot apunta a su centro (-18,-19) y tiene que quedar contra
# el frente. El otro bot va por la Calle Principal y la Avenida del Puerto, cruza Los Muelles y llega al borde de la
# ciudad, en x = 300 (pared invisible, del otro lado está el agua).
PORT=7803
"$GODOT" --headless --path game -- --server --port $PORT > walls-server.log 2>&1 &
srv=$!
sleep 4
client walls-a.log $PORT --bot-goto -18,-19 --test-seconds 20 --expect-players 2 & a=$!
client walls-b.log $PORT --bot-route "0,-1;100,-1;240,-1;400,-1" --test-seconds 110 --expect-players 2 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
pos_of() { grep -h NET_TEST_ "$1" | tail -1 | grep -o ' pos=[-0-9.,]*' | cut -d= -f2; }
pa=$(pos_of walls-a.log); pb=$(pos_of walls-b.log)
if grep -q NET_TEST_OK walls-a.log && echo "$pa" | awk -F, '{ dx=$1+18; dz=$2+19; if (dx<0) dx=-dx; if (dz<0) dz=-dz; exit !(dx > 4.2 || dz > 4.2) }'; then
  note "Colisión con edificio" "el bot quedó afuera, en ${pa}"
else error "Colisión con edificio" "posición final ${pa:-?}: atravesó el edificio o falló ($(grep -h NET_TEST_ walls-a.log | tail -1))"; fi
if grep -q NET_TEST_OK walls-b.log && echo "$pb" | awk -F, '{ exit !($1 >= 250 && $1 <= 300.2) }'; then
  note "Borde de la ciudad" "el bot fue por la Calle Principal y la Avenida del Puerto, cruzó Los Muelles y el borde lo frenó en ${pb}"
else error "Borde de la ciudad" "posición final ${pb:-?} ($(grep -h NET_TEST_ walls-b.log | tail -1 | cut -c1-200))"; fi

# ---------- 1d. Barrios de las familias: se llega por las avenidas y el borde de la ciudad frena ----------
# A va por la Avenida Oeste hasta la plaza del Barrio Moretti (antes de la fuente, x≈−107).
# B sube por la Avenida Norte al Barrio Valenti, dobla por una calle y choca con el borde este del barrio (x = 74.5).
PORT=7808
"$GODOT" --headless --path game -- --server --port $PORT > barrios-server.log 2>&1 &
srv=$!
sleep 4
client barrios-a.log $PORT --bot-route "0,-1;-25,-1;-104,-1" --test-seconds 40 --expect-players 2 & a=$!
client barrios-b.log $PORT --bot-route "0,-1;24.5,-1;24.5,-91;200,-91" --test-seconds 40 --expect-players 2 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
pa=$(pos_of barrios-a.log); pb=$(pos_of barrios-b.log)
if grep -q NET_TEST_OK barrios-a.log && echo "$pa" | awk -F, '{ exit !($1 <= -100 && $2 > -6 && $2 < 6) }'; then
  note "Barrio Moretti" "el bot llegó por la Avenida Oeste a la plaza: ${pa}"
else error "Barrio Moretti" "posición final ${pa:-?} ($(grep -h NET_TEST_ barrios-a.log | tail -1 | cut -c1-200))"; fi
if grep -q NET_TEST_OK barrios-b.log && echo "$pb" | awk -F, '{ exit !($1 >= 70 && $1 <= 75.2 && $2 < -85) }'; then
  note "Barrio Valenti y borde" "el bot entró por la Avenida Norte y el borde lo frenó en ${pb}"
else error "Barrio Valenti y borde" "posición final ${pb:-?} ($(grep -h NET_TEST_ barrios-b.log | tail -1 | cut -c1-200))"; fi

# ---------- 1c. PvP a piñas (partida sin cuentas: vale todo salvo cerca de los locales) ----------
PORT=7804
"$GODOT" --headless --path game -- --server --port $PORT > fight-server.log 2>&1 &
srv=$!
sleep 4
client fight-victim.log $PORT --bot-goto 3,-2 --test-seconds 30 --expect-players 2 & a=$!
sleep 1
client fight-attacker.log $PORT --bot-fight --test-seconds 30 --expect-players 2 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
line=$(grep -h NET_TEST_ fight-attacker.log | tail -1)
if grep -q NET_TEST_OK fight-attacker.log && grep -q COMBAT_KO fight-server.log; then
  note "PvP a piñas" "$(grep COMBAT_KO fight-server.log | head -1 | sed 's/.*COMBAT_KO //') — ${line#*\] }"
else
  error "PvP a piñas" "${line:-sin resultado} $(grep -h 'bot-pelea' fight-attacker.log | tail -1)"
fi
line=$(grep -h NET_TEST_ fight-victim.log | tail -1)
if grep -q NET_TEST_OK fight-victim.log; then note "PvP (herido)" "reapareció y siguió conectado — ${line#*\] }"; else error "PvP (herido)" "${line:-sin resultado}"; fi

# ---------- 1d. PvP con pistola ----------
PORT=7805
"$GODOT" --headless --path game -- --server --port $PORT > gun-server.log 2>&1 &
srv=$!
sleep 4
client gun-victim.log $PORT --bot-goto 3,-2 --test-seconds 30 --expect-players 2 & a=$!
sleep 1
client gun-attacker.log $PORT --bot-fight-weapon pistola --test-seconds 30 --expect-players 2 & b=$!
wait $a; wait $b
kill $srv 2>/dev/null; wait $srv 2>/dev/null
line=$(grep -h NET_TEST_ gun-attacker.log | tail -1)
if grep -q NET_TEST_OK gun-attacker.log && grep -q COMBAT_KO gun-server.log; then
  note "PvP con pistola" "$(grep COMBAT_KO gun-server.log | head -1 | sed 's/.*COMBAT_KO //') — ${line#*\] }"
else
  error "PvP con pistola" "${line:-sin resultado} $(grep -h 'bot-pelea' gun-attacker.log | tail -1)"
fi

# ---------- 2. Red mala: latencia, variación y pérdida de paquetes ----------
# netem sobre la interfaz local: 60 ms ± 20 ms por sentido (≈120 ms de ida y vuelta) y 3 % de pérdida.
# Dos bots caminan; se exige que sigan conectados, se vean y que la predicción local converja con el servidor.
PORT=7802
if sudo tc qdisc add dev lo root netem delay 60ms 20ms distribution normal loss 3% 2>tc.err; then
  "$GODOT" --headless --path game -- --server --port $PORT --metrics-interval 5 > lag-server.log 2>&1 &
  srv=$!
  sleep 4
  client lag-a.log $PORT --bot --test-seconds 30 --expect-players 2 & a=$!
  client lag-b.log $PORT --bot --test-seconds 30 --expect-players 2 & b=$!
  wait $a; wait $b
  kill $srv 2>/dev/null; wait $srv 2>/dev/null
  sudo tc qdisc del dev lo root
  for x in a b; do
    line=$(grep -h NET_TEST_ "lag-$x.log" | tail -1)
    if grep -q NET_TEST_OK "lag-$x.log"; then note "Red mala ($x): 120 ms y 3 % de pérdida" "${line#*\] }"
    else error "Red mala ($x): 120 ms y 3 % de pérdida" "${line:-sin resultado}"; fi
  done
  disc=$(grep METRICS lag-server.log | num desconexiones | sort -n | tail -1)
  note "Red mala: servidor" "$(grep METRICS lag-server.log | tail -1 | sed 's/.*METRICS //') (desconexiones en la prueba: ${disc:-?})"
else
  echo "::warning title=Red mala::no se pudo simular la red ($(cat tc.err)); prueba omitida"
fi

# ---------- 3. Carga ----------
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
  cpu=$(echo "$full" | num cpu_pct | sort -n | tail -1)
  disc=$(echo "$full" | num desconexiones | sort -n | tail -1) # mientras estaban todos conectados
  sent=$(echo "$full" | num enviado_kbps | sort -n | tail -1)
  recv=$(echo "$full" | num recibido_kbps | sort -n | tail -1)
  per=$(awk -v s="${sent:-0}" -v n="$n" 'BEGIN {printf "%.1f", s/n}')
  summary="clientes OK ${ok}/${n} · ping prom ${pavg} ms, máx ${pmax:-?} ms · servidor: física máx ${phys:-?} ms/tick, CPU máx ${cpu:-?} %, RAM ${ram:-?} MB, envía ${sent:-?} kbps (${per} por jugador), recibe ${recv:-?} kbps, desconexiones ${disc:-?}"
  if [ "$ok" -eq "$n" ]; then note "Carga ${n} jugadores" "$summary"; else
    grep -h NET_TEST_FAIL load-$n-*.log | head -2 | while IFS= read -r l; do echo "::warning title=Carga ${n}::${l#*\] }"; done
    error "Carga ${n} jugadores" "$summary"
  fi
done

exit $fail
