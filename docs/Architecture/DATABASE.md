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

Por ahora la migración inicial solo crea el esquema `distrito` y una tabla de control de migraciones. Las tablas de cuentas, personajes, inventarios y ledger se diseñan en la etapa del hito 1 y se documentan acá antes de crearlas.

Borrador de entidades para el hito 1 (no implementado):

| Tabla | Propósito |
|---|---|
| `accounts` | Cuenta de usuario |
| `characters` | Personaje (pertenece a una cuenta) |
| `item_definitions` | Tipos de mercancía |
| `item_stacks` | Mercancía concreta: propietario + ubicación + cantidad |
| `ledger_entries` | Asientos económicos inmutables |
| `contracts` | Contratos y su estado |
| `crews` / `crew_members` | Cuadrillas |

## Migraciones

Archivos SQL numerados en `database/migrations/` (`0001_...sql`). **Pendiente:** elegir cómo se aplican (herramienta propia mínima, DbUp, o migraciones de EF Core). Ver ADR-001 → decisiones pendientes.
