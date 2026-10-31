-- 0013 — Territorio (Etapa E, propuesta aprobada por el equipo, 2026-10-31): influencia por zona y temporada.
CREATE TABLE distrito.zone_influence (
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    season      integer     NOT NULL,
    zone        text        NOT NULL CHECK (zone IN ('calle-almacen', 'los-talleres', 'la-oficina', 'la-esquina')),
    crew_id     uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    points      integer     NOT NULL CHECK (points > 0),
    reason      text        NOT NULL,
    created_at  timestamptz NOT NULL
);
CREATE INDEX zone_influence_season_idx ON distrito.zone_influence (season, zone, crew_id);

-- Ganadores de cada zona al cerrar una temporada (una fila por zona; crew_id null = nadie llegó al umbral).
CREATE TABLE distrito.zone_season_results (
    season      integer     NOT NULL,
    zone        text        NOT NULL,
    crew_id     uuid        REFERENCES distrito.crews (id) ON DELETE SET NULL,
    points      integer     NOT NULL,
    closed_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (season, zone)
);
