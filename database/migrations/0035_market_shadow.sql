-- 0035 — Modo sombra de la economía de mercado (etapa 2, hoja de ruta): el mercado nuevo calcula al lado de la economía
-- real y anota lo que habría pasado. Solo lectura del juego: estas tablas no mueven plata ni mercadería.

-- Un renglón por ciclo económico observado (se guardan los últimos 7 días).
CREATE TABLE distrito.market_shadow_cycles (
    cycle        bigint      PRIMARY KEY,
    recorded_at  timestamptz NOT NULL DEFAULT now(),
    data         jsonb       NOT NULL
);

-- El resumen de cada día del mercado simulado (se guardan todos: son pocos y sirven para comparar semanas).
CREATE TABLE distrito.market_shadow_days (
    last_cycle   bigint      PRIMARY KEY,
    recorded_at  timestamptz NOT NULL DEFAULT now(),
    data         jsonb       NOT NULL
);
