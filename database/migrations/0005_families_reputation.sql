-- 0005 — Familias NPC de Bellmont y reputación de los personajes con cada una.
-- Fuente: documento de diseño conceptual v0.1 (familias Moretti, Valenti, Caruso; reputación 0–1000).

CREATE TABLE distrito.families (
    code            text PRIMARY KEY,
    name            text NOT NULL,
    specialization  text NOT NULL
);

INSERT INTO distrito.families (code, name, specialization) VALUES
    ('moretti', 'Familia Moretti', 'Comercio y puertos'),
    ('valenti', 'Familia Valenti', 'Política y finanzas'),
    ('caruso',  'Familia Caruso',  'Industria y transporte');

CREATE TABLE distrito.character_reputation (
    character_id  uuid    NOT NULL REFERENCES distrito.characters (id),
    family_code   text    NOT NULL REFERENCES distrito.families (code),
    value         integer NOT NULL DEFAULT 0 CHECK (value BETWEEN 0 AND 1000),
    PRIMARY KEY (character_id, family_code)
);

-- Todo cambio de reputación queda registrado (auditable), ligado a la operación que lo causó.
CREATE TABLE distrito.reputation_events (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    character_id    uuid        NOT NULL REFERENCES distrito.characters (id),
    family_code     text        NOT NULL REFERENCES distrito.families (code),
    delta           integer     NOT NULL,
    reason          text        NOT NULL,
    transaction_id  uuid        REFERENCES distrito.ledger_transactions (id),
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX reputation_events_character_idx ON distrito.reputation_events (character_id);

-- Los NPC de prueba pasan a pertenecer a las familias del documento (los códigos no cambian: son identificadores estables).
UPDATE distrito.economic_accounts SET display_name = 'Almacén Moretti (NPC de prueba)' WHERE code = 'npc:deposito-delgado';
UPDATE distrito.economic_accounts SET display_name = 'Taller Caruso (NPC de prueba)'   WHERE code = 'npc:taller-moreno';
