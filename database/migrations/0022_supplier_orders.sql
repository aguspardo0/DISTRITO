-- 0022 — Inventario, etapa 5: pedidos a proveedores.
-- Números aprobados por el equipo (2026-11-01): precio de mostrador, 10 % menos desde 20 unidades, mínimo 5,
-- 1 ciclo de preparación cada 10 unidades, el proveedor prepara hasta 50 unidades por ciclo (cola),
-- retiro gratis en el proveedor o envío al depósito de la cuadrilla por 10 % más (mínimo $1) y 1 ciclo más.
-- Lo pedido sale del stock real del proveedor y queda apartado en la ubicación pedido:<id> de su cuenta hasta la entrega.
CREATE TABLE distrito.supplier_orders (
    id              uuid        PRIMARY KEY,
    character_id    uuid        NOT NULL REFERENCES distrito.characters (id),
    supplier_code   text        NOT NULL,
    item_code       text        NOT NULL REFERENCES distrito.item_definitions (code),
    quantity        integer     NOT NULL CHECK (quantity > 0),
    goods_cents     bigint      NOT NULL CHECK (goods_cents >= 0),
    delivery_cents  bigint      NOT NULL CHECK (delivery_cents >= 0),
    delivery        text        NOT NULL CHECK (delivery IN ('retiro', 'envio')),
    warehouse_id    uuid        REFERENCES distrito.warehouses (id) ON DELETE SET NULL,
    status          text        NOT NULL CHECK (status IN ('preparando', 'entregado', 'cancelado')),
    ready_cycle     bigint      NOT NULL,
    deliver_cycle   bigint      NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    closed_at       timestamptz
);
CREATE INDEX supplier_orders_open_idx ON distrito.supplier_orders (supplier_code, status) WHERE status = 'preparando';
CREATE INDEX supplier_orders_character_idx ON distrito.supplier_orders (character_id, created_at DESC);
