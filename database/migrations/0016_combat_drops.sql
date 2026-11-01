-- 0016 — PvP: la mercadería de un herido queda en el suelo (cuenta de sistema) hasta que alguien la levanta.
INSERT INTO distrito.economic_accounts (id, kind, code, display_name, allow_negative) VALUES
    ('00000000-0000-7000-8000-000000000003', 'system', 'system:suelo', 'Mercadería tirada en la calle', false);
