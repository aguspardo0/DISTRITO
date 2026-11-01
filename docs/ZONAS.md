# Cómo editar las reglas de zona (PvP y conflicto)

Las reglas están en `config/zonas.json`. Se editan en GitHub (lápiz → Commit changes); las pruebas validan el archivo
y el servidor lo toma solo en unos minutos. Si hay un error, la corrida dice cuál y el servidor sigue con las reglas anteriores.

- `base`: permisos de todo el barrio.
- `zonas`: cambios por zona (`calle-almacen`, `los-talleres`, `la-oficina`, `la-esquina`).
- `excepciones`: cambios mientras pasa algo: `disputa` (dos o más cuadrillas/coaliciones con 10+ de influencia), `evento`, `guerra`.
- `zonaSegura`: alrededor de cada local (`radio` en metros); gana siempre.

| Permiso | Qué controla |
|---|---|
| pvp | si se puede dañar a otros jugadores |
| armas / cuerpoACuerpo | si se puede usar armas de fuego / piñas |
| saqueo / recogida | levantar lo que otro dejó al morir / levantar objetos del suelo |
| robos / persecuciones / vehiculos / actividades | preparados para actividades futuras |
| perderMercaderiaAlMorir | si la mercadería que llevás queda tirada al morir (las armas siempre) |
| segundosBulto | cuánto tarda en desaparecer lo que queda tirado (10 a 86400) |
