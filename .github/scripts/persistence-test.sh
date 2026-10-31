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

# GitHub muestra solo 10 anotaciones por paso: cada resultado también va al resumen de la corrida.
summary() { [ -n "${GITHUB_STEP_SUMMARY:-}" ] && echo "- $1 **$2**: ${3:0:400}" >> "$GITHUB_STEP_SUMMARY"; return 0; }
note()  { echo "::notice title=$1::$2"; summary "✅" "$1" "$2"; }
error() { echo "::error title=$1::$2"; summary "❌" "$1" "$2"; fail=1; }

# --- Backend ---
ConnectionStrings__Distrito="$DISTRITO_TEST_DB" \
Internal__ServerKey="$DISTRITO_SERVER_KEY" \
Economy__BusinessCycleSeconds=5 \
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
  -c "SELECT name, round(pos_x::numeric,2), round(pos_y::numeric,2), round(pos_z::numeric,2) FROM distrito.characters ORDER BY name")
note "Base de datos" "posiciones guardadas (x y z): $(echo "$saved" | tr '\n' ';')"
if echo "$saved" | awk '{ if ($(NF-1) > 5) bad=1 } END { exit !bad }'; then error "Base de datos" "hay personajes guardados flotando (y > 5)"; fi

# --- Ronda 2: volver y aparecer en el mismo lugar; un duplicado debe ser rechazado ---
if [ -n "$pos_a" ] && [ -n "$pos_b" ]; then
  bot persist-r2-a.log bot_alfa "Bot Alfa" 8 --expect-spawn "$pos_a" & a=$!
  bot persist-r2-b.log bot_beta "Bot Beta" 8 --expect-spawn "$pos_b" & b=$!
  sleep 3
  bot persist-dup.log bot_alfa "Bot Alfa" 4 & d=$!
  wait $a; wait $b; wait $d
  for x in a b; do
    line=$(result_line "persist-r2-$x.log")
    if grep -q NET_TEST_OK "persist-r2-$x.log"; then note "Ronda 2 bot $x" "esperado=$( [ $x = a ] && echo "$pos_a" || echo "$pos_b") ${line#*\] }"; else error "Ronda 2 bot $x" "${line:-sin resultado}"; fi
  done
  if grep -q "ya está conectado" persist-dup.log; then note "Duplicado" "rechazado correctamente"; else error "Duplicado" "el mismo personaje pudo entrar dos veces"; fi
else
  error "Ronda 2" "no se obtuvo la posición final de la ronda 1"
fi

grep -h "Posición guardada" persist-server.log | while IFS= read -r l; do echo "::warning title=Servidor (ubicación)::${l#*\] }"; done

# --- Ubicación segura: posiciones guardadas corruptas (fuera del barrio / dentro de un edificio) ---
PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -q -c "
  UPDATE distrito.characters SET pos_x = 500, pos_y = 1, pos_z = 500 WHERE name = 'Bot Alfa';
  UPDATE distrito.characters SET pos_x = -14, pos_y = 0, pos_z = -14 WHERE name = 'Bot Beta';"
for who in "bot_alfa:Bot Alfa:fuera del barrio" "bot_beta:Bot Beta:dentro de un edificio"; do
  IFS=: read -r acc name why <<< "$who"
  timeout 60 "$GODOT" --headless --path game -- \
    --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
    --account "$acc" --password "clave-de-bot-123" --character "$name" \
    --test-seconds 6 --expect-players 1 --expect-spawn 3,0 > "persist-safe-$acc.log" 2>&1
  line=$(result_line "persist-safe-$acc.log")
  if grep -q NET_TEST_OK "persist-safe-$acc.log"; then note "Ubicación segura ($why)" "${line#*\] }"; else error "Ubicación segura ($why)" "${line:-sin resultado}"; fi
done

# --- Reconexión: se corta el servidor en plena partida y el cliente vuelve solo ---
timeout 90 "$GODOT" --headless --path game -- \
  --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
  --account bot_reconecta --password "clave-de-bot-123" --character "Bot Reconecta" \
  --test-seconds 35 --expect-players 1 > persist-reconnect.log 2>&1 & rc=$!
