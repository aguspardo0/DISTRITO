-- 0012 — Aspecto del personaje (cuatro láminas de diseño del equipo): se elige al crearlo.
ALTER TABLE distrito.characters ADD COLUMN look text NOT NULL DEFAULT 'trabajador'
    CHECK (look IN ('ejecutivo', 'operativo', 'trabajador', 'asociado'));
