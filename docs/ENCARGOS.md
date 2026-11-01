# Encargos e historias de las familias

Propuesta aprobada por el equipo (2026-11-01). Todo es contra NPC. El PvP se suma después, sobre lo mismo.

## Quién da trabajo
Cada familia tiene su gente en su barrio (ver `docs/CIUDAD.md`):

| Familia | Calle (entrada del barrio) | Negocios (plaza) |
|---|---|---|
| Moretti | Tano Ricci | Sofía Moretti |
| Valenti | Lupo | Vittorio, el contador |
| Caruso | Beppe, el capataz | Rosa, la de la cantina |

- **Calle:** cobro, paquete, asalto y vigilancia.
- **Negocios:** entrega, contrarreloj, recado y reparto.
- **El capo** recibe desde **50 de reputación** con su familia y da la historia de la familia.
- **Gaetano, el marinero** (Los Muelles) entrega los paquetes.

## Encargos personales
- Hay **4 ofertas por turno de 2 horas**, de familias y tipos variados, más **un encargo del día** que paga ×2,5 y da ×2,5 de experiencia.
- Se pueden tener hasta **2 en curso**. Los capítulos de historia no cuentan para ese límite.
- Las ofertas no se guardan: se calculan con una semilla por personaje y turno (`EncargoGenerator`), y se guardan recién al tomarlas (`character_encargos`).
- **Paga:** $8 + $1,50 por nivel, más algo de azar, multiplicado por el peso del tipo, con tope de $45. Da 60–150 de experiencia y +2 a +5 de reputación con la familia que encarga.
- La paga sale de la cuenta real de la familia, como en los contratos. La mercadería entregada va al stock de la familia.

| Tipo | Nivel | Peso | Qué hay que hacer |
|---|---|---|---|
| Entrega | 1 | 1,0 | Comprar en el Almacén y llevárselo a quien lo pidió |
| Contrarreloj | 2 | 1,0 | Lo mismo con tiempo límite (4–6 min); +25 % si llegás en la primera mitad |
| Cobro | 1 | 1,1 | Cobrarle al local de una familia rival (−2 de reputación con ellos) y traer el sobre. Si lo perdés, se cobra al vencer o al abandonar |
| Paquete | 2 | 1,2 | Buscarlo con Gaetano en Los Muelles (zona libre) y traerlo |
| Asalto | 3 | 1,35 | Asaltar algo de una familia rival (el camión de los Moretti, el cobrador de los Valenti o la caja de un local) y volver a cobrar |
| Recado | 1 | 0,9 | La familia te da un sobre sellado y se lo llevás a alguien del Mercado. Reemplaza a «venta», que no cerraba: solo compran dos locales y al 60 % |
| Reparto | 3 | 1,4 | 10–20 unidades cargadas en un vehículo tuyo o de tu cuadrilla, estacionado al lado de quien lo pidió |
| Vigilar | 1 | 0,8 | Quedarse a 10 m del local de una familia rival de 2 a 4 minutos y volver a contar |

- Abandonar no tiene multa, salvo el sobre de una cobranza que ya no tengas.
- Los encargos vencen a los 45 minutos de tomados. El contrarreloj vence con su tiempo y el del día, a medianoche.

## Historias (las da el capo)
- Cada historia tiene 4 capítulos, en orden, y cada capítulo es un encargo con texto propio.
- En total da **$150 y 800 de experiencia**, +5 de reputación por capítulo y un **premio único**.
- Cada capítulo vence a los 90 minutos. Si vence o lo abandonás, el capo te lo vuelve a ofrecer.

| Familia | Historia | Capítulos | Premio |
|---|---|---|---|
| Moretti | Mercadería que no llega | vigilar el Taller Caruso → cobrarles → vaciarles la caja → recuperar el cargamento con Gaetano | 5 % menos en el Almacén Moretti |
| Valenti | Números rojos | recado al Club de jazz → vigilar el Almacén Moretti → asaltar el camión de los Moretti → seis cajones para la gala | Mesa VIP de El Trébol sin esperar al rango 6 |
| Caruso | Hierro y sudor | paquete de repuestos con Gaetano → seis cajas de repuestos → cobrar en la Oficina Valenti → asaltar al cobrador | 10 % menos en los vehículos del Taller Caruso |

## En el juego
- **Libreta (L):** encargos en curso (qué hacer ahora, reloj, paga, abandonar) y el trabajo disponible, con quién lo da y en qué barrio.
- **Seguimiento** arriba a la derecha: el paso actual, a cuántos metros está y el reloj.
- **Marcadores:** una columna de luz del color de la familia sobre a quién tenés que ir, y la ruta marcada en el mapa (M).
- **Aviso grande al cumplir:** cuánto cobraste, si subiste de nivel o si el capo quiere conocerte.
- **Admin (F10):** +10 o +50 de reputación con las tres familias, para probar al capo y las historias.

## Pruebas
- `EncargoRulesTests`: números aprobados, variedad de familias, rivalidad, el del día y las historias.
- `EncargoApiTests`, contra la base:
  - tomar, entregar y cobrar;
  - cobro al rival y sobre perdido;
  - asalto por evento;
  - recado y vencimiento;
  - la historia de los Moretti con su premio en el Almacén.
