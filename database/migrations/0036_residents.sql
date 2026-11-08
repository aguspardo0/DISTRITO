-- 0036 — Vida autónoma de los NPC, etapa 1 (documento del equipo 2026-11-01; decisiones 2026-11-08): habitantes con
-- identidad, su último estado y los hechos importantes de su vida. No mueven plata: la clientela de la economía sigue
-- siendo la de los habitantes simulados; los NPC visibles son ambientación.

CREATE TABLE distrito.npc_residents (
    id          uuid        PRIMARY KEY,
    created_at  timestamptz NOT NULL DEFAULT now(),
    identity    jsonb       NOT NULL
);

-- Lo que está haciendo y dónde: se guarda cada pocos minutos, no cada paso.
CREATE TABLE distrito.npc_resident_states (
    resident_id uuid        PRIMARY KEY REFERENCES distrito.npc_residents (id),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    state       jsonb       NOT NULL
);

-- Hechos importantes (consiguió trabajo, vio un robo, se peleó...). No cada movimiento.
CREATE TABLE distrito.npc_resident_events (
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    resident_id uuid        NOT NULL REFERENCES distrito.npc_residents (id),
    at          timestamptz NOT NULL,
    kind        text        NOT NULL,
    text        text        NOT NULL
);
CREATE INDEX npc_resident_events_resident_idx ON distrito.npc_resident_events (resident_id, at DESC);
