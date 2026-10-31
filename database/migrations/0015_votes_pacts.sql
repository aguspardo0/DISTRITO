-- 0015 — Votaciones en la cuadrilla y acuerdos con las familias (Etapa E, propuesta aprobada por el equipo, 2026-10-31).
CREATE TABLE distrito.crew_votes (
    id                   uuid        PRIMARY KEY,
    crew_id              uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    kind                 text        NOT NULL CHECK (kind IN ('leader', 'payout')),
    target_character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    amount_cents         bigint      NOT NULL DEFAULT 0 CHECK (amount_cents >= 0),
    opened_by            uuid        NOT NULL REFERENCES distrito.characters (id),
    opened_at            timestamptz NOT NULL,
    closes_at            timestamptz NOT NULL,
    status               text        NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'passed', 'failed')),
    closed_at            timestamptz
);
CREATE UNIQUE INDEX crew_votes_one_open ON distrito.crew_votes (crew_id) WHERE status = 'open';

CREATE TABLE distrito.crew_vote_ballots (
    vote_id       uuid    NOT NULL REFERENCES distrito.crew_votes (id) ON DELETE CASCADE,
    character_id  uuid    NOT NULL REFERENCES distrito.characters (id),
    yes           boolean NOT NULL,
    PRIMARY KEY (vote_id, character_id)
);

-- Acuerdo de una familia con una cuadrilla (o su coalición) por temporada: uno por familia y temporada.
CREATE TABLE distrito.family_pacts (
    season        integer     NOT NULL,
    family_code   text        NOT NULL REFERENCES distrito.families (code),
    crew_id       uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    coalition_id  uuid        REFERENCES distrito.coalitions (id) ON DELETE SET NULL,
    signed_at     timestamptz NOT NULL,
    PRIMARY KEY (season, family_code)
);
