# Progresión del personaje (Etapa D, aprobada por el equipo el 2026-10-31)

| Sistema | Regla |
|---|---|
| Nivel 1–30 | +100 de experiencia por contrato cumplido (cada participante), +10 por ciclo en que el puesto vende (al dueño). Del nivel n al n+1: 100 × n (nivel 30 = 43.500). |
| Oficios 1–10 | Por práctica. Del nivel n al n+1: 5 × n prácticas (nivel 5 = 50, nivel 10 = 225). |
| Logística | +1 al entregar mercadería de un contrato. Ventaja: +10 s de plazo por nivel por encima de 1. |
| Negociación | +1 por contrato cobrado. Ventaja: +1 % del pago por nivel por encima de 1; lo paga quien paga el contrato (el dinero se conserva). |
| Administración | +1 por ciclo de venta del puesto. Ventaja: +1 venta por ciclo desde el nivel 5, +2 desde el 10. |

El nivel habilita contratos (ver abajo).
Código: `shared/Distrito.Shared/Progression/Progression.cs`, tabla `character_progress` (migración 0009).

# Ánimo de las familias (aprobado el 2026-10-31)

Común a todos los jugadores; se calcula con las 24 h anteriores al comienzo de la hora actual.
- Cooperación: 10+ contratos cumplidos que paga la familia → sus contratos pagan +10 %.
- Rivalidad: 10+ puntos de reputación perdidos con la familia → contratos −10 %, precios de venta y licencias +10 %. Gana sobre cooperación.
- Neutral en otro caso. Expansión y recuperación quedan para cuando haya territorio.

# Caja de la cuadrilla (aprobado el 2026-10-31)

- Cuenta propia en el libro (kind `crew`), creada al primer uso. Depositar: cualquier integrante ($1, $5, $10 o todo).
- Retirar: el jefe; el segundo solo si el jefe lo permite (arranca en no).
- Invitar: jefe siempre; segundo si el jefe lo permite (arranca en sí); miembros si el jefe lo permite (arranca en no).
- Aporte automático: 0–20 % de cada cobro de contrato de cada integrante va a la caja (arranca en 0 %).
- Todo movimiento queda en el historial de la cuadrilla.

# Contratos por nivel (aprobado el 2026-10-31)

| Contrato | Pide | Qué hacer | Paga | Reputación |
|---|---|---|---|---|
| Reparto para la fiesta (Moretti) | nivel 3, Logística 2 | 3 cajones de bebidas al Taller Caruso en 4 min | $20 | +4 Moretti, +2 Caruso; vencido −3 Moretti |
| Revisar las cuentas (Valenti) | nivel 5, Negociación 3 | ir al Almacén Moretti, sin mercadería | $18 | +5 Valenti, −1 Moretti |
| Cobranza grande (Valenti) | nivel 8, 15 Caruso, 2 compañeros presentes | cobrar en el Taller Caruso | $36 entre los tres | +8 Valenti, −3 Caruso |
