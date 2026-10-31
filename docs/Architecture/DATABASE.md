# Base de datos

## Motor

PostgreSQL 16 (desarrollo local vía Docker Compose; en CI como servicio de GitHub Actions).

## Principios

1. **Identificadores estables:** toda entidad importante usa `uuid` como clave primaria, generado una sola vez y nunca reutilizado. Nada de IDs relativos a una ciudad o a una sesión.
2. **Persistente vs. temporal:** posiciones en tiempo real, sesiones de red y entidades cercanas viven en memoria del servidor de juego. A la base solo va lo que debe sobrevivir a un reinicio (personaje, inventario, dinero, contratos, relaciones), con guardados en momentos definidos.
3. **Integridad transaccional:** toda operación económica (pago, transferencia de mercancía, cierre de contrato) es **una transacción** que se completa entera o no ocurre.
4. **Registro económico (ledger) auditable:** el dinero no se guarda solo como un número que se pisa. Cada movimiento queda como asiento inmutable (quién, a quién, cuánto, por qué, cuándo, referencia a la operación). El saldo se puede reconstruir desde el registro.
5. **Dinero en enteros:** montos en la unidad mínima (centavos) como `bigint`. Nunca `float`.
6. **Idempotencia:** cada operación económica lleva un identificador único de solicitud para que un reintento no la duplique.

## Esquema

Implementado (migración 0002):

| Tabla | Propósito |
|---|---|
| `accounts` | Cuenta: usuario único (sin distinguir mayúsculas) y hash PBKDF2 |
| `sessions` | Sesiones de login: SHA-256 del token + vencimiento |
| `characters` | Personaje: cuenta dueña, nombre único global, zona y posición |

Implementado (migración 0003, ver ADR-003): `economic_accounts`, `ledger_transactions`, `ledger_entries`, `item_definitions`, `item_stacks`, `item_movements`, `contracts`.

Pendiente para el resto del hito 1:

| Tabla | Propósito |
|---|---|
| `item_definitions` | Tipos de mercancía |
| `item_stacks` | Mercancía concreta: propietario + ubicación + cantidad |
| `ledger_entries` | Asientos económicos inmutables |
| `contracts` | Contratos y su estado |
| `crews` / `crew_members` | Cuadrillas |

## Migraciones

Archivos SQL numerados en `database/migrations/`, aplicados por el backend al arrancar. Ver ADR-002 y `database/migrations/README.md`.
