# Interiores y física compartida

Pedido del equipo (2026-11-02): interiores de todo, con sentido de distribución, empezando por el Mercado (mayoristas en
galpones y tiendas minoristas), y física que se sienta, compartida entre jugadores.

## Cómo funciona

- **Se entra por la puerta** con **E**: puerta en la fachada, con cartel, luz y, en las tiendas, toldo a rayas. Lo decide
  el servidor: hay que estar en la puerta, a pie y en pie. Adentro, la salida está en la misma pared (cartel «SALIDA [E]»).
- **Los salones están armados aparte**, en fila, lejos de la ciudad (a partir de x = 3000, cada 300 m). Así adentro solo
  se ve a quien está en ese mismo local (el radio de interés es 60 m) y no pesa en la calle.
- **En el mapa**, quien está adentro figura en la puerta del edificio. Adentro **no se pelea** y la zona de territorio es
  la de la puerta.
- **Los tres mayoristas atienden en la vereda y también adentro**: el Almacén Moretti, el Taller Caruso y la Oficina
  Valenti tienen un segundo mostrador en su galpón u oficina (mismo NPC, mismas opciones). Así no cambió nada de lo que
  ya funcionaba.
- **El galpón Moretti muestra la mercadería real**: los estantes con stock y los pedidos por mayor listos para retirar
  están adentro.

## Los locales del Mercado

| Local | Edificio | Adentro |
|---|---|---|
| Galpón Moretti (mayorista) | Al oeste de la calle, puerta en la cara este | Oficina con mostrador y caja, escritorio y archivos; tres filas de estanterías altas; estantes con el stock real; pedidos listos; portón de carga con pallets, carretilla, cajones, barriles y bolsas |
| Taller Caruso | Al este, cara oeste | Sedán de 1950 en el elevador, banco de trabajo, pared de herramientas, mostrador, puerta «solo personal», cubiertas y barriles |
| Oficina Valenti | Al oeste, cara este | Recepción con mostrador, escritorio, archivos, sillones de espera, caja fuerte, cuadro de Don Valenti |
| Ramos generales La Esperanza | Sur de la calle | Estantes de mercadería en las paredes, mostrador con balanza y caja, heladera, barriles y bolsas |
| Botica Ferri | Sur de la calle | Estantes de frascos, mostrador con balanza, vidriera, puerta al laboratorio |
| Sastrería Bianchi | Sur de la calle | Maniquíes con traje y sombrero, percheros, espejo, mesa de corte con tela, probador con cortina |
| Diner La Estrella | Norte de la calle | Barra roja y cromada con taburetes, cocina, reservados, rocola encendida, mesas con sillas sueltas, piso damero |

Las cuatro tiendas ya se pueden recorrer y su dueño te atiende. **Todavía no venden:** los productos y precios esperan la
aprobación del equipo (ver la propuesta abajo).

## Arte provisorio y reemplazo por modelos definitivos

Cada mueble es un **lugar con un tipo** (`InteriorItem` en `server/Distrito.Server.Core/World/Interiors.cs`). El juego
dibuja una forma provisoria, pero si existe **`game/assets/interiores/<tipo>.glb`** usa ese modelo (la colisión sigue
siendo la caja del tipo). Para poner el arte final se copian los archivos; no hay que tocar código.

Medidas de cada tipo (ancho × alto × profundidad, en metros; el modelo se apoya en el piso, con el frente hacia +Z):

| Tipo | Medida | Tipo | Medida |
|---|---|---|---|
| mostrador | 3 × 1,05 × 0,8 | estante-alto (galpón) | 5 × 3,6 × 1,1 |
| estante (tienda) | 2,6 × 2,2 × 0,5 | estante-frascos | 2,4 × 2,2 × 0,45 |
| escritorio | 1,8 × 0,78 × 0,9 | archivo | 0,5 × 1,3 × 0,6 |
| porton | 4,5 × 4,5 × 0,2 | pallet | 1,2 × 0,15 × 1 |
| carretilla | 0,6 × 1,2 × 0,5 | elevador-auto (con auto) | 5 × 2,4 × 2,6 |
| banco-trabajo | 2,4 × 0,9 × 0,8 | pared-herramientas | 3 × 2 × 0,1 |
| puerta-trasera | 1,2 × 2,2 × 0,1 | sillon | 1 × 0,9 × 0,9 |
| planta | 0,5 × 1,3 × 0,5 | caja-fuerte | 0,8 × 1,1 × 0,7 |
| heladera | 1 × 1,9 × 0,7 | vidriera | 1,8 × 1 × 0,6 |
| mesa-corte | 2,2 × 0,9 × 1,1 | maniqui | 0,5 × 1,75 × 0,4 |
| perchero | 1,6 × 1,6 × 0,5 | espejo | 1 × 2 × 0,08 |
| probador | 1,4 × 2,2 × 1,4 | barra (diner) | 9 × 1,1 × 0,8 |
| taburete | 0,4 × 0,75 × 0,4 | reservado | 1,6 × 1,1 × 1,4 |
| rocola | 0,9 × 1,6 × 0,6 | mesa | 0,9 × 0,75 × 0,9 |

