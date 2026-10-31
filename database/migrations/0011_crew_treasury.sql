-- 0011 — Caja común de la cuadrilla y permisos por rol (propuesta aprobada por el equipo, 2026-10-31).
ALTER TABLE distrito.economic_accounts DROP CONSTRAINT economic_accounts_kind_check;
ALTER TABLE distrito.economic_accounts ADD CONSTRAINT economic_accounts_kind_check
    CHECK (kind IN ('system', 'npc', 'character', 'business', 'crew'));

ALTER TABLE distrito.crews
    ADD COLUMN account_id uuid UNIQUE REFERENCES distrito.economic_accounts (id),
    ADD COLUMN second_can_withdraw boolean NOT NULL DEFAULT false,
    ADD COLUMN second_can_invite   boolean NOT NULL DEFAULT true,
    ADD COLUMN member_can_invite   boolean NOT NULL DEFAULT false,
    ADD COLUMN contract_tithe_pct  integer NOT NULL DEFAULT 0 CHECK (contract_tithe_pct BETWEEN 0 AND 20);
