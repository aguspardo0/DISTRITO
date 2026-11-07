#!/usr/bin/env bash
# Corre en cada máquina de bots de la prueba de escala real. Todas las máquinas arrancan al mismo segundo (T0)
# y suman bots en dos escalones, para medir el servidor con la mitad y con el total:
#   t = 0 … mitad s        entra 1 bot por segundo hasta la mitad (p. ej. 50)
#   30 s quietos           meseta con la mitad
#   después                entra 1 bot por segundo hasta el total (p. ej. 100)
#   60 s quietos           meseta con el total; después salen todos juntos
# Cada bot es un cliente real (Godot sin ventana) que camina en círculos y mide su ping.
# Uso: escala-bots.sh <host> <puerto> <t0-epoch> <máquina 0..n-1> <máquinas> <bots en total>
set -uo pipefail
host=$1 port=$2 t0=$3 runner=$4 runners=$5 total=$6
half=$((total / 2))
end=$((total + 30 + 60))
mkdir -p bots

join_at() { local g=$1; if [ "$g" -lt "$half" ]; then echo "$g"; else echo $((half + 30 + g - half)); fi; }

now=$(date +%s)
if [ "$now" -gt "$t0" ]; then
  echo "::warning title=Bots máquina ${runner}::la máquina estuvo lista $((now - t0)) s tarde; sus primeros bots entran con atraso"
fi

pids=()
for g in $(seq "$runner" "$runners" $((total - 1))); do
  at=$((t0 + $(join_at "$g")))
  wait_s=$((at - $(date +%s)))
  [ "$wait_s" -gt 0 ] && sleep "$wait_s"
  seconds=$((t0 + end - $(date +%s)))
  [ "$seconds" -lt 10 ] && seconds=10
  timeout $((seconds + 60)) "$GODOT" --headless --path game -- --client --host "$host" --port "$port" \
    --bot --test-seconds "$seconds" --expect-players 1 > "bots/bot-$g.log" 2>&1 &
  pids+=($!)
done
for p in "${pids[@]}"; do wait "$p"; done

ok=$(grep -l NET_TEST_OK bots/*.log 2>/dev/null | wc -l)
n=$(ls bots/*.log | wc -l)
pings=$(grep -h NET_TEST_ bots/*.log | grep -o 'ping_ms_prom=[0-9.]*' | cut -d= -f2)
pavg=$(echo "$pings" | awk '{s+=$1; c++} END {if (c) printf "%.0f", s/c; else print "?"}')
pmax=$(grep -h NET_TEST_ bots/*.log | grep -o 'ping_ms_max=[0-9.]*' | cut -d= -f2 | sort -n | tail -1)
seen=$(grep -h NET_TEST_ bots/*.log | grep -o 'jugadores_max=[0-9]*' | cut -d= -f2 | sort -n | tail -1)
echo "ESCALA_BOTS maquina=${runner} ok=${ok}/${n} ping_prom=${pavg} ping_max=${pmax:-?} vio_max=${seen:-?}" | tee bots/resumen.txt
echo "::notice title=Bots máquina ${runner}::${ok}/${n} conectados hasta el final · ping prom ${pavg} ms, máx ${pmax:-?} ms · el que más vio: ${seen:-?} jugadores"
grep -h NET_TEST_FAIL bots/*.log | head -3 | while IFS= read -r l; do echo "::warning title=Bot con falla (máquina ${runner})::${l#*\] }"; done
[ "$ok" -eq "$n" ]
