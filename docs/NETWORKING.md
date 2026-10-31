
## Etapa B — estabilidad (2026-10-31, CI en una máquina de 4 núcleos compartida con los clientes)

| Prueba | Resultado |
|---|---|
| Red mala: 60 ms ± 20 ms por sentido, 3 % de pérdida (netem) | 2/2 conectados, ping ≈ 140–160 ms, se ven entre sí, error de predicción final 0,00 m, 0 desconexiones |
| Colisión con edificio | el bot que camina hacia el edificio queda afuera |
| Borde del barrio | bordes invisibles a ±30 m: el bot queda en x ≈ 29,6 (antes se caía del mapa) |
| 10 jugadores | física máx 3,7 ms/tick · CPU 7 % · RAM 185 MB · 44 kbps por jugador · 0 desconexiones |
| 20 jugadores | física máx 6,3 ms/tick · CPU 14 % · RAM 191 MB · 105 kbps por jugador · 0 desconexiones |

Las métricas del servidor (`METRICS`) ahora incluyen `cpu_pct`, `desconexiones` y `errores`.
