-- 0038 — Reportes de problemas desde el juego (prueba cerrada, 2026-11-08): F8 en el juego, se ven en el panel de
-- administración. Guarda quién, dónde y qué versión, para poder reproducirlo.
CREATE TABLE distrito.bug_reports (
    id            bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    created_at    timestamptz NOT NULL DEFAULT now(),
    character_id  uuid,
    player        text        NOT NULL,
    text          text        NOT NULL CHECK (length(text) BETWEEN 1 AND 1000),
    x             real        NOT NULL,
    z             real        NOT NULL,
    zone          text        NOT NULL DEFAULT '',
    version       text        NOT NULL DEFAULT ''
);
CREATE INDEX bug_reports_created ON distrito.bug_reports (created_at DESC);
