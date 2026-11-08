# Vida autónoma de los NPC

Documento del equipo (2026-11-01) e informe «DISTRITO — Vida autónoma de los NPC: auditoría e integración» (2026-11-08).

## Decisiones del equipo (2026-11-08)

- **Compras:** la plata la siguen moviendo los habitantes simulados de la economía (existen desde siempre: por eso un
  local vende apenas tiene mercadería). Los NPC visibles son **ambientación**: representan a esa gente (van al local,
  hacen la fila, salen con la compra) sin pagar aparte ni cambiar lo que vende el local.
- **Identidad liviana:** nombre, rutina y estado se guardan para muchos; solo los cercanos se dibujan y caminan.
- **Día y noche:** 6 horas reales de día y 6 de noche: dos días de Bellmont por día real (`BellmontClock`). La
  medianoche de Bellmont cae a las 0:00 y 12:00 de Argentina; amanece a las 3:00 y 15:00, anochece a las 9:00 y 21:00.
  La economía, La Gaceta y el crítico siguen con el día real.
- **Policía:** entra en la prueba; patrulla y acude a avisos, sin arrestar todavía.
- **Casas:** una puerta por vecino en su barrio, sin interior.
- **Gente comprando en el mundo:** vecinos que salen a comprar y vuelven con la bolsa (ambientación).

## Etapa 1 (2026-11-08): identidad y estado guardado

- `config/vida.json`: rasgos de personalidad (con opuestos que se complementan), barrios, lugares de trabajo,
  profesiones con turno, edad y plata, y listas de nombres de época. Se valida con errores claros.
- `ResidentGenerator` (compartido): crea los 30 habitantes de la prueba (8 trabajadores, 6 comerciantes,
  4 transportistas, 8 vecinos y 4 policías, uno de cada dos de noche). Determinista: la misma semilla da los mismos
  habitantes.
- Backend (`LifeStore`, migración 0036): los crea **una sola vez** la primera vez que se piden y guarda su último
  estado y los hechos importantes (`npc_residents`, `npc_resident_states`, `npc_resident_events`).
  Endpoints internos: `GET /internal/life/residents`, `POST /internal/life/save`,
  `GET /internal/life/residents/{id}/history`.
- Servidor del juego (`LifeNetwork`): los carga al arrancar (línea `[DISTRITO] VIDA habitantes=30 …` en el log) y
  guarda su estado cada 5 minutos. Todavía no se mueven: eso viene con las rutinas y la navegación.

La plata de cada habitante es solo un estado para sus decisiones (si le alcanza para algo), no una cuenta del libro.

## Etapas 2 a 4 (2026-11-08): rutinas, necesidades y decisiones

`LifeSimulation` (compartido, lógica pura y determinista):

- **Necesidades** de 0 a 1: hambre, cansancio y ganas de ver gente. Suben con las horas de Bellmont y bajan comiendo,
  durmiendo o charlando; trabajar cansa (menos a los trabajadores).
- **Decisiones con puntaje:** trabajar o patrullar (si le toca el turno), dormir, comer, ver gente, comprar, pasear o
  quedarse en casa. Cada opción suma según necesidades, personalidad (sociable, reservado, temeroso, ambicioso,
  trabajador, perezoso), hora, día o noche y plata; un azar controlado por habitante desempata. Siempre queda anotado
  el motivo («hambre 0,82 y le alcanza ($12,30)»).
- **Interrupciones:** cada 15 minutos de Bellmont se revisa si algo urgente corta lo que hace (mucha hambre o cansancio).
- **Desplazamientos lógicos:** sale de donde está, camina a 1,3 m/s hasta el lugar y ahí hace la actividad. Los
  policías van de parada en parada durante el turno. Desde la etapa 5 los cercanos se dibujan (ver abajo).
- **Plata del habitante:** cobra al terminar el turno (o al levantarse, si no trabaja) y gasta en comer, salir y
  comprar (`gastos` en `config/vida.json`). Es solo su estado: no toca la economía.
- **Ponerse al día:** como cada uno tiene la hora en la que termina lo que hace, después de un reinicio se recalcula
  lo que pasó mientras tanto.

En el servidor del juego, `LifeNetwork` los avanza cada segundo y cada 5 minutos escribe en el log qué hacen
(`VIDA hora_bellmont=… trabajando=… durmiendo=…`). **Inspección:** panel de administración (F10) → «Habitantes» →
qué hace cada uno, por qué y cómo está de hambre, cansancio y ganas de ver gente.

