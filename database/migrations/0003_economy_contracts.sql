-- 0003 — Economía base: cuentas, libro contable de doble entrada, mercancías y contratos.
-- Reglas (ver ADR-003):
--   * El dinero solo se crea desde la cuenta 'system:emision' y siempre por una transacción del libro.
--   * Cada transacción tiene asientos que suman cero. El saldo guardado es un caché del libro.
--   * Las mercancías tienen dueño y ubicación; todo movimiento queda registrado.

CREATE TABLE distrito.economic_accounts (
    id              uuid    PRIMARY KEY,
    kind            text    NOT NULL CHECK (kind IN ('system', 'npc', 'character')),
    code            text    UNIQUE,                                         -- cuentas de sistema y NPC
    character_id    uuid    UNIQUE REFERENCES distrito.characters (id),     -- cuentas de personaje
    display_name    text    NOT NULL,
    balance_cents   bigint  NOT NULL DEFAULT 0,
    allow_negative  boolean NOT NULL DEFAULT false,
    CHECK (allow_negative OR balance_cents >= 0),
    CHECK ((kind = 'character') = (character_id IS NOT NULL)),
    CHECK ((kind = 'character') = (code IS NULL))
);

CREATE TABLE distrito.ledger_transactions (
    id               uuid        PRIMARY KEY,
    idempotency_key  text        NOT NULL UNIQUE,
    kind             text        NOT NULL,
    description      text        NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE distrito.ledger_entries (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transaction_id  uuid   NOT NULL REFERENCES distrito.ledger_transactions (id),
    account_id      uuid   NOT NULL REFERENCES distrito.economic_accounts (id),
    amount_cents    bigint NOT NULL CHECK (amount_cents <> 0)
);
CREATE INDEX ledger_entries_account_idx ON distrito.ledger_entries (account_id);
CREATE INDEX ledger_entries_transaction_idx ON distrito.ledger_entries (transaction_id);

CREATE TABLE distrito.item_definitions (
    code                   text    PRIMARY KEY,
    name                   text    NOT NULL,
    category               text    NOT NULL,
    reference_price_cents  bigint  NOT NULL CHECK (reference_price_cents >= 0),
    weight_kg              numeric NOT NULL CHECK (weight_kg >= 0),
    volume_l               numeric NOT NULL CHECK (volume_l >= 0),
    legal_status           text    NOT NULL CHECK (legal_status IN ('legal', 'restricted', 'illegal'))
);

CREATE TABLE distrito.item_stacks (
    id                uuid    PRIMARY KEY,
    owner_account_id  uuid    NOT NULL REFERENCES distrito.economic_accounts (id),
    item_code         text    NOT NULL REFERENCES distrito.item_definitions (code),
    location          text    NOT NULL,
    quantity          integer NOT NULL CHECK (quantity > 0),
    UNIQUE (owner_account_id, item_code, location)
);

CREATE TABLE distrito.item_movements (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transaction_id   uuid        NOT NULL REFERENCES distrito.ledger_transactions (id),
    item_code        text        NOT NULL REFERENCES distrito.item_definitions (code),
    from_account_id  uuid        NOT NULL REFERENCES distrito.economic_accounts (id),
    from_location    text        NOT NULL,
    to_account_id    uuid        NOT NULL REFERENCES distrito.economic_accounts (id),
    to_location      text        NOT NULL,
    quantity         integer     NOT NULL CHECK (quantity > 0),
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE distrito.contracts (
    id                          uuid        PRIMARY KEY,
    template_code               text        NOT NULL,
    character_id                uuid        NOT NULL REFERENCES distrito.characters (id),
    status                      text        NOT NULL CHECK (status IN ('active', 'completed', 'abandoned')),
    accept_key                  text        NOT NULL UNIQUE,
    accepted_at                 timestamptz NOT NULL DEFAULT now(),
    completed_at                timestamptz,
    completion_transaction_id   uuid        REFERENCES distrito.ledger_transactions (id)
);
CREATE UNIQUE INDEX contracts_one_active_per_template
    ON distrito.contracts (character_id, template_code) WHERE status = 'active';

-- ---------- Datos iniciales de prueba (barrio de prueba) ----------

INSERT INTO distrito.economic_accounts (id, kind, code, display_name, allow_negative) VALUES
    ('00000000-0000-7000-8000-000000000001', 'system', 'system:emision', 'Emisión de moneda (sistema)', true),
    ('00000000-0000-7000-8000-000000000101', 'npc', 'npc:deposito-delgado', 'Depósito Delgado (NPC de prueba)', false),
    ('00000000-0000-7000-8000-000000000102', 'npc', 'npc:taller-moreno', 'Taller Moreno (NPC de prueba)', false);

INSERT INTO distrito.item_definitions (code, name, category, reference_price_cents, weight_kg, volume_l, legal_status) VALUES
    ('caja-repuestos', 'Caja de repuestos', 'vehiculos-repuestos', 800, 12, 30, 'legal');

-- Fondos iniciales del taller (paga los contratos de entrega).
INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description) VALUES
    ('00000000-0000-7000-8000-000000001001', 'seed:fondos-taller-moreno', 'seed', 'Fondos iniciales del Taller Moreno');
INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents) VALUES
    ('00000000-0000-7000-8000-000000001001', '00000000-0000-7000-8000-000000000001', -10000000),
    ('00000000-0000-7000-8000-000000001001', '00000000-0000-7000-8000-000000000102',  10000000);
UPDATE distrito.economic_accounts SET balance_cents = balance_cents - 10000000 WHERE code = 'system:emision';
UPDATE distrito.economic_accounts SET balance_cents = balance_cents + 10000000 WHERE code = 'npc:taller-moreno';

-- Stock inicial del depósito.
INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES
    ('00000000-0000-7000-8000-000000002001', '00000000-0000-7000-8000-000000000101', 'caja-repuestos', 'deposito-delgado', 100000);