sleep 10
kill "$server_pid" 2>/dev/null; wait "$server_pid" 2>/dev/null
sleep 3
"$GODOT" --headless --path game -- --server --port "$PORT" --backend "$BACKEND_URL" > persist-server-2.log 2>&1 &
server_pid=$!
wait $rc
line=$(result_line persist-reconnect.log)
if grep -q NET_TEST_OK persist-reconnect.log && grep -q "Reconectado" persist-reconnect.log; then
  note "Reconexión" "el servidor se reinició y el cliente volvió solo — ${line#*\] }"
else
  grep -E "Reconect|perdida|No se pudo" persist-reconnect.log | tail -4
  error "Reconexión" "${line:-sin resultado}"
fi

# --- Contrato completo: el bot camina al depósito, acepta, compra, camina al taller y entrega ---
timeout 120 "$GODOT" --headless --path game -- \
  --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
  --account bot_contrato --password "clave-de-bot-123" --character "Bot Changarin" \
  --bot-contract --test-seconds 60 --expect-players 1 > persist-contract.log 2>&1
line=$(result_line persist-contract.log)
if grep -q NET_TEST_OK persist-contract.log; then
  note "Contrato" "${line#*\] }"
else
  grep "bot-contrato" persist-contract.log | tail -8
  error "Contrato" "${line:-sin resultado}"
fi

# --- Pedido urgente (contrato 2): Moretti → Oficina Valenti con plazo de 3 minutos ---
timeout 120 "$GODOT" --headless --path game -- \
  --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
  --account bot_urgente --password "clave-de-bot-123" --character "Bot Apurado" \
  --bot-contract-code pedido-urgente --test-seconds 60 --expect-players 1 > persist-urgent.log 2>&1
line=$(result_line persist-urgent.log)
if grep -q NET_TEST_OK persist-urgent.log; then
  note "Pedido urgente" "${line#*\] }"
else
  grep "bot-contrato" persist-urgent.log | tail -8
  error "Pedido urgente" "${line:-sin resultado}"
fi

# --- Negocio: licencia, reponer, ventas automáticas por ciclo y retiro ---
timeout 150 "$GODOT" --headless --path game -- \
  --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
  --account bot_negocio --password "clave-de-bot-123" --character "Bot Cafetero" \
  --bot-business --test-seconds 90 --expect-players 1 > persist-business.log 2>&1
line=$(result_line persist-business.log)
if grep -q NET_TEST_OK persist-business.log; then
  note "Negocio" "${line#*\] }"
else
  grep "bot-negocio" persist-business.log | tail -6
  error "Negocio" "${line:-sin resultado}"
fi

