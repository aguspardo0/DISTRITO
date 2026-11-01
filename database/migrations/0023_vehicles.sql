-- 0023 — Inventario, etapa 6: vehículos con carga.
-- Aprobado por el equipo (2026-11-01): camioneta de reparto $120 (nivel 3, 400 kg) y camión de carga $350 (nivel 6, 1500 kg),
-- en el Taller Caruso. Personales o de cuadrilla (los compra el jefe con la caja). La carga vive en item_stacks con la
-- cuenta dueña (account_id) y la ubicación vehiculo:<id>.
CREATE TABLE distrito.vehicles (
    id                  uuid        PRIMARY KEY,
    kind                text        NOT NULL CHECK (kind IN ('camioneta', 'camion')),
    owner_character_id  uuid        REFERENCES distrito.characters (id),
    owner_crew_id       uuid        REFERENCES distrito.crews (id) ON DELETE SET NULL,
    account_id          uuid        NOT NULL REFERENCES distrito.economic_accounts (id),
    x                   double precision NOT NULL,
    y                   double precision NOT NULL,
    z                   double precision NOT NULL,
    yaw                 double precision NOT NULL DEFAULT 0,
    created_at          timestamptz NOT NULL DEFAULT now()
);