## Etapa 5 (2026-11-08): navegación y niveles — los cercanos se ven caminando

- **Caminos** (`CityNavigation`, en `Distrito.Server.Core/World`): grilla de 1,5 m sobre el plano de `CityLayout`;
  una celda es caminable si está adentro de un distrito y a más de 60 cm de cualquier edificio de `MapPresence`.
  Camino más corto con A* (sin cortar esquinas) y después se queda solo con las esquinas que hacen falta. Si el destino
  cae adentro de un edificio, el camino termina en la vereda más cercana (la puerta). La grilla sale del mismo plano
  que el mundo 3D, así que crece sola cuando crezca el mapa.
- **Simulación:** `LifeSimulation` recibe el buscador de caminos; cada decisión guarda las esquinas (`Path`) y el
  tiempo de viaje sale del largo real del camino. Al ponerse al día después de un reinicio no se buscan caminos (son
  viajes que ya pasaron).
- **Lugares cerrados:** en `config/vida.json` cada lugar tiene `"adentro"`; al llegar a uno cerrado (o a su casa) el
  habitante entra y deja de verse hasta que sale. El kiosco, el taller, los muelles y el puesto de policía son al aire
  libre. La comisaría pasó a ser el **puesto de policía de la Avenida Norte** (24,5; −33): el punto viejo (0; −60)
  quedaba en el borde del Barrio Valenti.
- **Tres niveles** (`ResidentLevels`): **cerca** (hasta 65 m de un jugador, se suelta a los 75 m para que no
  parpadeen): se dibujan; **intermedio** (hasta 150 m): no se dibujan pero ya están por aparecer; **lejos**: solo
  viven en la simulación. El log del servidor los cuenta (`cerca=… intermedio=… lejos=…`) y el panel de
  administración también.
- **Red:** a cada jugador se le manda **un tramo por decisión** (`ResidentView`: quién es, la actividad, la hora de
  salida y de llegada, las esquinas y si al llegar se mete adentro) y cuándo deja de verse. No se manda la posición a
  cada rato: el cliente lo hace caminar solo con la hora del servidor (corrige la diferencia de reloj con cada paquete).
  30 habitantes cerca son unos pocos KB por minuto.
- **Cliente:** cada habitante cercano es una figura con las piezas de los personajes (`ResidentLooks`: la policía de
  azul con gorra; las mujeres de vestido; el resto con ropa variada según una semilla), camina por las esquinas con la
  animación de caminata, mira hacia donde va y muestra su nombre cuando estás a menos de 14 m. Al entrar a un local o
  a su casa desaparece; al salir vuelve a aparecer en la puerta.
- **Pruebas:** `CityNavigationTests` (ningún camino atraviesa edificios ni sale de la ciudad, todos los lugares y
  casas se alcanzan, y el cliente calcula la misma posición que el servidor). En la prueba de persistencia el log del
  bot muestra `VIDA_CLIENTE habitantes_cerca=…` si alguno pasó cerca.

## Etapa 6 (2026-11-08): relaciones

`LifeSociety` (compartido, determinista, sin plata: es ambientación):

- **Qué se guarda:** cada habitante tiene, de los que conoce (hasta 12), un número de −1 a 1 (`Ties` en su estado).
  Desde 0,25 se **conocen**, desde 0,6 son **amigos** y desde −0,4 para abajo **se llevan mal**.
- **Al principio:** cada uno conoce un poco a dos o tres vecinos de su barrio y a sus compañeros de trabajo.
- **Cómo cambia:** cada 15 minutos de Bellmont, los que están en el mismo lugar (trabajando, comiendo, charlando, de
  visita) se acercan o se alejan según qué tan **compatibles** son: los sociables y los del mismo barrio se acercan,
  los agresivos y los desconfiados chocan. Los compañeros de trabajo se acercan un poco más; los que salieron a
  charlar, el doble. El **cliente** que come o compra donde atiende un comerciante le va tomando **confianza** (menos
  los desconfiados). En la calle o durmiendo no se cruzan con nadie.