ledger=$(PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -At -F ' ' -c "
  SELECT (SELECT count(*) FROM distrito.ledger_transactions),
         (SELECT COALESCE(sum(balance_cents),0) FROM distrito.economic_accounts),
         (SELECT count(*) FROM (SELECT transaction_id FROM distrito.ledger_entries GROUP BY transaction_id HAVING sum(amount_cents) <> 0) x),
         (SELECT count(*) FROM distrito.item_movements)")
read -r ntx total unbalanced moves <<< "$ledger"
if [ "$total" = "0" ] && [ "$unbalanced" = "0" ]; then
  note "Libro contable" "$ntx transacciones, $moves movimientos de mercadería, suma de saldos = 0, transacciones desbalanceadas = 0"
else
  error "Libro contable" "suma de saldos=$total, desbalanceadas=$unbalanced"
fi

# --- Cuadrilla: un bot funda e invita, el otro acepta ---
crew_bot() { # log cuenta personaje modo...
  local log=$1 account=$2 character=$3; shift 3
  timeout 120 "$GODOT" --headless --path game -- \
    --client --host 127.0.0.1 --port "$PORT" --backend "$BACKEND_URL" \
    --account "$account" --password "clave-de-bot-123" --character "$character" \
    --test-seconds 60 --expect-players 2 "$@" > "$log" 2>&1
}
crew_bot persist-crew-jefe.log bot_jefe "Bot Jefe" --bot-crew-create "Los Del Barrio" & a=$!
crew_bot persist-crew-recluta.log bot_recluta "Bot Recluta" --bot-crew-join & b=$!
wait $a; wait $b
for x in jefe recluta; do
  line=$(result_line "persist-crew-$x.log")
  if grep -q NET_TEST_OK "persist-crew-$x.log"; then note "Cuadrilla ($x)" "${line#*\] }"; else grep "bot-cuadrilla" "persist-crew-$x.log" | tail -5; error "Cuadrilla ($x)" "${line:-sin resultado}"; fi
done
crew_db=$(PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -At -F ' ' -c "
  SELECT cr.name, count(m.*), string_agg(ch.name || ':' || m.role, ', ' ORDER BY m.joined_at)
  FROM distrito.crews cr JOIN distrito.crew_members m ON m.crew_id = cr.id JOIN distrito.characters ch ON ch.id = m.character_id
  WHERE cr.name = 'Los Del Barrio' GROUP BY cr.name")
if echo "$crew_db" | grep -q " 2 "; then note "Cuadrilla en la base" "$crew_db"; else error "Cuadrilla en la base" "${crew_db:-no existe}"; fi

# --- Ciclo completo en cuadrilla (contrato 3): el jefe acepta la cobranza en la Oficina Valenti
#     y la cobra en el Taller Caruso con el recluta al lado; el pago se reparte entre los dos ---
PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -q -c "
  INSERT INTO distrito.character_reputation (character_id, family_code, value)
  SELECT id, 'caruso', 5 FROM distrito.characters WHERE name = 'Bot Jefe'
  ON CONFLICT (character_id, family_code) DO UPDATE SET value = GREATEST(distrito.character_reputation.value, 5)"
crew_bot persist-collect-jefe.log bot_jefe "Bot Jefe" --bot-contract-code cobranza-caruso & a=$!
crew_bot persist-collect-recluta.log bot_recluta "Bot Recluta" --bot-goto "8.5,4.5" & b=$!
wait $a; wait $b
line=$(result_line persist-collect-jefe.log)
if grep -q NET_TEST_OK persist-collect-jefe.log; then note "Cobranza en cuadrilla (jefe)" "${line#*\] }"
else grep "bot-contrato" persist-collect-jefe.log | tail -6; error "Cobranza en cuadrilla (jefe)" "${line:-sin resultado}"; fi
collect_db=$(PGPASSWORD=distrito_dev psql -h localhost -U distrito -d distrito -At -F ' ' -c "
  SELECT string_agg(ch.name || ': valenti ' || r.value, ', ' ORDER BY ch.name)
  FROM distrito.character_reputation r JOIN distrito.characters ch ON ch.id = r.character_id
  WHERE r.family_code = 'valenti' AND ch.name IN ('Bot Jefe', 'Bot Recluta') AND r.value >= 6")
if echo "$collect_db" | grep -q "Bot Recluta"; then note "Cobranza en la base" "$collect_db (los dos ganaron reputación)"
else error "Cobranza en la base" "el recluta no recibió su parte: ${collect_db:-nadie}"; fi

# --- Cliente sin cuenta contra servidor con cuentas ---
timeout 60 "$GODOT" --headless --path game -- --client --host 127.0.0.1 --port "$PORT" --test-seconds 4 > persist-guest.log 2>&1
if grep -q "requiere iniciar sesión" persist-guest.log; then note "Invitado" "rechazado correctamente"; else error "Invitado" "el servidor con cuentas aceptó un cliente sin sesión"; fi

note "Servidor" "$(grep -c 'Guardado ' persist-server.log) guardados registrados"

kill "$server_pid" "$backend_pid" 2>/dev/null || true
exit $fail
