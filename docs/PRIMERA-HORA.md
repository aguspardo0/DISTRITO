# La primera hora: de empleado a dueño del café (2026-11-08)

Decisiones del equipo: la base es que el jugador nuevo **primero trabaja de empleado y después tiene su café**; el
tutorial lo guía pero **no lo obliga** (el que ya sabe puede comprar su puesto directo) y los turnos quedan como
ingreso extra para siempre. Turno de **10 minutos, $3 y propinas de hasta $1**.

## Cómo se hacía el café en 1952

En las cafeterías y los diners de EE.UU. el café salía de la **urna**: una cafetera grande de filtro, siempre caliente,
de la que se servía en taza gruesa, negro o con leche o crema y terrones de azúcar. La máquina de espresso existía
(Gaggia, 1948), pero era cosa de los barrios italianos: queda para un café del Barrio Moretti más adelante.

## El turno (`CoffeeShiftRules`, `CoffeeShiftNetwork`, `CoffeeShiftPanel`)

- En la **Esquina del café** del Mercado, Don Aldo ofrece «Trabajar un turno de 10 minutos ($3 + propinas de hasta $1)».
- Llega un cliente cada 30 segundos (esperan hasta 3 a la vez y se van al minuto). Cada uno pide su café: **negro, con
  leche o con crema, y de 0 a 2 terrones**.
- Para servir: **mantener Espacio** bajo la canilla de la urna y soltar en la línea (85 % de la taza; si pasa de 98 %
  se rebalsa), **1** leche, **2** crema, **3** un terrón, **0** tirar y empezar de nuevo, **Tab** cambiar de pedido y
  **Enter** entregar.
- **La urna** da 10 tazas por carga; con **R** se recarga (12 segundos sin poder servir). Lo que se sirve con la urna
  vacía sale aguado (vale la mitad).
- Cada café se puntúa (0 a 100 %): la taza hasta la línea, lo que pidió y qué tan rápido.
- **Cobro:** al terminar, $3 si serviste al menos 6, más una propina de hasta $1 según el promedio (toda la propina con
  12 o más bien servidos). La plata sale de la clientela (`system:poblacion`), como las ventas de los negocios.
- Si te vas de la barra (más de 7 m) o dejás el turno con Esc, no se cobra.
- El servidor manda los pedidos, toma el tiempo, lleva la urna y puntúa; el backend revisa los topes y que el mismo
  turno no se cobre dos veces.

## El tutorial ahora

1. Bienvenida (abrir el mapa).
2. **Tu primer trabajo:** un turno en la Esquina del café.
3. **Tu propio puesto:** sacar la licencia en la Oficina Valenti.
4. **Mercadería para el puesto:** bolsas de café en el Almacén Moretti.
5. y siguientes: las familias, el primer encargo, la libreta y lo que viene (como antes).

Los que estaban a mitad del tutorial siguen en el mismo paso.

## Pendiente

- **Tope:** 6 turnos cobrados por día de Argentina (aprobado 2026-11-08). Después, la plata sale del puesto propio, los encargos, los contratos y las misiones del café.
- Los locales físicos del Centro: el equipo todavía no decidió si los negocios de los jugadores ocupan un local en el
  mundo 3D (preocupa el espacio si hay muchos). Lo que sí está decidido: se **alquilan** principalmente, comprar se
  puede pero **cuesta mucho**, y si el dueño no paga o no entra en **14 días** el local se libera. Las **sedes** de las
  cuadrillas van a tener la misma regla (alquiler primero; comprar, muy caro).

## La competencia y las misiones del café (2026-11-08)

- **Café La Perla:** el café de la competencia, en un local del Centro a pasos del Mercado (cartel de neón y toldo
  verde). Se lleva parte de la clientela de los puestos según precio, calidad y fama (ver `docs/NEGOCIOS-QUE-CRECEN.md`).
- **«El café de Don Aldo»:** cuatro misiones que da Don Aldo en la Esquina del café **a los que ya tienen su puesto**.
  Son una historia más (se juegan con la libreta, los marcadores y el seguimiento de siempre) y no cuentan para el
  límite de 2 encargos:
  1. **Granos del puerto** (paquete con Gaetano, en Los Muelles) — $5, lo pagan los Moretti.
  2. **La reunión de los Valenti** (2 bolsas de café) — $12, los Valenti.
  3. **Noches de jazz** (un sobre al Club de jazz) — $8, los Valenti.
  4. **¿Quién se robó la urna?** (escuchar 2 minutos cerca del kiosco) — $10, los Caruso.

  Cada capítulo da además **+60 de fama al puesto**, experiencia y +5 de reputación con la familia que paga. La plata
  sale de la cuenta de esa familia, como todos los encargos.