- **Qué cambia en sus decisiones:** si un amigo está en el diner o el kiosco, va ahí («su amigo Sal está en el Diner La
  Estrella»); si un amigo está en su casa, puede ir a **visitarlo** (actividad nueva «de visita»: entra a la casa y no
  se ve); come y compra donde atiende alguien en quien confía; y no sale a charlar donde está alguien con quien se
  lleva mal.
- **Hechos importantes:** cuando dos se hacen amigos o empiezan a llevarse mal queda anotado en su historial y en el
  log del servidor (`VIDA relacion amistad: …`). El resumen cuenta `amistades=… rivalidades=…`.
- **Inspección:** el panel de administración muestra las relaciones de cada uno con nombre y valor.
- **Pruebas:** `LifeSocietyTests` (cuatro días de Bellmont con los 30: se hacen amigos, las relaciones cambian las
  decisiones, es determinista y se guarda igual).

## Etapa 7 (2026-11-08): reacciones

- **Qué ven y oyen:** el servidor avisa a la vida de los NPC (`CombatNetwork.Incident`) cuando hay un **tiro**
  (se oye hasta 45 m), alguien queda **herido** (30 m, asusta más) o un **asalto** a un local, al camión o al cobrador
  (20 m). Los tiros seguidos en el mismo lugar cuentan una sola vez cada 20 segundos.
- **Qué hace cada uno** (`LifeSimulation.React`, determinista), según su personalidad y qué tan cerca estaba:
  - la **policía de turno** va a ver qué pasó (actividad «acudiendo») y se queda un rato mirando: sin arrestos, como
    decidió el equipo;
  - los más asustados **salen corriendo a su casa** (a 3,2 m/s, se ve que corren);
  - otros **se meten en el local cerrado más cercano** (o en su casa, si está más cerca) y se quedan adentro;
  - los confiados y pacíficos **avisan a la policía**: van hasta dos policías de turno, aunque estén lejos;
  - los más duros **siguen con lo suyo**;
  - los que ya estaban adentro **se quedan adentro** un rato más; los que duermen no se enteran.
- **Después del susto:** por un rato (más largo cuanto más miedo tuvo) prefiere quedarse en casa y sale menos a
  pasear, comprar o ver gente («todavía está asustado por lo que vio»).
- **Registro:** cada reacción importante queda en el historial del habitante y el log del servidor resume cada hecho
  (`VIDA reaccion tiro en (12,-4): avisaron=1 escondidos=2 huyeron=1 policias=2 siguieron=3`).
- **Para probarlo a mano:** panel de administración (F10) → «Habitantes» → «Tiro al aire acá»: simula un tiro donde
  estás parado (sin daño ni balas) y muestra qué hizo cada uno.
- **Pruebas:** `LifeReactionTests`.

## Etapa 8 (2026-11-08): la gente con la bolsa y hablar con ellos

Sigue siendo **ambientación** (decisión del equipo): la plata la mueven los habitantes simulados de siempre.

- **La bolsa:** el que sale de comprar (botica, ramos generales, sastrería, kiosco) camina hasta su próximo destino con
  una bolsa de papel en la mano (`ResidentState.Bag`, viaja en el tramo de red).
- **Hablar con ellos:** cerca de un habitante (2,4 m) aparece «[E] Hablar con …» (los mostradores y las puertas tienen
  prioridad). El servidor revisa que estés cerca y contesta (`ResidentTalk`):
  - **el saludo:** quién es, de qué trabaja, de qué barrio es y qué está haciendo («¡Ahora no, que hay tiros!» si
    huye; «Todavía tengo el corazón en la boca» si se asustó hace poco);
  - **«¿Pasó algo raro por acá?»:** lo que vio (tiros, heridos, asaltos de las últimas 6 horas, con el lugar y cuándo)
    o lo que le contó un amigo que lo vio — los rumores corren por las amistades de la etapa 6. El desconfiado no
    suelta nada y la policía sabe de todos los avisos;
  - **«¿A quién conoce en el barrio?»:** sus amigos, conocidos y con quién se lleva mal;
  - **«¿Dónde conviene comprar?»:** el comerciante en el que confía, si tiene uno.
- **Registro:** cada pregunta queda en el historial del habitante («Un forastero le preguntó…»).
- **Pruebas:** `ResidentTalkTests`.
- **Queda para más adelante:** que la fila de los locales del Mercado la formen habitantes con nombre (hoy son
  figuras genéricas) y que el habitante se frene mientras le hablás.
