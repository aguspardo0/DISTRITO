-- 0004 — Cuadrillas (estructura organizacional básica del hito 1). Ver ADR-004.

CREATE TABLE distrito.crews (
    id                   uuid        PRIMARY KEY,
    name                 text        NOT NULL,
    name_normalized      text        NOT NULL UNIQUE,
    leader_character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    created_at           timestamptz NOT NULL DEFAULT now()
);

-- Un personaje pertenece como máximo a una cuadrilla (character_id es único).
CREATE TABLE distrito.crew_members (
    crew_id       uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    character_id  uuid        NOT NULL UNIQUE REFERENCES distrito.characters (id),
    role          text        NOT NULL CHECK (role IN ('leader', 'member')),
    joined_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (crew_id, character_id)
);

CREATE TABLE distrito.crew_invitations (
    crew_id       uuid        NOT NULL REFERENCES distrito.crews (id) ON DELETE CASCADE,
    character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    invited_by    uuid        NOT NULL REFERENCES distrito.characters (id),
    created_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz NOT NULL,
    PRIMARY KEY (crew_id, character_id)
);
CREATE INDEX crew_invitations_character_idx ON distrito.crew_invitations (character_id);
