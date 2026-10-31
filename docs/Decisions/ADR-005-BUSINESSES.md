# ADR-005 — Primer negocio: puesto de café

- **Estado:** aceptada (valores provisionales)
- **Fecha:** 2026-10-31
- **Origen:** objetivo verificable del documento de diseño ("administrar una pequeña actividad económica").

## Decisiones

- Cada negocio tiene **su propia cuenta económica** (su caja), separada del dueño. Así no se confunden facturación, ganancia, liquidez y patrimonio.
- Las ventas ocurren en **ciclos económicos** que corre el backend (`BusinessCycleWorker`), **aunque el dueño esté desconectado**.
- Cada (negocio, ciclo) es una transacción del libro con clave idempotente: repetir o correr en paralelo no duplica ventas.
- Los clientes son la **población de Bellmont** (`system:poblacion`), una cuenta de sistema que puede quedar negativa: representa la economía externa al juego. El libro sigue sumando cero.
- Si el servidor estuvo apagado, se simulan como máximo 288 ciclos al volver.
- El alquiler va a la **Oficina Valenti** (política y finanzas, según el documento).

## Valores provisionales

| Valor | Provisional |
|---|---|
| Licencia | $15 (a los Valenti) |
| Bolsa de café | $3 (Almacén Moretti) |
| Venta | hasta 2 por ciclo a $5 |
| Alquiler | $1 por ciclo con ventas |
| Ciclo | 5 minutos en el servidor (5 s en las pruebas) |
| Negocios por personaje | 1 |

## Pendiente

Demanda variable (oferta y demanda, competencia entre puestos), empleados, otros tipos de negocio (restaurante, club, taller…), negocios de organizaciones, robo/riesgo, impuestos.
