-- 0017 — Guerras entre cuadrillas (PvP regulado, decisión del equipo 2026-10-31).
CREATE TABLE distrito.crew_wars (
    id              uuid        PRIMARY KEY,
    attacker_crew   uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    defender_crew   uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    declared_by     uuid        NOT NULL REFERENCES distrito.characters (id),
    declared_at     timestamptz NOT NULL,
    starts_at       timestamptz NOT NULL,
    ends_at         timestamptz NOT NULL,
    CHECK (attacker_crew <> defender_crew)
);
CREATE INDEX crew_wars_active_idx ON distrito.crew_wars (ends_at);
