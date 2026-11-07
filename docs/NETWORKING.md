## Escala real: 100 jugadores en el servidor de internet (2026-11-07)

Workflow manual `escala.yml` (Actions → «Escala real» → Run workflow). Levanta una partida aparte en el servidor
(modo invitado, UDP 7790, la versión desplegada; la partida oficial y la base no se tocan) y la llenan bots desde 4
máquinas de GitHub a la vez: entra 1 bot por segundo hasta 50, 30 s de meseta, sigue hasta 100 y 60 s de meseta.
Cada bot es un cliente real que camina en círculos. **Es el peor caso:** todos aparecen en el mismo lugar y se ven
todos con todos (≈ N² pares visibles). El servidor sube el tope con `--max-players` (en producción sigue en 64).

**Servidor:** Vultr São Paulo, **1 núcleo** (Xeon Cascadelake) y 2 GB de RAM. Los bots salen de EE.UU. (ping ≈ 165 ms
promedio, 500 ms máximo); desde Argentina el ping sería mucho menor.

| Jugadores | Física máx (ms por paso; presupuesto 16,7) | CPU (100 = el núcleo entero) | Cuadros por segundo | Envía en total | Por jugador |
|---|---|---|---|---|---|
| 8 | 2,2 | 9 % | 60 | 0,3 Mbps | 33 kbps |
| 28 | 2,6 | 19 % | 60 | 4,5 Mbps | 161 kbps |
| 50 (meseta) | 5,1 | 40 % | 60 | 16,9 Mbps | 337 kbps |
| 68 | 9,9 | 56 % | 60 | 29,3 Mbps | 431 kbps |
| 86 | 9,4 | 78 % | 60 | 45,4 Mbps | 528 kbps |
| 100 (meseta) | 12,8 | 85–93 % | **43–59** | 62–66 Mbps | 620–660 kbps |

- **Con 100 todos juntos, el núcleo se satura:** el servidor baja a 43–48 cuadros por segundo, 2 bots se
  desconectan y uno queda con la predicción 1,23 m corrida. 99 de 100 terminaron bien. RAM: 260 MB, sin problema.
- **Hasta unos 80 todos juntos, anda bien** en esta máquina (CPU ≤ 64 %, 60 cuadros, sin desconexiones).
- **El límite real es la red:** lo que manda el servidor crece con el cuadrado de los jugadores que se ven entre sí
  (cada uno recibe a todos los demás). Por jugador pasa de 33 kbps (8) a 650 kbps (100). Para cada cliente 0,65 Mbps
  está bien, pero el servidor manda 65 Mbps en total: en uso continuo eso agotaría rápido la transferencia mensual
  de un plan chico.
- En una ciudad real la gente se reparte y no se ve toda con toda (radio de 60 m), así que el uso normal queda muy por
  debajo de este peor caso.

**Qué conviene hacer, en orden:** (1) mandar menos por la red: posiciones con menos frecuencia para los lejanos y
paquetes propios más chicos en vez del sincronizador automático (es la Etapa 3 de la hoja de ruta) y repetir esta
misma prueba para comparar; (2) cuando haya jugadores reales, pasar a un servidor de 2 núcleos.


## Etapa B — estabilidad (2026-10-31, CI en una máquina de 4 núcleos compartida con los clientes)

| Prueba | Resultado |
|---|---|
| Red mala: 60 ms ± 20 ms por sentido, 3 % de pérdida (netem) | 2/2 conectados, ping ≈ 140–160 ms, se ven entre sí, error de predicción final 0,00 m, 0 desconexiones |
| Colisión con edificio | el bot que camina hacia el edificio queda afuera |
| Borde del barrio | bordes invisibles a ±30 m: el bot queda en x ≈ 29,6 (antes se caía del mapa) |
| 10 jugadores | física máx 3,7 ms/tick · CPU 7 % · RAM 185 MB · 44 kbps por jugador · 0 desconexiones |
| 20 jugadores | física máx 6,3 ms/tick · CPU 14 % · RAM 191 MB · 105 kbps por jugador · 0 desconexiones |

Las métricas del servidor (`METRICS`) ahora incluyen `cpu_pct`, `desconexiones` y `errores`.

## Etapa C — presencia en el mapa

`MapPresence` (núcleo) + `MapNetwork` (Godot): el servidor manda a cada cliente, 1 vez por segundo y sin garantía de entrega,
arreglos paralelos `peers / nombres / x,z` con posiciones redondeadas a 0,5 m. Es independiente del interés de red:
quien está lejos aparece en el mapa pero no se replica su entidad 3D. Costo: N² marcadores por segundo
(20 jugadores ≈ 400 marcadores/s, unos pocos KB/s en total).
