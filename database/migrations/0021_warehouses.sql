-- 0021 — Inventario, etapa 3: almacenes con dueño, lugar, capacidad y permisos.
-- Lo guardado vive en el libro central (item_stacks) con la cuenta de la caja de la cuadrilla y la ubicación almacen:<id>.
-- Números aprobados por el equipo (2026-11-01): depósito de cuadrilla en La Oficina, $20 para abrirlo,
-- $1 de alquiler por ciclo mientras tenga algo guardado (como el puesto de café), 300 kg.
CREATE TABLE distrito.warehouses (
    id                   uuid        PRIMARY KEY,
    crew_id              uuid        NOT NULL UNIQUE REFERENCES distrito.crews (id) ON DELETE CASCADE,
    place                text        NOT NULL,
    capacity_kg          numeric,
    capacity_l           numeric,
    last_rent_cycle      bigint      NOT NULL,
    debt_cents           bigint      NOT NULL DEFAULT 0 CHECK (debt_cents >= 0),
    member_can_view      boolean     NOT NULL DEFAULT true,
    member_can_deposit   boolean     NOT NULL DEFAULT true,
    member_can_withdraw  boolean     NOT NULL DEFAULT true,
    second_can_withdraw  boolean     NOT NULL DEFAULT true,
    created_at           timestamptz NOT NULL DEFAULT now()
);
