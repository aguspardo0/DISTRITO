# La ciudad: el Mercado y los barrios de las familias

Pedido del equipo (2026-11-01): las familias son rivales y no se juntan; cada una tiene su barrio, separado de los otros.
Los proveedores y comerciantes están en otra zona: el Mercado (el barrio original), que es terreno neutral.

```
                 Barrio Valenti (norte) · piedra y art déco · «El dinero no duerme»
                          │ Avenida Norte
Barrio Moretti ── Avenida Oeste ── EL MERCADO ── Los Muelles (zona libre)
ladrillo · «Ladrillo, familia y palabra»   │ Avenida Sur
                 Barrio Caruso (sur) · obrero · «Acá se trabaja con las manos»
```

- El plano vive en un solo lugar: `server/Distrito.Server.Core/World/CityLayout.cs`. Con ese plano se arman el mundo 3D
  (en el servidor y en los clientes, `game/scripts/World/CityBuilder.cs`), las paredes del borde (calculadas solas),
  el mapa (M), las zonas de combate y la validación de los puntos de aparición.
- Los barrios miden 100 × 100 m y solo se tocan con su avenida (de 30 m), que sale del Mercado. Los barrios no se tocan
  entre sí. La ciudad entera ocupa unos 244 × 320 m.
- Cada barrio tiene una cuadrícula de 3 × 3 manzanas con calles de 14 m. En el eje central están el bulevar de entrada
  (con arco, cartel y alumbrado eléctrico; en los techos, antenas de televisión y carteles de neón de 1952), la plaza (fuente de los Moretti, obelisco de los Valenti, monumento al trabajo de los
  Caruso) y, al fondo, el cuartel de la familia (estandartes, nombre, guardaespaldas). Las otras manzanas se generan
  con una semilla fija por familia: son iguales en todos los equipos.

## La gente de cada familia y el capo
Hablar con el capo es exclusivo (pedido del equipo): primero se trabaja con su gente, que está en el mapa.

| Familia | Entrada del barrio (calle) | Plaza (negocios) | Capo |
|---|---|---|---|
| Moretti | Tano Ricci | Sofía Moretti, la sobrina | Don Moretti |
| Valenti | Lupo, el chofer | Vittorio, el contador | Don Valenti |
| Caruso | Beppe, el capataz | Rosa, la de la cantina | Don Caruso |

El capo espera en la puerta del cuartel. Recibe a quien tiene **50 de reputación** con la familia
(`FamilyPeople.BossReputation`); antes, el guardaespaldas te manda a hablar con su gente. Los acuerdos de temporada
también se pueden firmar con el capo, una vez que te recibe.

## Pruebas
- `CityLayoutTests`: los distritos no se pisan, cada barrio solo toca su avenida, las uniones están abiertas, cada pared del
  borde separa adentro de afuera, los edificios no tapan el bulevar y cada NPC está en un lugar libre.
- En la prueba de escala, un bot va por la Avenida Oeste hasta la plaza Moretti y otro sube al Barrio Valenti y choca
  con el borde.
