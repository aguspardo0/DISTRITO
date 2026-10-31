# ADR-003 — Economía base: libro contable, mercancías y contratos

- **Estado:** aceptada
- **Fecha:** 2026-10-31

## Contexto

El hito 1 exige que el servidor valide pagos y transferencias de mercancía. Los principios del proyecto piden integridad transaccional y registros auditables desde el primer día.

## Decisiones

### Dinero: libro de doble entrada
- Toda cuenta que puede tener dinero (personaje, NPC, sistema) está en `economic_accounts`.
- Cada operación crea una `ledger_transaction` con asientos (`ledger_entries`) que **suman cero**.
- El dinero solo nace en la cuenta `system:emision` (la única con saldo negativo permitido). La suma de todos los saldos es siempre cero.
- `balance_cents` es un caché que se actualiza en la misma transacción; un `CHECK` impide saldos negativos en cuentas normales. Los tests verifican que el caché coincida con el libro.

### Mercancías: dueño + ubicación + movimiento
- `item_stacks` guarda cantidad por (dueño, mercancía, ubicación). Lo que lleva un personaje encima tiene ubicación `encima`.
- Todo traslado queda en `item_movements`, ligado a la transacción contable que lo causó.

### Concurrencia e idempotencia
- Las cuentas se bloquean con `SELECT … FOR UPDATE` siempre en orden de id (sin interbloqueos).
- Cada acción lleva un `RequestId` único. Reintentarlo no cobra ni entrega dos veces.

### Contratos
- `contracts` guarda estado (`active`, `completed`, `abandoned`). Un índice único parcial impide dos contratos activos del mismo tipo para el mismo personaje.
- Las plantillas de contrato viven por ahora en código (`EconomyCatalog`). Se pasan a la base cuando haya más de unas pocas.

### Quién decide qué
- **Cliente:** pide hablar con un NPC y elige una opción. Nada más.
- **Servidor de juego:** verifica sesión, distancia al NPC y que la opción estuviera disponible (`NpcInteractionRules.Resolve`).
- **Backend:** valida saldo, stock y estado del contrato dentro de la transacción. Es la última palabra.

## Valores provisionales (no son diseño de juego)

| Valor | Provisional |
|---|---|
| Fondos iniciales de un personaje | $20.00 |
| Caja de repuestos | $8.00 |
| Pago del contrato de entrega | $15.00 |
| NPCs | Depósito Delgado y Taller Moreno ("de prueba") |
| Stock del depósito / fondos del taller | grandes y fijos; todavía no hay producción ni reposición |

## Pendiente

Oferta y demanda, producción, reposición de stock, empresas, impuestos, transporte entre ubicaciones con tiempo y riesgo. La estructura (cuentas, libro, stacks con ubicación) está pensada para soportarlos sin rehacerse.
