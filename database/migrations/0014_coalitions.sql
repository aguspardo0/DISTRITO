-- 0014 — Coaliciones entre cuadrillas (Etapa E, propuesta aprobada por el equipo, 2026-10-31).
CREATE TABLE distrito.coalitions (
    id               uuid        PRIMARY KEY,
    name             text        NOT NULL,
    name_normalized  text        NOT NULL UNIQUE,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE distrito.coalition_members (
    coalition_id  uuid        NOT NULL REFERENCES distrito.coalitions (id) ON DELETE CASCADE,
    crew_id       uuid        NOT NULL UNIQUE REFERENCES distrito.crews (id) ON DELETE CASCADE,
    joined_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (coalition_id, crew_id)
);

-- Invitación de una cuadrilla a otra. Si la que invita no tiene coalición, al aceptar se crea con proposed_name.
CREATE TABLE distrito.coalition_invitations (
    from_crew_id   uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    to_crew_id     uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    proposed_name  text        NOT NULL,
    expires_at     timestamptz NOT NULL,
    PRIMARY KEY (from_crew_id, to_crew_id)
);

ALTER TABLE distrito.crews ADD COLUMN coalition_left_at timestamptz;
ALTER TABLE distrito.zone_season_results ADD COLUMN coalition_id uuid REFERENCES distrito.coalitions (id) ON DELETE SET NULL;
