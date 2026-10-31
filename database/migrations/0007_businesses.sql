-- 0007 — Primer negocio administrado por jugadores: puesto de café (objetivo verificable del documento de diseño).
-- Cada negocio tiene su propia cuenta económica (su caja), separada de la del dueño. Ver ADR-005.

-- Las cuentas pueden ser de negocios (sin código ni personaje).
ALTER TABLE distrito.economic_accounts DROP CONSTRAINT economic_accounts_kind_check;
ALTER TABLE distrito.economic_accounts DROP CONSTRAINT economic_accounts_check2;
ALTER TABLE distrito.economic_accounts ADD CONSTRAINT economic_accounts_kind_check
    CHECK (kind IN ('system', 'npc', 'character', 'business'));
ALTER TABLE distrito.economic_accounts ADD CONSTRAINT economic_accounts_code_check
    CHECK ((kind IN ('system', 'npc')) = (code IS NOT NULL));

CREATE TABLE distrito.businesses (
    id                   uuid        PRIMARY KEY,
    kind                 text        NOT NULL CHECK (kind IN ('puesto-cafe')),
    name                 text        NOT NULL,
    owner_character_id   uuid        NOT NULL UNIQUE REFERENCES distrito.characters (id), -- por ahora, un negocio por personaje
    account_id           uuid        NOT NULL UNIQUE REFERENCES distrito.economic_accounts (id),
    location             text        NOT NULL,
    last_cycle           bigint      NOT NULL,
    created_at           timestamptz NOT NULL DEFAULT now()
);

-- Resultado de cada ciclo económico (permite reconstruir facturación, costos y ganancia).
CREATE TABLE distrito.business_cycles (
    business_id     uuid        NOT NULL REFERENCES distrito.businesses (id),
    cycle           bigint      NOT NULL,
    units_sold      integer     NOT NULL CHECK (units_sold >= 0),
    revenue_cents   bigint      NOT NULL CHECK (revenue_cents >= 0),
    rent_cents      bigint      NOT NULL CHECK (rent_cents >= 0),
    transaction_id  uuid        REFERENCES distrito.ledger_transactions (id),
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (business_id, cycle)
);

INSERT INTO distrito.economic_accounts (id, kind, code, display_name, allow_negative) VALUES
    ('00000000-0000-7000-8000-000000000103', 'npc', 'npc:oficina-valenti', 'Oficina Valenti (NPC de prueba)', false),
    -- Los clientes de Bellmont: población NPC que compra en los negocios. Puede quedar negativa como la emisión.
    ('00000000-0000-7000-8000-000000000002', 'system', 'system:poblacion', 'Población de Bellmont (clientes)', true);

INSERT INTO distrito.item_definitions (code, name, category, reference_price_cents, weight_kg, volume_l, legal_status) VALUES
    ('bolsa-cafe', 'Bolsa de café', 'alimentos', 300, 5, 8, 'legal');

INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES
    ('00000000-0000-7000-8000-000000002002', '00000000-0000-7000-8000-000000000101', 'bolsa-cafe', 'deposito-delgado', 100000);
