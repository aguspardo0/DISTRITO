-- 0033 — Lo que el jugador ve del mercado (aprobado por el equipo 2026-11-02): libreta de precios, diario, informante
-- y libro de cuentas. La plata del diario y del informante va a sus cuentas (no se crea ni se destruye).

INSERT INTO distrito.economic_accounts (id, kind, code, display_name, allow_negative) VALUES
    ('00000000-0000-7000-8000-000000003001', 'npc', 'npc:kiosco', 'Kiosco de diarios', false),
    ('00000000-0000-7000-8000-000000003002', 'npc', 'npc:informante', 'Nino, el que sabe', false)
ON CONFLICT (code) DO NOTHING;

-- Libreta: el último precio que vio cada personaje en cada pizarra.
CREATE TABLE distrito.price_notes (
    character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    npc_code      text        NOT NULL,
    place         text        NOT NULL,
    item_code     text        NOT NULL,
    name          text        NOT NULL,
    buying        boolean     NOT NULL,
    price_cents   bigint      NOT NULL CHECK (price_cents >= 0),
    in_stock      boolean     NOT NULL,
    seen_at       timestamptz NOT NULL,
    PRIMARY KEY (character_id, npc_code, item_code, buying)
);

-- Una edición por día: se escribe la primera vez que alguien la pide y queda fija.
CREATE TABLE distrito.newspaper_editions (
    day           date        PRIMARY KEY,
    content       jsonb       NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- Precios del día de cada edición (para las flechas de suba y baja).
CREATE TABLE distrito.market_prices (
    day           date        NOT NULL,
    npc_code      text        NOT NULL,
    item_code     text        NOT NULL,
    buying        boolean     NOT NULL,
    price_cents   bigint      NOT NULL,
    in_stock      boolean     NOT NULL,
    PRIMARY KEY (day, npc_code, item_code, buying)
);

CREATE TABLE distrito.newspaper_purchases (
    character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    day           date        NOT NULL,
    bought_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (character_id, day)
);

CREATE TABLE distrito.informant_reports (
    character_id  uuid        PRIMARY KEY REFERENCES distrito.characters (id),
    valid_until   timestamptz NOT NULL
);

-- Libro de cuentas: cuántos clientes quisieron comprar en cada ciclo (los que se fueron sin comprar = quisieron − vendidas).
-- Los ciclos con el local cerrado por falta de mercadería quedan anotados con 0 ventas (sin plata de por medio).
ALTER TABLE distrito.business_cycles ADD COLUMN units_wanted integer CHECK (units_wanted >= 0);

-- El diario cuenta asaltos y entregas de las últimas 24 horas.
CREATE INDEX ledger_transactions_kind_time_idx ON distrito.ledger_transactions (kind, created_at);
