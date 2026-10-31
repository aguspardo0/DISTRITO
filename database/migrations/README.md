# Migraciones

Archivos SQL numerados: `NNNN_descripcion.sql`. Los aplica el backend al arrancar (`MigrationRunner`), en orden, cada uno en su propia transacción, y los registra en `distrito.schema_migrations`.

Reglas:
- **Nunca** modificar una migración ya aplicada en algún entorno. Para cambiar algo, crear una nueva.
- No insertar en `schema_migrations` desde el SQL: lo hace el runner.
- Un candado de PostgreSQL (`pg_advisory_lock`) evita que dos backends migren a la vez.

Ver ADR-002.
