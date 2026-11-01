-- 0028 — Árbol de habilidades (aprobado por el equipo 2026-11-01): rangos aprendidos por personaje.
CREATE TABLE distrito.character_skills (
    character_id  uuid NOT NULL REFERENCES distrito.characters (id) ON DELETE CASCADE,
    node_code     text NOT NULL,
    rank          int  NOT NULL CHECK (rank > 0),
    PRIMARY KEY (character_id, node_code)
);
