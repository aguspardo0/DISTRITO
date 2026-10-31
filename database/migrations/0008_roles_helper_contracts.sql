-- 0008 — Decisiones del equipo sobre la Hoja de ruta v0.2.1 (2026-10-31):
--   roles de cuadrilla (jefe, segundo, miembro), ayudante del puesto de café y contratos con plazo / en grupo.

-- Roles de cuadrilla
ALTER TABLE distrito.crew_members DROP CONSTRAINT crew_members_role_check;
ALTER TABLE distrito.crew_members ADD CONSTRAINT crew_members_role_check CHECK (role IN ('leader', 'second', 'member'));

-- Ayudante del puesto (empleado básico)
ALTER TABLE distrito.businesses ADD COLUMN has_helper boolean NOT NULL DEFAULT false;
ALTER TABLE distrito.business_cycles ADD COLUMN wages_cents bigint NOT NULL DEFAULT 0 CHECK (wages_cents >= 0);

-- Contratos con plazo y contratos que pueden fallar
ALTER TABLE distrito.contracts DROP CONSTRAINT contracts_status_check;
ALTER TABLE distrito.contracts ADD CONSTRAINT contracts_status_check CHECK (status IN ('active', 'completed', 'abandoned', 'failed'));
ALTER TABLE distrito.contracts ADD COLUMN expires_at timestamptz;

-- Fondos iniciales de las familias que pagan los contratos nuevos (igual que el Taller Caruso en 0003).
INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description) VALUES
    ('00000000-0000-7000-8000-000000001002', 'seed:fondos-moretti', 'seed', 'Fondos iniciales del Almacén Moretti'),
    ('00000000-0000-7000-8000-000000001003', 'seed:fondos-valenti', 'seed', 'Fondos iniciales de la Oficina Valenti');
INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents) VALUES
    ('00000000-0000-7000-8000-000000001002', '00000000-0000-7000-8000-000000000001', -10000000),
    ('00000000-0000-7000-8000-000000001002', '00000000-0000-7000-8000-000000000101',  10000000),
    ('00000000-0000-7000-8000-000000001003', '00000000-0000-7000-8000-000000000001', -10000000),
    ('00000000-0000-7000-8000-000000001003', '00000000-0000-7000-8000-000000000103',  10000000);
UPDATE distrito.economic_accounts SET balance_cents = balance_cents - 20000000 WHERE code = 'system:emision';
UPDATE distrito.economic_accounts SET balance_cents = balance_cents + 10000000 WHERE code IN ('npc:deposito-delgado', 'npc:oficina-valenti');
