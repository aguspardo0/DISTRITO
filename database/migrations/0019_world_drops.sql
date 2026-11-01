-- 0019 — Botín en el mundo: lo que queda tirado al morir tiene lugar y vencimiento y sobrevive a reinicios del servidor.
-- Los objetos viven en el inventario central (cuenta system:suelo, ubicación bulto:<id>); esta tabla es su lugar en la calle.
CREATE TABLE distrito.world_drops (
    id           uuid        PRIMARY KEY,
    victim_id    uuid        REFERENCES distrito.characters (id),
    x            double precision NOT NULL,
    y            double precision NOT NULL,
    z            double precision NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    picked_by    uuid        REFERENCES distrito.characters (id),
    picked_at    timestamptz
);
CREATE INDEX world_drops_active_idx ON distrito.world_drops (expires_at) WHERE picked_at IS NULL;
