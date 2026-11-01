# Asaltos a NPC (eventos PvE)

Aprobado por el equipo (2026-11-01). Todo se edita en `config/eventos-pve.json` (el servidor lo toma solo; las pruebas
validan el archivo). Los asaltos a NPC no dependen de las reglas de zona: son contra NPC, no contra jugadores.

## Cómo se asalta
- **Amenazar**: con un arma en la mano (2–4), apuntarle al NPC (a menos de 12 m) durante unos segundos. Mientras le apuntás,
  frena. Si dejás de apuntar, se le va pasando el susto. **Balearlo** hace que se rinda enseguida con el que tiró.
- Lo robado sale de datos reales: mercadería del stock de Moretti, plata de la caja de la familia. El contrabando entra de
  afuera (es mercadería nueva en la economía).
- **Consecuencia**: asaltar a una familia baja 10 la reputación con ella; como el ánimo de las familias se calcula con la
  reputación perdida en 24 h, un asalto ya la pone en **rivalidad** (contratos −10 %, precios +10 %). El contrabando no es de nadie.

## Variantes
| Evento | Cuándo | Defensa | Botín |
|---|---|---|---|
| Camión de reparto de Moretti | cada 20 min, recorre el barrio hasta Los Muelles y vuelve | el chofer se rinde si le apuntás 3 s; también frena si le cortás el paso | 10–15 cajas de repuestos del stock real, caen a la calle detrás del camión; si nadie lo asalta, vuelven al stock |
| Cobrador de Valenti | cada 15 min, camina una ronda | se rinde si le apuntás 3 s | $30–60 de la caja de Valenti, directo al que asalta |
| Contrabando en Los Muelles | 13, 19 y 23 h (Argentina), dura 10 min | 2 guardias con 60 de vida que disparan (25 de daño, 15 m, mejor puntería de cerca) | al caer los guardias queda un bulto con munición o armas (uno de 4 botines) |
| Caja de un local | Taller Caruso y Almacén Moretti, 30 min de espera por local | apuntarle al encargado 8 s | $20–40 de la caja del local |

Los avisos (sale el camión, llegó el contrabando, asaltaron…) les llegan a todos los conectados.
