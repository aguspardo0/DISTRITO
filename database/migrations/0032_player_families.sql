-- 0032 — Familias de jugadores y subfacciones (Etapa E). Decisión del equipo (2026-11-01): máximo 5 en el servidor y,
-- por ahora, sin poder fundarlas (config/familias.json). Una cuadrilla funda la familia y las demás se suman como
-- subfacciones: cada cuadrilla conserva su caja, sede y misiones.
CREATE TABLE distrito.player_families (
    id                uuid        PRIMARY KEY,
    name              text        NOT NULL UNIQUE,
    founder_crew_id   uuid        NOT NULL UNIQUE REFERENCES distrito.crews (id),
    don_character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    created_at        timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE distrito.crews ADD COLUMN family_id uuid REFERENCES distrito.player_families (id) ON DELETE SET NULL;
