# La ciudad: la Calle Principal (antes el Mercado) y los barrios de las familias

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

## El Mercado en el medio del Centro y sin galpón de depósitos (2026-11-08)

- Pedido del equipo: el Mercado tiene que quedar bien centrado y conectado con la ciudad. La grilla del Centro ahora
  **arranca en el Mercado**: la primera calle lo rodea (ejes a ±39 m, pegada a su borde) y de ahí siguen las calles
  cada 75 m (±114, ±189, ±264) hasta el borde. El Mercado es la manzana del medio y sus calles siguen derecho por toda
  la ciudad (`CityLayout.StreetLines`).
- Se sacó el **galpón de Depósitos Valenti** (las bahías al oeste del Mercado que decían «Bahía libre»). Los depósitos
  de cuadrilla se siguen usando igual, desde el mostrador de la **Oficina Valenti**.

## La Calle Principal y Los Muelles contra el agua (2026-11-09)

Pedido del equipo: rehacer el Mercado «para que cuadre con la ciudad, con los negocios más separados y con una lógica».
Decisiones (preguntadas): **calle principal** (no plaza ni repartido por rubro), negocios **a una o dos cuadras**, los
locales de las familias **en la boca de su avenida**, **Los Muelles al borde** contra el agua y el **casino en la calle**.

```
               Av. Norte (Valenti)
                     │
 Av. Oeste ── GALPÓN · BOTICA · OF.VALENTI │ SASTRERÍA · RAMOS · HOTEL · CLUB DE JAZZ ── x=114 ── Avenida del Puerto ── LOS MUELLES │ agua
 (Moretti)   ════════════════ calzada de 12 m, veredas de 5 m ════════════════════════
             DINER · TALLER CARUSO │ LA ESQUINA · CASINO · BARBERÍA · FERRETERÍA · CORREO
                     │
               Av. Sur (Caruso)
```

- **La Calle Principal** (`MainStreet` en Server.Core; el código de zona sigue siendo `el-mercado`): 135 m de largo
  (x de −30 a 105), sobre el eje z = 0. Arranca donde entra la Avenida Oeste y termina en la calle del Centro x = 114.
  Calzada de 12 m con línea al medio, veredas de 5 m, faroles cada 12 m y sendas peatonales donde cruzan las avenidas.
- **Cada negocio en su lote** (16 m de fondo) con pasajes de 2 a 4 m entre lotes y un pasaje de servicio detrás.
  Norte: Galpón Moretti, Botica Ferri, Oficina Valenti, Sastrería Bianchi, Ramos generales, Hotel Roma (fachada) y el
  Club de jazz (marquesina violeta). Sur: Diner La Estrella, Taller Caruso, La Esquina (plazoleta con el puesto de café de
  Don Aldo, el kiosco, bancos, un árbol y un reloj), el Casino El Trébol, barbería, ferretería y correo (fachadas).
- **Las familias, en la boca de su avenida:** el Galpón Moretti donde entra la Avenida Oeste; la Oficina Valenti en la
  esquina de la Avenida Norte y el Taller Caruso en la de la Avenida Sur (la puerta de estos dos da a su avenida).
- **Las zonas** siguen igual: Calle del Almacén al oeste (Galpón), Los Talleres y La Oficina donde cruzan las avenidas y
  La Esquina al este (café, casino y club de jazz).
- **Avenida del Puerto:** sigue derecho a la Calle Principal (mismo eje) entre las manzanas del Centro hasta Los Muelles.
  Es calle del Centro (zona `el-centro`); los habitantes la cruzan en cada esquina.
- **Los Muelles** se mudaron al **borde este** (x de 246 a 300): los mismos galpones, sedes de cuadrilla, Gaetano y el
  contrabando, corridos 216 m (`CityLayout.DocksShift`). Del otro lado de la pared del borde se ve el agua.
- **Asaltos:** el camión de los Moretti va del Galpón por la Calle Principal y la Avenida del Puerto hasta Los Muelles;
  el cobrador de los Valenti da la vuelta por las veredas del centro de la calle (`config/eventos-pve.json`).
- **Sin tocar la economía:** las coordenadas de la población y de los negocios que usa la demanda (`mercado.json`,
  `negocios.json`) quedaron como estaban, para no cambiar reglas aprobadas. Si se quiere que la distancia a Los Muelles
  cuente con la ubicación nueva, hay que aprobarlo aparte.
- Los bots de prueba que van a un NPC ahora siguen el camino de la grilla de la ciudad (`BotSteering`).
- Se sacaron del escenario los siete edificios viejos del Mercado y su piso; todo lo arma `MainStreetBuilder`.
