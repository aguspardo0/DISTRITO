-- 0037 — Misiones del café (aprobado por el equipo, 2026-11-08): la historia «El café de Don Aldo» la da la Esquina del
-- café a los dueños de un puesto. Se guarda como cualquier historia, con la clave 'cafe'.
ALTER TABLE distrito.character_encargos DROP CONSTRAINT IF EXISTS character_encargos_story_check;
ALTER TABLE distrito.character_encargos ADD CONSTRAINT character_encargos_story_check CHECK (story IN ('moretti', 'valenti', 'caruso', 'cafe'));
