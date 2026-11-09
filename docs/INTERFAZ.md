# La pantalla de juego (2026-11-08)

Pedido del equipo: **lo más limpia posible, pero que diga lo importante**. `GameHud` reemplaza el texto fijo de
arriba a la izquierda (la lista de controles y el último mensaje) y los textos sueltos de vida y balas.

| Dónde | Qué muestra |
|---|---|
| Arriba al medio | Brújula fina (N, NE, E…; el norte en dorado) con el **objetivo** marcado con un rombo dorado (el paso del tutorial o, si no hay, el primer encargo); si queda atrás, una flecha en el borde. Debajo, su nombre y la distancia. |
| Abajo a la izquierda | **Vida** (número y barra fina, roja si baja de 40 o estás herido), **plata** y la **zona** donde estás (en rojo si es zona libre). |
| Abajo a la derecha | La **hora de Bellmont** (día o noche) y, solo con arma en la mano, las **balas**. |
| Arriba a la derecha | **Avisos**: aparecen, se leen y se van solos a los 5 segundos (8 los importantes, con borde rojo); como mucho tres; el mismo aviso no se repite. |

- **F1** muestra y oculta los controles (al entrar aparece el aviso «F1: ver los controles»).
- **F3** muestra los datos de rendimiento a la derecha, a media altura (antes estaban siempre a la vista y tapaban los avisos).
- El nombre propio ya no aparece arriba de la cabeza; el de los demás jugadores, sí.
- Al entrar a Los Muelles sale un aviso importante: zona libre.
- El tutorial queda arriba a la izquierda mientras dura; la libreta de encargos, a la derecha debajo de los avisos.

## La plata que entra y sale

Cuando cambia la plata, al lado del número sale **«+$3,00»** en verde (cobraste) o **«−$12,00»** en rojo (pagaste),
que sube y se borra en dos segundos y medio. Como mucho se ven cuatro a la vez.

## El mapa (M)

- **Rueda del mouse:** acercar o alejar (de 1× a 6×), hacia donde apunta el mouse.
- **Arrastrar con el clic izquierdo:** mover el mapa (no se va del marco).
- **Espacio:** acercar y centrar en vos.
- Vos sos una **flecha roja** que apunta hacia donde mira la cámara.
- Las referencias (símbolos y la lista de lugares) van en una columna a la derecha del mapa, así no tapan nada.
- Abrir el mapa suelta el mouse (como las otras ventanas); cerrarlo lo vuelve a tomar.

Arreglo de paso: los avisos de la pantalla de juego contaban como «ventana abierta» y un clic no volvía a tomar el mouse.
