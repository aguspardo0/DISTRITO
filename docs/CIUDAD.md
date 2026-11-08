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

## El Centro: fase 1 del mapa (2026-11-08)

Decisiones del equipo: **grilla generada** (manzanas de los años 50 por código, después se retocan a mano), **calles
con autos y veredas**, y **fachadas de época con locales vacíos** en planta baja.

- **Tamaño:** un cuadrado de **600 × 600 m** (de −300 a 300 en x y en z) alrededor de todo lo que ya existía: el
  Mercado sigue en el medio, Moretti al oeste, Valenti al norte, Caruso al sur y Los Muelles al este. Nada de lo de
  antes se movió (las posiciones guardadas, los NPC y las misiones siguen igual).
- **Calles:** grilla cada 75 m: manzanas de 57 m y calles de 18 m (11 m de calzada y dos veredas de 3,5 m). Alrededor
  de cada distrito de antes queda una calle de 18 m; las manzanas que lo pisarían se recortan
  (`CityLayout.DowntownBlocks`).
- **Edificios** (`CityLayout.DowntownBuildings`, semilla fija 1952): pegados, de 9 a 18 m de frente, mirando a la
  calle; en las manzanas hondas, dos filas con un pasaje de 4 m en el medio. De 2 a 6 pisos y, cerca del medio, alguna
  torre de hasta 15. Algún baldío. Sin interior. Cornisa, línea de planta baja, una franja de ventanas por piso y la
  vidriera del frente. Cada manzana es **una sola malla y un solo cuerpo de colisión**: cientos de edificios con pocas
  llamadas de dibujo.
- **Locales vacíos** (`CityLayout.EmptyShops`, códigos `local-001`…): uno de cada cinco o seis edificios bajos tiene
  un local en planta baja con toldo y cartel «SE ALQUILA». Todavía no se alquilan: falta definir las reglas de los
  negocios físicos (cuántos por jugador, alquiler o compra, qué pasa con los dueños inactivos). En el mapa (M) se ven
  como cuadraditos dorados.
- **Los barrios siguen cerrados:** un muro de ladrillo de 3,2 m del color de cada familia los rodea; solo se entra por
  su avenida, por el arco. Las familias no se mezclan.
- **Zona:** las calles nuevas son la zona **El Centro** (`el-centro` en `config/zonas.json`), tranquila como el Mercado.
- **Habitantes:** en el Centro caminan por las veredas, cruzan en las esquinas y usan la calle que rodea a los
  distritos de antes; no van por la calzada ni por los pasajes (`CityLayout.NpcArea`). La grilla de caminos pasó a
  400 × 400 celdas y marca los edificios de una (rápido aunque sean cientos).
- **Borde:** paredes invisibles en el borde del cuadrado. La bahía, el puerto grande y el anillo para autos son la
  fase 2.

## Día y noche visibles (2026-11-08)

`DayNightCycle` (solo en los clientes) sigue el reloj de Bellmont (`BellmontClock`, el mismo de la vida de los NPC y de
las horas pico): 6 horas reales de día y 6 de noche.

- El sol sale a las 6 y se pone a las 18 de Bellmont y cruza de este a oeste; de noche hay una luna fría y tenue.
- El cielo, la niebla y la luz ambiente pasan por amanecer anaranjado, día gris azulado, atardecer rojizo y noche azul
  oscuro, con transiciones suaves.
- Al oscurecer se prenden los faroles de los barrios (luz de verdad), brillan las cabezas de los faroles del Centro y
  los carteles de neón, y se encienden las ventanas de algunos pisos del Centro (siempre los mismos).
- Abajo a la derecha se ve la hora de Bellmont («Bellmont 19:40 · noche»).
