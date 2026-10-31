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

# Territorio (Etapa E, aprobado el 2026-10-31)

Zonas: Calle del Almacén (Moretti), Los Talleres (Caruso), La Oficina (Valenti), La Esquina (café y club).
Influencia por cuadrilla y temporada: +3 por contrato entregado en la zona (una vez por cuadrilla), +1 por ciclo con ventas
de un negocio de un integrante; tope de 30 por día (hora de Argentina) por zona. Controla quien más tiene con 20 o más
(empate: quien llegó primero): sus integrantes cobran +5 % en contratos entregados ahí. Temporadas de 4 semanas desde el
lunes 26/10/2026; al cerrar, el ganador de cada zona queda en `zone_season_results` y en el historial de la cuadrilla.

# Coaliciones (Etapa E, aprobado el 2026-10-31)

Hasta 3 cuadrillas. El jefe invita a otra cuadrilla por nombre (con el nombre de la coalición si todavía no tiene); el jefe
invitado acepta o rechaza (24 h). En territorio, la influencia de las cuadrillas aliadas en una zona se suma y cuenta el 75 %;
si la coalición controla, todos sus integrantes cobran el +5 %. En trabajos de cuadrilla sirven compañeros de cuadrillas
aliadas. Salir: cuando el jefe quiere, con 24 h de espera para entrar a otra; con una sola cuadrilla se disuelve.
