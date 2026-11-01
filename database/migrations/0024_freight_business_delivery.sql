-- 0024 — Inventario, etapa 8: pedidos con envío directo al negocio (aprobado 2026-11-01, mismo costo de envío: 10 %, mín. $1).
ALTER TABLE distrito.supplier_orders DROP CONSTRAINT supplier_orders_delivery_check;
ALTER TABLE distrito.supplier_orders ADD CONSTRAINT supplier_orders_delivery_check CHECK (delivery IN ('retiro', 'envio', 'negocio'));
ALTER TABLE distrito.supplier_orders ADD COLUMN business_id uuid REFERENCES distrito.businesses (id) ON DELETE SET NULL;
