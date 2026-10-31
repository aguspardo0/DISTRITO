-- 0010 — Segundo negocio: club de jazz (propuesta aprobada por el equipo, 2026-10-31).
-- Un personaje puede tener un negocio de cada tipo. El proveedor es el Almacén Moretti (cajón de bebidas a $4).
ALTER TABLE distrito.businesses DROP CONSTRAINT businesses_kind_check;
ALTER TABLE distrito.businesses ADD CONSTRAINT businesses_kind_check CHECK (kind IN ('puesto-cafe', 'club-jazz'));
ALTER TABLE distrito.businesses DROP CONSTRAINT businesses_owner_character_id_key;
ALTER TABLE distrito.businesses ADD CONSTRAINT businesses_owner_kind_key UNIQUE (owner_character_id, kind);

INSERT INTO distrito.item_definitions (code, name, category, reference_price_cents, weight_kg, volume_l, legal_status) VALUES
    ('cajon-bebidas', 'Cajón de bebidas', 'alimentos', 400, 15, 20, 'legal');

INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES
    ('00000000-0000-7000-8000-000000002003', '00000000-0000-7000-8000-000000000101', 'cajon-bebidas', 'deposito-delgado', 100000);
