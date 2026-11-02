-- 0034 — Negocios que crecen (aprobado por el equipo 2026-11-02): fama, mejoras sin tope (capacidad, calidad, atracción),
-- precio elegido por el dueño y la nota del crítico de La Gaceta. La clientela sale de la gente de Bellmont (config/negocios.json).
ALTER TABLE distrito.businesses
    ADD COLUMN fame                double precision NOT NULL DEFAULT 0 CHECK (fame >= 0),
    ADD COLUMN price_cents         bigint           CHECK (price_cents > 0),   -- NULL: el precio de referencia del rubro
    ADD COLUMN level_capacity      integer          NOT NULL DEFAULT 0 CHECK (level_capacity >= 0),
    ADD COLUMN level_quality       integer          NOT NULL DEFAULT 0 CHECK (level_quality >= 0),
    ADD COLUMN level_attraction    integer          NOT NULL DEFAULT 0 CHECK (level_attraction >= 0),
    ADD COLUMN customer_carry      double precision NOT NULL DEFAULT 0,       -- fracción de cliente que pasa al ciclo siguiente
    ADD COLUMN last_customers      integer          NOT NULL DEFAULT 0;       -- atendidos en el último ciclo (gente que se ve en el local)

ALTER TABLE distrito.business_cycles ADD COLUMN fame double precision;

-- Una nota por local y por día.
CREATE TABLE distrito.business_reviews (
    business_id   uuid        NOT NULL REFERENCES distrito.businesses (id),
    day           date        NOT NULL,
    stars         integer     NOT NULL CHECK (stars BETWEEN 1 AND 5),
    text          text        NOT NULL,
    fame_delta    double precision NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (business_id, day)
);

-- Mejoras compradas (historial; la plata va al Taller Caruso en el libro contable).
CREATE TABLE distrito.business_upgrades (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    business_id   uuid        NOT NULL REFERENCES distrito.businesses (id),
    branch        text        NOT NULL CHECK (branch IN ('capacidad', 'calidad', 'atraccion')),
    level         integer     NOT NULL CHECK (level >= 1),
    cost_cents    bigint      NOT NULL CHECK (cost_cents > 0),
    transaction_id uuid       REFERENCES distrito.ledger_transactions (id),
    created_at    timestamptz NOT NULL DEFAULT now()
);
