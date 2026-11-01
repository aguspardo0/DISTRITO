-- 0027 — Panel de administración: rehacer el tablero de misiones de una cuadrilla (cambia la semilla del generador).
ALTER TABLE distrito.crews ADD COLUMN mission_salt int NOT NULL DEFAULT 0;
