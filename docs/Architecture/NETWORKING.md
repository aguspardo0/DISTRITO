# Networking

## Decisión inicial: ENet mediante MultiplayerAPI de Godot

Se **evalúa** (no está cerrado) `ENetMultiplayerPeer` + `MultiplayerAPI` de Godot 4.7.

**A favor:**
- Viene con el motor: sin dependencias externas.
- ENet sobre UDP con canales confiables y no confiables.
- RPCs tipados y `MultiplayerSynchronizer` con **filtros de visibilidad por peer**, que sirven como base para el interés de red.

**Riesgos a medir:**
- Rendimiento de `MultiplayerSynchronizer` con 30–60 jugadores y muchas entidades.
- Control fino sobre el formato de los paquetes (puede hacer falta serializar a mano).
- Acoplamiento del servidor a Godot headless.

**Criterio de salida:** si las pruebas de carga (etapa 20+ jugadores) muestran que no alcanza, se evalúa un servidor .NET puro con su propia capa de transporte. Como la lógica ya vive en `Distrito.Server.Core`, el cambio afecta solo al host.

## Reglas

1. **El servidor tiene autoridad** sobre toda entidad. El cliente solo tiene autoridad sobre su *input*.
2. **Nunca se envía el estado completo del mundo.** Cada peer recibe solo lo que está en su área de interés.
3. Movimiento: el cliente manda input (dirección, salto) a tasa fija; el servidor simula y replica posiciones.
4. Mensajes económicos (comprar, pagar, aceptar contrato) van por canal **confiable** y son solicitudes, no hechos.

## Interés de red (diseño)

- Primera versión: radio fijo alrededor del jugador (p. ej. 60 m), recalculado unas pocas veces por segundo, no cada frame.
- Más adelante: grilla espacial por celdas para no comparar todos contra todos.

## Tasas iniciales (a medir, no definitivas)

| Dato | Frecuencia |
|---|---|
| Tick del servidor (física) | 60 Hz (valor por defecto de Godot en el prototipo) |
| Input del cliente | 60 Hz (prototipo: cada tick de física; reducir tras medir) |
| Replicación de posiciones | 20 Hz |
| Recalculo de interés | 4 Hz |

## Métricas a registrar por etapa

CPU, RAM, ancho de banda por jugador, latencia, frecuencia de sincronización, cantidad de entidades, tiempo de respuesta y consistencia de datos. Plan: 2 → 10 → 20 jugadores → mayor concurrencia, con clientes headless automatizados.

## Implementación actual (prototipo, etapa 1)

- `GameNetwork` crea el peer ENet en uno de tres modos: servidor dedicado, anfitrión (servidor + jugador en el mismo proceso, id 1) o cliente.
- El servidor crea un `Player` por conexión, con nombre = id de red. `MultiplayerSpawner` lo replica a todos (incluidos los que entran después).
- `MultiplayerSynchronizer` (autoridad del servidor) replica `position` y `rotation` cada 50 ms.
- El dueño envía su intención por RPC no confiable (`ReceiveInput`). El servidor verifica que el emisor sea el dueño, la sanea con `MovementRules` y la aplica.
- Si no llegan entradas por 250 ms, el personaje se detiene.
- Los demás clientes suavizan el modelo visual hacia la última posición recibida.
- CI levanta un servidor dedicado y 2 clientes bot y verifica conexión, visibilidad mutua y movimiento.

## Interés de red (implementado, etapa 3)

- `InterestRules` (Server.Core): radio en el plano XZ con histéresis de 5 m (entrar a R, salir a R+5).
- `InterestManager` (servidor): agrega un filtro de visibilidad a cada `MultiplayerSynchronizer` y lo recalcula a 4 Hz.
  Si un personaje deja de ser visible para un peer, Godot deja de enviarlo y lo elimina de la escena de ese cliente.
- Radio por defecto: 60 m (`NetworkDefaults.InterestRadius`), configurable con `--interest-radius`.
- Probado en CI: con radio 12 m, dos bots que se alejan 26 m terminan viéndose solo a sí mismos.

## Mediciones (CI, 2026-10-31)

Máquina: 4 núcleos, 15 GB; servidor y todos los clientes en la misma máquina (los números del servidor son pesimistas en CPU y optimistas en red).
Todos los jugadores dentro del radio de interés (peor caso: todos ven a todos).

| Jugadores | Física máx. por tick | RAM servidor | Envío servidor | Por jugador |
|---|---|---|---|---|
| 10 | 1,98 ms | 184 MB | 377 kbps | 38 kbps |
| 20 | 5,89 ms | 190 MB | 1.546 kbps | 77 kbps |

Lectura:
- **CPU:** holgada. Un tick a 60 Hz tiene 16,6 ms; con 20 jugadores se usa ~6 ms.
- **RAM:** prácticamente constante (~190 MB).
- **Ancho de banda:** crece con el cuadrado de los jugadores cercanos (cada uno recibe a todos los demás). Extrapolando sin cambios, 60 jugadores juntos serían ~230 kbps por jugador y ~14 Mbps de salida. Es el límite a atacar primero: radio de interés más chico en zonas densas, menor frecuencia para entidades lejanas, y serialización propia si hace falta.
