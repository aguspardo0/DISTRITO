-- 0026 — Casino «El Trébol» (aprobado por el equipo 2026-11-01). Manos de blackjack en curso (una por personaje).
CREATE TABLE distrito.casino_hands (
    id            uuid        PRIMARY KEY,
    character_id  uuid        NOT NULL REFERENCES distrito.characters (id),
    stake_cents   bigint      NOT NULL CHECK (stake_cents > 0),
    deck          int[]       NOT NULL,
    player        int[]       NOT NULL,
    dealer        int[]       NOT NULL,
    status        text        NOT NULL CHECK (status IN ('jugando', 'ganada', 'perdida', 'empate', 'blackjack')),
    created_at    timestamptz NOT NULL DEFAULT now(),
    closed_at     timestamptz
);
CREATE UNIQUE INDEX casino_hands_one_open ON distrito.casino_hands (character_id) WHERE status = 'jugando';
