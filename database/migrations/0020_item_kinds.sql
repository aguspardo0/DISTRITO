-- 0020 — Inventario, etapa 1: cada tipo de objeto declara su clase (qué comportamiento tiene), descripción,
-- si se apila y propiedades propias opcionales (sin obligar a todos a tener lo mismo).
-- La categoría vieja (alimentos, vehiculos-repuestos…) queda como subcategoría; no cambia ningún dato existente.
ALTER TABLE distrito.item_definitions
    ADD COLUMN kind        text    NOT NULL DEFAULT 'mercancia'
        CHECK (kind IN ('arma', 'municion', 'herramienta', 'mercancia', 'materia-prima', 'producto', 'documento', 'equipamiento', 'mision')),
    ADD COLUMN description text    NOT NULL DEFAULT '',
    ADD COLUMN stackable   boolean NOT NULL DEFAULT true,
    ADD COLUMN properties  jsonb   NOT NULL DEFAULT '{}'::jsonb;

UPDATE distrito.item_definitions SET kind = 'mercancia',
    description = 'Repuestos para autos y camiones. Los talleres los necesitan para trabajar.' WHERE code = 'caja-repuestos';
UPDATE distrito.item_definitions SET kind = 'materia-prima',
    description = 'Café en grano. Con esto el puesto de café prepara lo que vende.' WHERE code = 'bolsa-cafe';
UPDATE distrito.item_definitions SET kind = 'mercancia',
    description = 'Un cajón de bebidas para la barra del club.' WHERE code = 'cajon-bebidas';
UPDATE distrito.item_definitions SET kind = 'arma', stackable = true,
    description = 'Pistola semiautomática. Liviana y fácil de esconder.', properties = '{"municion": "bala-pistola"}' WHERE code = 'pistola';
UPDATE distrito.item_definitions SET kind = 'arma', stackable = true,
    description = 'Escopeta de dos caños. Devastadora de cerca.', properties = '{"municion": "cartucho"}' WHERE code = 'escopeta';
UPDATE distrito.item_definitions SET kind = 'arma', stackable = true,
    description = 'Subfusil Thompson. Ráfagas de cincuenta balas.', properties = '{"municion": "bala-thompson"}' WHERE code = 'thompson';
UPDATE distrito.item_definitions SET kind = 'municion', description = 'Bala para pistola. Se gasta al disparar.' WHERE code = 'bala-pistola';
UPDATE distrito.item_definitions SET kind = 'municion', description = 'Cartucho de escopeta. Se gasta al disparar.' WHERE code = 'cartucho';
UPDATE distrito.item_definitions SET kind = 'municion', description = 'Bala para la Thompson. Se gasta al disparar.' WHERE code = 'bala-thompson';

-- Quién hizo cada movimiento y por qué (el motivo ya está en la transacción del libro).
ALTER TABLE distrito.item_movements ADD COLUMN actor_character_id uuid REFERENCES distrito.characters (id);

-- Bultos soltados a propósito (no solo al morir): quién lo soltó.
ALTER TABLE distrito.world_drops ADD COLUMN reason text NOT NULL DEFAULT 'muerte' CHECK (reason IN ('muerte', 'soltado'));
