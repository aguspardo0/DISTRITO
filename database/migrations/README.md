# Migraciones

Archivos SQL numerados (`0002_accounts.sql`, `0003_...`). Cada uno inserta su fila en `distrito.schema_migrations`.

`database/init/0001_schema.sql` es la migración base; Docker la ejecuta al crear el contenedor por primera vez.

**Pendiente (ADR-001):** cómo se aplican las migraciones siguientes de forma automática. Se decide antes de crear las tablas del hito 1.
