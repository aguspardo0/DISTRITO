-- 0009 — Progresión individual (Etapa D, propuesta aprobada por el equipo 2026-10-31):
--   experiencia (nivel 1–30) y práctica de los oficios Logística, Negociación y Administración (nivel 1–10).
--   El nivel se calcula a partir de estos acumulados (ver Distrito.Shared.Progression).
CREATE TABLE distrito.character_progress (
    character_id    uuid    PRIMARY KEY REFERENCES distrito.characters (id) ON DELETE CASCADE,
    xp              bigint  NOT NULL DEFAULT 0 CHECK (xp >= 0),
    logistics       int     NOT NULL DEFAULT 0 CHECK (logistics >= 0),
    negotiation     int     NOT NULL DEFAULT 0 CHECK (negotiation >= 0),
    administration  int     NOT NULL DEFAULT 0 CHECK (administration >= 0),
    updated_at      timestamptz NOT NULL DEFAULT now()
);
