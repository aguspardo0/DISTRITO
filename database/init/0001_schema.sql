-- 0001 — Esquema base de DISTRITO.
-- Solo crea el esquema y la tabla de control de migraciones.
-- Las tablas de juego se agregan en migraciones numeradas cuando se diseñen (ver docs/Architecture/DATABASE.md).

CREATE SCHEMA IF NOT EXISTS distrito;

CREATE TABLE IF NOT EXISTS distrito.schema_migrations (
    version     integer     PRIMARY KEY,
    name        text        NOT NULL,
    applied_at  timestamptz NOT NULL DEFAULT now()
);

INSERT INTO distrito.schema_migrations (version, name)
VALUES (1, '0001_schema')
ON CONFLICT (version) DO NOTHING;
