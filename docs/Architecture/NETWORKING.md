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
| Tick del servidor (física) | 30 Hz |
| Input del cliente | 30 Hz |
| Replicación de posiciones | 20 Hz |
| Recalculo de interés | 4 Hz |

## Métricas a registrar por etapa

CPU, RAM, ancho de banda por jugador, latencia, frecuencia de sincronización, cantidad de entidades, tiempo de respuesta y consistencia de datos. Plan: 2 → 10 → 20 jugadores → mayor concurrencia, con clientes headless automatizados.
