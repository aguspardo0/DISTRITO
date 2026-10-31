-- 0006 — Cuadrilla progresiva y sucesión por inactividad (decisiones del equipo, 2026-10-31).

-- Última vez que el personaje estuvo en el mundo (entrar o guardar posición la actualizan).
ALTER TABLE distrito.characters ADD COLUMN last_seen_at timestamptz NOT NULL DEFAULT now();

-- Nivel de la cuadrilla: define el cupo (nivel 1 = 5). Sube con el progreso colectivo en etapas futuras.
ALTER TABLE distrito.crews ADD COLUMN level integer NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 5);

-- Historial auditable de la cuadrilla (altas, bajas, sucesiones).
CREATE TABLE distrito.crew_events (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    crew_id       uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    kind          text        NOT NULL,
    character_id  uuid        REFERENCES distrito.characters (id),
    detail        text        NOT NULL DEFAULT '',
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX crew_events_crew_idx ON distrito.crew_events (crew_id);
