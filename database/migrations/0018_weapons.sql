-- 0018 — Armas y munición (propuesta aprobada por el equipo, 2026-10-31). Las vende el Taller Caruso.
INSERT INTO distrito.item_definitions (code, name, category, reference_price_cents, weight_kg, volume_l, legal_status) VALUES
    ('pistola', 'Pistola', 'armas', 4000, 1.1, 1, 'illegal'),
    ('escopeta', 'Escopeta', 'armas', 7000, 3.5, 6, 'illegal'),
    ('thompson', 'Thompson', 'armas', 15000, 4.9, 8, 'illegal'),
    ('bala-pistola', 'Bala de pistola', 'municion', 29, 0.02, 0, 'illegal'),
    ('cartucho', 'Cartucho', 'municion', 50, 0.04, 0, 'illegal'),
    ('bala-thompson', 'Bala de Thompson', 'municion', 16, 0.02, 0, 'illegal');

INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES
    ('00000000-0000-7000-8000-000000002101', '00000000-0000-7000-8000-000000000102', 'pistola', 'taller-moreno', 100000),
    ('00000000-0000-7000-8000-000000002102', '00000000-0000-7000-8000-000000000102', 'escopeta', 'taller-moreno', 100000),
    ('00000000-0000-7000-8000-000000002103', '00000000-0000-7000-8000-000000000102', 'thompson', 'taller-moreno', 100000),
    ('00000000-0000-7000-8000-000000002104', '00000000-0000-7000-8000-000000000102', 'bala-pistola', 'taller-moreno', 10000000),
    ('00000000-0000-7000-8000-000000002105', '00000000-0000-7000-8000-000000000102', 'cartucho', 'taller-moreno', 10000000),
    ('00000000-0000-7000-8000-000000002106', '00000000-0000-7000-8000-000000000102', 'bala-thompson', 'taller-moreno', 10000000);