Sin colisión (decorado): caja-registradora, balanza, cartel, cuadro, lampara-colgante.

## Física compartida (Jolt)

- El proyecto usa **Jolt Physics** (Godot 4.7).
- **Objetos con física:** cajones, cajas, bolsas, barriles, sillas, botellas, cubiertas y tachos, en las veredas del
  Mercado y adentro de los locales.
- **Los simula el servidor** y todos los ven igual. Se empujan caminando, los autos los atropellan y se caen.
- **El servidor manda solo los que se mueven** (12 veces por segundo, a quien esté a menos de 80 m). Los quietos no
  gastan red, y los clientes suavizan el movimiento.
- **Vuelven solos:** si alguien los deja tirados, a los 10 minutos vuelven a su lugar. Si se caen del mundo, vuelven al
  instante.
- **Capa de colisión 3 (valor 4):** solo para estos objetos. La predicción local del jugador no choca con ellos (el
  empujón lo decide el servidor), así no hay tirones.

## Propuesta de productos (para aprobar)

| Tienda | Productos propuestos | Para qué sirven |
|---|---|---|
| Ramos generales | Sándwich $8 · Atado de cigarrillos $4 · Provisiones (cajón) $20 | El sándwich cura un poco fuera de pelea. El atado se le regala a la gente de las familias: +1 de reputación, una vez por día. Las provisiones son mercadería para el diner y para encargos |
| Botica | Venda $15 · Tónico $25 · Botiquín $60 | La venda cura 30 de salud en 3 s. El tónico hace que te levantes más rápido cuando quedás herido. El botiquín cura todo, fuera de pelea |
| Sastrería | Gorra $40 · Sombrero fedora $80 · Campera de cuero $120 · Traje cruzado $250 | Cambian el aspecto del personaje. El traje podría pedirse para la mesa VIP del casino |
| Diner | Café $5 · Sándwich $8 · Tarta de manzana $6 | Se consumen ahí: un poco de salud. Es lugar de encuentro de cuadrillas y, más adelante, un negocio que se puede comprar |

## Sin teletransporte: los salones en su lugar (2026-11-09)

Pedido del equipo: «que la puerta tenga física, que puedas entrar y que no haya un salto hacia el lugar ni una carga».

- **Cada salón está adentro de su edificio** de la Calle Principal. El lote mide lo mismo que el salón más sus paredes
  (0,3 m) y la pared de la puerta queda sobre la línea de edificación. `InteriorPlan.Origin`, `ToWorld` y `Yaw` lo
  calculan a partir de la puerta (`Door`, `Outward`, `DoorX`); los muebles y objetos siguen en coordenadas del salón.
- **La puerta es un objeto con física:** una hoja con colisión que gira sobre su bisagra hacia adentro. Con **E** al lado
  (de afuera o de adentro) se abre o se cierra; lo decide el servidor y lo ven todos igual (`DoorNetwork`). Se cierra
  sola a los 12 segundos si no hay nadie en el paso. Los que entran después reciben cómo está cada puerta.
- **Se entra caminando:** no hay salto ni pantalla de carga. Por las vidrieras (comercios, diner y oficina) se ve de
  la calle hacia adentro y al revés.
- **Paredes:** de afuera, el color de la fachada del lote; de adentro, el revestimiento del local. Arriba del techo del
  salón sigue el resto del edificio (los pisos de arriba no se recorren).
- **Reglas que se mantienen:** adentro sigue siendo zona segura (no se pelea ni se dispara a través de la vidriera, ni
  de adentro hacia afuera) y vale la zona de la calle donde está la puerta.
- Lo que cambió para quien estaba guardado en los salones viejos (lejos de la ciudad): aparece en su punto de aparición.
- Captura de prueba nueva: `puerta-abierta` (el diner con la puerta abierta, desde la vereda de enfrente).
