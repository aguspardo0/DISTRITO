-- 0030 — Historias de las familias (propuesta aprobada por el equipo, 2026-11-01): cada capítulo es un encargo que da el
-- capo. Se guarda de qué historia y qué capítulo es; la historia está terminada cuando el capítulo 4 (índice 3) se cumplió.
ALTER TABLE distrito.character_encargos
    ADD COLUMN story      text CHECK (story IN ('moretti', 'valenti', 'caruso')),
    ADD COLUMN story_step int  CHECK (story_step BETWEEN 0 AND 3);
CREATE INDEX character_encargos_story ON distrito.character_encargos (character_id, story) WHERE story IS NOT NULL;
