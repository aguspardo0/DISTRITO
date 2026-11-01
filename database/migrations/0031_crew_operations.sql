-- 0031 — Operaciones de cuadrilla (propuesta aprobada por el equipo, 2026-11-01): una por día y por cuadrilla, tres
-- etapas en orden; paga al final a la caja y suma prestigio. Cada etapa es una misión del tablero (crew_missions) con
-- slot = −día e idx = 100 + etapa, así se cumple con la misma lógica que las misiones (lugar, depósito o evento).
CREATE TABLE distrito.crew_operations (
    crew_id       uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    day           int         NOT NULL,
    code          text        NOT NULL,
    status        text        NOT NULL CHECK (status IN ('en curso', 'cumplida')),
    reward_cents  bigint      NOT NULL CHECK (reward_cents >= 0),
    prestige      int         NOT NULL CHECK (prestige >= 0),
    started_by    uuid        NOT NULL REFERENCES distrito.characters (id),
    started_at    timestamptz NOT NULL,
    finished_at   timestamptz,
    PRIMARY KEY (crew_id, day)
);

ALTER TABLE distrito.crew_missions
    ADD COLUMN operation_day   int,
    ADD COLUMN operation_stage int CHECK (operation_stage BETWEEN 0 AND 2);
