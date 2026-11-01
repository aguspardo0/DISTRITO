# Familias de jugadores y subfacciones

**Decisión del equipo (2026-11-01):**
- Va a haber como máximo **5 familias de jugadores** en todo el servidor, para que sean pocas y estar en una tenga valor.
- **Por ahora no se pueden fundar.**

Para todo lo que no se definió, se tomó lo recomendado:

| Tema | Elegido |
|---|---|
| Formación | Una cuadrilla funda la familia y las demás se suman como **subfacciones**. Cada cuadrilla conserva su caja, sede y misiones, y su jefe pasa a ser Caporegime |
| Requisitos para fundar | Prestigio rango 5, el jefe nivel 10 y $1.000 de la caja (los cobra la Oficina Valenti por los papeles) |
| Rangos | Los del diseño: Don, Consigliere, Sottocapo, Caporegime, Miembro, Asociado |
| Relación con las familias NPC | Podrá jurar lealtad a una. Queda para cuando se abra la fundación |

## Qué hay hoy
- **Base de datos:** `player_families` (nombre, cuadrilla fundadora, Don) y `crews.family_id` (de qué familia es subfacción cada cuadrilla).
- **Tablero (J):** «Familias de jugadores: 0 de 5», las que existan con su Don y sus subfacciones, el aviso de que todavía no se pueden fundar y qué va a hacer falta.
- **Fundación** (`POST /internal/families/found`):
  - Rechaza con `not_enabled` mientras esté cerrada.
  - Habilitada, valida el nombre, el tope de 5 (con bloqueo, para que dos pedidos a la vez no lo pasen) y los requisitos.
  - Cobra la fundación y deja a la cuadrilla como primera subfacción.

## Cómo se abre
- En el repo: `config/familias.json` → `"fundacionHabilitada": true`.
- En el servidor, sin tocar el repo: variable de entorno `Families__FoundingEnabled=true`.
- El máximo (`"maximo"`) se puede bajar, pero nunca pasa de 5.

## Pendiente para cuando se abra
- Sumar y quitar cuadrillas como subfacciones (invitación del Don y aceptación del jefe).
- Independizarse.
- Consigliere y Sottocapo.
- Permisos configurables con historial.
- Lealtad a una familia NPC.

## Pruebas
`PlayerFamilyApiTests`:
- cerrada por la configuración del repo, con un máximo de 5;
- habilitada: requisitos, nombre, fundación y «ya tiene familia».
