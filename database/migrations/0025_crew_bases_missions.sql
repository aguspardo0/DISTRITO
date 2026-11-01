-- 0025 — Sede de cuadrilla, prestigio y misiones generadas (aprobado por el equipo 2026-11-01).
-- Sede: galpón de Los Muelles, $250 de la caja + $2 por ciclo; adentro es zona segura; depósito de 500 kg; reaparición.
CREATE TABLE distrito.crew_bases (
    id          uuid        PRIMARY KEY,
    crew_id     uuid        NOT NULL UNIQUE REFERENCES distrito.crews (id) ON DELETE CASCADE,
    shed        int         NOT NULL UNIQUE CHECK (shed >= 0),
    created_at  timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE distrito.crews ADD COLUMN prestige int NOT NULL DEFAULT 0 CHECK (prestige >= 0);

-- Tablero: misiones generadas por turno de 3 horas (idx = lugar en el tablero).
CREATE TABLE distrito.crew_missions (
    id             uuid        PRIMARY KEY,
    crew_id        uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    slot           bigint      NOT NULL,
    idx            int         NOT NULL,
    kind           text        NOT NULL,
    title          text        NOT NULL,
    detail         text        NOT NULL,
    item_code      text,
    npc_code       text,
    target         int         NOT NULL CHECK (target > 0),
    progress       int         NOT NULL DEFAULT 0,
    reward_cents   bigint      NOT NULL,
    prestige       int         NOT NULL,
    payer_family   text        NOT NULL,
    status         text        NOT NULL DEFAULT 'abierta' CHECK (status IN ('abierta', 'cumplida')),
    expires_at     timestamptz NOT NULL,
    completed_by   uuid        REFERENCES distrito.characters (id),
    completed_at   timestamptz,
    UNIQUE (crew_id, slot, idx)
);
