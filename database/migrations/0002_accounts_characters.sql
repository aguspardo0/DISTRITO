-- 0002 — Cuentas, sesiones y personajes (hito 1).
-- Identificadores uuid generados por el backend (UUIDv7, ordenables por tiempo).

CREATE TABLE distrito.accounts (
    id                   uuid        PRIMARY KEY,
    username             text        NOT NULL,
    username_normalized  text        NOT NULL UNIQUE,
    password_hash        text        NOT NULL,
    created_at           timestamptz NOT NULL DEFAULT now()
);

-- Sesiones de login. Se guarda solo el hash SHA-256 del token: si se filtra la tabla, los tokens no sirven.
CREATE TABLE distrito.sessions (
    token_hash  bytea       PRIMARY KEY,
    account_id  uuid        NOT NULL REFERENCES distrito.accounts (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL DEFAULT now(),
    expires_at  timestamptz NOT NULL
);
CREATE INDEX sessions_account_idx ON distrito.sessions (account_id);

CREATE TABLE distrito.characters (
    id               uuid             PRIMARY KEY,
    account_id       uuid             NOT NULL REFERENCES distrito.accounts (id),
    name             text             NOT NULL,
    name_normalized  text             NOT NULL UNIQUE,
    zone_id          text             NOT NULL,
    pos_x            double precision NOT NULL,
    pos_y            double precision NOT NULL,
    pos_z            double precision NOT NULL,
    yaw              double precision NOT NULL DEFAULT 0,
    created_at       timestamptz      NOT NULL DEFAULT now(),
    updated_at       timestamptz      NOT NULL DEFAULT now()
);
CREATE INDEX characters_account_idx ON distrito.characters (account_id);
