-- 0029 — Encargos personales (propuesta aprobada por el equipo, 2026-11-01).
-- Los ofrecidos no se guardan: se calculan con una semilla por personaje y turno (EncargoGenerator) y tienen un id
-- estable. Acá queda cada encargo desde que se toma: su paso, su progreso y cómo terminó.
CREATE TABLE distrito.character_encargos (
    id            uuid        PRIMARY KEY,
    character_id  uuid        NOT NULL REFERENCES distrito.characters (id) ON DELETE CASCADE,
    kind          text        NOT NULL,
    giver_npc     text        NOT NULL,
    family        text        NOT NULL,
    title         text        NOT NULL,
    detail        text        NOT NULL,
    item_code     text,
    quantity      int         NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    target_npc    text,
    progress      int         NOT NULL DEFAULT 0 CHECK (progress >= 0),
    target        int         NOT NULL CHECK (target > 0),
    stage         int         NOT NULL DEFAULT 0 CHECK (stage IN (0, 1)),
    reward_cents  bigint      NOT NULL CHECK (reward_cents >= 0),
    xp            int         NOT NULL CHECK (xp >= 0),
    reputation    int         NOT NULL CHECK (reputation >= 0),
    daily         boolean     NOT NULL DEFAULT false,
    status        text        NOT NULL CHECK (status IN ('activo', 'cumplido', 'vencido', 'abandonado')),
    accepted_at   timestamptz NOT NULL,
    expires_at    timestamptz NOT NULL,
    fast_until    timestamptz,
    finished_at   timestamptz,
    paid_cents    bigint
);
CREATE INDEX character_encargos_active ON distrito.character_encargos (character_id) WHERE status = 'activo';

-- Objetos de encargo: los crea el encargo y se van con él (no se venden; si se pierden en la calle, no sirven a otro).
INSERT INTO distrito.item_definitions (code, name, category, reference_price_cents, weight_kg, volume_l, legal_status, kind, description, stackable) VALUES
    ('sobre-cobro', 'Sobre de una cobranza', 'encargos', 0, 0.1, 0, 'legal', 'documento',
     'La plata de una deuda cobrada. Hay que llevársela a quien la encargó; si se pierde, la pagás vos.', true),
    ('sobre-sellado', 'Sobre sellado', 'encargos', 0, 0.1, 0, 'legal', 'documento',
     'Un sobre cerrado con el sello de una familia. No es asunto tuyo lo que dice.', true),
    ('paquete-sellado', 'Paquete sellado', 'encargos', 0, 3, 4, 'restricted', 'mision',
     'Llegó por barco y no tiene papeles. Se lleva sin abrir.', true);
