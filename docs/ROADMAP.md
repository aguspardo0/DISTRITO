# Roadmap

Cada etapa debe compilar, tener sus pruebas en verde en CI y quedar explicada antes de pasar a la siguiente.

## Etapa 0 — Base técnica *(completa)*
- [x] Diagnóstico del entorno
- [x] Documentación de arquitectura inicial
- [x] Repositorio, solución, proyecto Godot, backend, PostgreSQL, tests, CI en verde

## Etapa 1 — Prototipo técnico de red *(completa, falta prueba visual del equipo)*
- [x] Escena 3D con un barrio de prueba
- [x] Personaje en tercera persona
- [x] Servidor dedicado headless
- [x] Dos jugadores conectados viéndose
- [x] Movimiento validado y replicado por el servidor
- [x] Prueba automática en CI: servidor + 2 clientes headless
- [x] Ejecutable de Windows publicado automáticamente (release `prototipo`)
- [x] Prueba manual del equipo (cámara, controles, sensación)

Deuda conocida del prototipo: sin predicción del lado del cliente (con latencia alta el movimiento propio se siente demorado), sin interés de red (todos ven a todos), sin login.

## Etapa 2 — Hito jugable 1 *(completo en CI; falta servidor en internet para probarlo entre casas)*
- [x] Cuentas y login (backend)
- [x] Crear / cargar personaje
- [x] Un NPC con interacción
- [x] Un contrato simple: conseguir y entregar una mercancía
- [x] Pago y transferencia validados por el servidor, con ledger
- [x] Formar una cuadrilla
- [x] Guardado y recarga correcta de la posición (probado en CI: salir, volver, aparecer en el mismo lugar)
- [x] Servidor en internet (backend + base + servidor de juego) para probar con cuentas entre casas (Vultr, despliegue automático)

## Etapa 3 — Escala
- [x] Interés de red por distancia (radio con histéresis), probado en CI
- [x] Pruebas con 10 y 20 clientes automatizados, con métricas (ver NETWORKING.md)
- [x] Prueba de escala real (2026-11-07): 100 bots desde 4 máquinas contra el servidor de internet, todos juntos. Hasta ~80 bien en el servidor de 1 núcleo; con 100 se satura (CPU 93 %, 43 cuadros, 2 desconexiones) y la red manda 65 Mbps. Ver NETWORKING.md
- [ ] Interés de red por grilla espacial (hoy compara todos contra todos: suficiente hasta ~60 jugadores por zona)
- [x] Reducir ancho de banda de los personajes en zonas densas (2026-11-08): paquetes propios de 20 bytes por personaje, frecuencia por distancia y nada si está quieto. Con 100 jugadores juntos, de 65 a 21 Mbps (ver NETWORKING.md)
- [ ] Lo mismo para vehículos, NPC y objetos con física; investigar los picos sueltos de 20–45 ms en el servidor de 1 núcleo
- [x] Decisión sobre el host del servidor (Vultr São Paulo)

## Etapa 4 — Alinear con el documento de diseño (en curso)
- [x] Bellmont (época actualizada a 1952 el 2026-11-02, decisión del equipo); NPCs de las familias Moretti y Caruso
- [x] Reputación por familia (0–1000) con historial auditable
- [x] Predicción de movimiento del jugador local
- [x] Dirección de arte v0.1 (`docs/Design/ART_DIRECTION.md`)
- [x] Pequeña actividad económica administrada por el jugador: puesto de café (ADR-005)
- [x] Tres contratos (MVP del documento): repuestos, pedido urgente con plazo, cobranza en cuadrilla
- [x] Cuadrilla progresiva (cupo 3, niveles 4 y 5 preparados), roles Jefe/Segundo/Miembro y sucesión por inactividad (14 días)
- [x] Empleado básico: ayudante opcional del puesto de café

## Hoja de ruta v0.2.1 — vertical slice
Estado detallado en `docs/AUDIT.md`. Los 7 objetivos están implementados y probados en CI (incluido el ciclo completo con dos bots de una cuadrilla).
- [x] Prueba manual con dos personas del ciclo completo (equipo, 2026-10-31: funciona)
- [x] Ampliar la cuadrilla a 4 y 5 por contratos cumplidos y reputación de los integrantes

## Etapa B — Estabilidad multijugador (completa)
- [x] Prueba con latencia, variación y pérdida de paquetes (netem en CI)
- [x] Prueba de colisiones (edificios y bordes del barrio; se agregaron bordes invisibles)
- [x] Métricas: CPU, RAM, RTT, ancho de banda, desconexiones y errores
- [x] Prueba de corte de cliente a mitad de partida (el resto sigue jugando, se guarda el progreso; un cliente colgado sale a los ~8 s)

## Etapa C — Mundo y visibilidad (completa)
- [x] Mapa del barrio (tecla M) separado del mundo 3D: nombre y posición redondeada (0,5 m) de todos, 1 vez por segundo
- [x] Privacidad decidida por el equipo: todos ven a todos en el mapa (las opciones por grupo/familia quedan para cuando haya permisos)
- [x] Prueba en CI: fuera del radio de interés no se cargan modelos, pero el mapa muestra a los dos
- [x] Prueba visual del mapa por el equipo (2026-10-31: funciona)

## Etapa D — Alpha de contenido (en curso)
- [x] Progresión del personaje: nivel 1–30 y oficios (ver `docs/Design/PROGRESSION.md`)
- [x] Menú de opciones en partida (Esc) y arreglos de ventana/pantalla completa (pedido del equipo)
- [x] Segundo negocio con proveedor: club de jazz (cajones de bebidas del Almacén Moretti, músico opcional, +1 de noche)
- [x] Familias NPC con estados: neutral, cooperación y rivalidad según las últimas 24 h (ver `FamilyRules`)
- [x] Herramientas de la cuadrilla: caja común, permisos por rol configurables por el jefe y aporte automático de contratos (0–20 %)
- [x] Prueba manual del equipo de la Etapa D (2026-10-31: anda bien)
- [x] Contratos por nivel: reparto para la fiesta (nivel 3, Logística 2), revisar las cuentas (nivel 5, Negociación 3), cobranza grande (nivel 8, 15 Caruso, dos compañeros)

## Etapa E — Beta social y territorial (en curso)
- [x] Territorio: 4 zonas, influencia por contratos (+3) y ventas (+1) con tope de 30/día/zona, control con 20+ (+5 % en contratos), temporadas de 4 semanas con ganadores en el historial
- [x] Coaliciones entre cuadrillas: hasta 3, territorio sumado al 75 %, cobranzas con aliados, 24 h de espera al salir
- [x] Eventos de distrito programados y editables (`config/eventos.json`, ver `docs/EVENTOS.md`)
- [x] Votaciones en la cuadrilla (cambiar de jefe, pagar de la caja) y acuerdos de temporada con las familias
- [x] Familias de jugadores y subfacciones: preparadas, con un máximo de 5 en el servidor y **la fundación cerrada por ahora** (decisión del equipo, 2026-11-01; se abre en `config/familias.json`). Ver `docs/FAMILIAS-JUGADORES.md`
- [ ] Abrir la fundación de familias y sumar subfacciones, lealtad a una familia NPC y permisos por rango (cuando el equipo lo decida)
- [x] PvP regulado a piñas: zonas en disputa y eventos, zona segura cerca de los locales, nivel 3+, herido 10 s, mercadería al suelo
- [x] Correr (Shift)
- [x] Guerras declaradas entre cuadrillas: empiezan a los 10 min, duran 24 h, PvP en todo el barrio entre las dos salvo zonas seguras
- [x] Armas de fuego: pistola (nivel 3), escopeta (5), Thompson (8); munición por cajas
- [x] Documento PvP, etapas 1–4: el disparo sale del cañón (el cuerpo gira y apunta, retroceso, recarga, paredes); herido 30 s → rematar o desangrarse = muerto; un compañero levanta con E 3 s (vuelve con 30 de salud); reaparición a los 10 s
- [x] Al morir (no al quedar herido) caen armas y munición siempre, la mercadería según la zona; la plata nunca. El bulto queda en la calle con su lugar y vencimiento, sobrevive a reinicios y se levanta una sola vez
- [x] Reglas por zona editables sin tocar código (`config/zonas.json`, ver `docs/ZONAS.md`): PvP, armas, piñas, saqueo, robos, persecuciones, vehículos, recogida, actividades, excepciones por disputa/evento/guerra
- [ ] Documento PvP, etapas siguientes: sistema general de actividades PvP, visibilidad de objetivos en el mapa por actividad, integración con inventario e historial de pérdidas, ganchos de consecuencias, interfaz de actividad
- [ ] Prueba manual del equipo de la Etapa E

### Mapa y herramientas
- [x] Los Muelles: zona libre al este del barrio (PvP y robos siempre; se configura en `config/zonas.json`), con galpones, cajones y muelle
- [x] Panel de administración (F10) para pruebas: plata, objetos, nivel, ir a lugares y curarse; solo cuentas de `config/admins.json` o `Admin__Accounts`

### Ciudad y misiones con las familias (pedido del equipo, 2026-11-01)
- [x] Ciudad ampliada: el Mercado (proveedores y comercios) en el centro y un barrio por familia (Moretti al oeste, Valenti al norte, Caruso al sur), separados y unidos solo por avenidas. Ver `docs/CIUDAD.md`
- [x] La gente de cada familia en el mapa (dos por barrio); el capo recibe desde 50 de reputación
- [x] Encargos personales: 8 tipos, 4 cada 2 h y uno del día; libreta (L). Aprobado: $8 + $1,50 por nivel (tope $45), 60–150 XP, +2 a +5 de reputación. «Venta» se reemplazó por «recado». Ver `docs/ENCARGOS.md`
- [x] Seguimiento en pantalla, marcadores en el mapa y en el mundo, aviso al cumplir
- [x] Historias de las tres familias, las da el capo (4 capítulos; premios: 5 % en el Almacén, 10 % en vehículos, mesa VIP)
- [x] Operaciones de cuadrilla en el tablero (J): una por día, 3 etapas, 2+ integrantes; $120–250 a la caja y 60–120 de prestigio. Ver `docs/CUADRILLAS-SEDE.md`

### Cuadrillas: sede, prestigio y misiones; casino (pedido del equipo, 2026-11-01)
- [x] Sede en los galpones de Los Muelles ($250 + $2/ciclo; zona segura adentro; depósito 500 kg; reaparición)
- [x] Prestigio de cuadrilla con 10 rangos y beneficios; misiones generadas cada 3 h (7 tipos), tablero con J
- [x] Casino «El Trébol»: ruleta, blackjack y dados ($1–$50, VIP hasta $200 desde rango 6). Ver `docs/CUADRILLAS-SEDE.md`
- [x] Protección de novato: el PvP (atacar, ser atacado, forzar vehículos) se desbloquea en nivel 5; antes, todo es contra NPC
- [x] Admin: prestigio de cuadrilla, tablero nuevo, adelantar ciclos, pedidos listos y sacar eventos PvE al instante
- [x] Arreglo: la rueda del mouse ya no esconde el cursor dentro de las ventanas
- [x] Venta a NPC: 60 % del precio, tope 20 unidades por hora por jugador
- [x] Árbol de habilidades (K): un punto por nivel desde el 2; ramas Calle, Negocios y Logística con 16 nodos en 3 escalones (nivel 1, 5 y 10). Ver `docs/HABILIDADES.md`

### Eventos PvE (pedido del equipo, 2026-11-01)
- [x] Asaltos a NPC: camión de reparto de Moretti, cobrador de Valenti, contrabando en Los Muelles con guardias armados y caja de locales. Amenazar apuntando; botín real; −10 de reputación (y ánimo) con la familia asaltada. Ver `docs/ASALTOS.md` y `config/eventos-pve.json`

### Inventario, objetos y logística (documento del equipo, 2026-10-31)
Base: el libro central de objetos que ya existía (item_stacks + item_movements); no se reemplaza. Ver `docs/INVENTARIO.md`.
- [x] Etapa 1 — clases de objeto (armas, munición, herramientas, mercancías, materias primas, productos, documentos, equipamiento, misión) con descripción y propiedades opcionales; ventana de inventario (I) con peso y volumen
- [x] Etapa 2 — dar en mano a alguien cerca (3 m), soltar en la calle (bulto que otro levanta), separar cantidades, equipar armas desde el inventario; capacidad configurable (`config/inventario.json`, por ahora sin límite)
- [x] Etapa 3 — depósito de la cuadrilla en La Oficina (aprobado: $20 de la caja, $1 por ciclo mientras haya algo guardado, 300 kg); permisos del jefe (ver, guardar, retirar); con alquiler adeudado no se retira; si la cuadrilla se disuelve, lo guardado pasa al último miembro
- [x] Etapa 4 — historial: personal (qué entró y salió, cuánto, cuándo y por qué) y del depósito (quién guardó o retiró qué)
- [x] Etapa 5 — pedidos por mayor al Almacén Moretti (aprobado: precio de mostrador, 10 % menos desde 20, mín. 5, 1 ciclo cada 10 unidades, 50 por ciclo en cola; retiro gratis o envío al depósito +10 % mín. $1 y 1 ciclo más; cancelar con reintegro mientras se prepara)
- [x] Etapa 6 — vehículos (aprobado: camioneta de reparto $120, nivel 3, 400 kg; camión de carga $350, nivel 6, 1500 kg) en el Taller Caruso, propios o de cuadrilla; manejo arcade simulado en el servidor (G subir/bajar); carga desde el inventario; la carga queda adentro al bajarse y se puede forzar (5 s) donde la zona permite robos; el vehículo no se roba; persiste dónde quedó
- [x] Etapa 7 — existencias visibles en el mundo, atadas a los datos reales: galpón «Depósitos Valenti» (bahías por cuadrilla con cartel y kg), estantes del Almacén Moretti (llenos/medio/poco) y pedidos listos en pallets, carga a la vista en camioneta y camión; bultos agrupados (nunca uno por unidad)
- [x] Etapa 8 — fletes en consignación (aprobado: 20 cajas de repuestos de Moretti al Taller Caruso $60; 15 cajones de bebidas al Club de jazz $50; nivel 3; 15 min; si vencen se devuelve lo que tengas y lo que falte se cobra) y pedidos a Moretti con envío directo al negocio propio (+10 %, mín. $1). Las entregas siguen siendo a mano (decisión del equipo)

### Operación del servidor
- [x] Copias de seguridad automáticas de la base: cada 6 h (se guardan 28), antes de cada despliegue (5), verificadas, con restauración en un comando (`docs/DEPLOY.md`).
- [x] Copia cifrada fuera del servidor: cada 6 h, cifrada con age en el servidor, artefacto de GitHub por 30 días.
- [ ] Cargar la llave pública del equipo en `deploy/backup-recipients.txt` (hasta entonces no se exporta).

## Etapa F — Preparación de lanzamiento (en curso)
- [x] Copias de seguridad automáticas y restauración (ver «Operación del servidor»)
- [x] Tutorial de inicio: seis pasos (mapa, primera compra, las familias, primer encargo, la libreta, lo que viene), con luz y aro en el mapa sobre el destino; se guarda por personaje, los que ya tienen experiencia lo saltean y se puede saltar o repetir desde Esc. Sin premios (no toca la economía)
- [ ] Interfaz y accesibilidad, rendimiento, seguridad y anti-exploit, telemetría, moderación, soporte, pruebas de regresión y operación

## Próximo, aprobado por el equipo (2026-11-01), en este orden
1. [ ] **Economía de mercado:** población por zona con presupuesto, demanda que se satura, precio de referencia, producción, transporte y NPC que trabajan. Primero el simulador con la cadena de prueba (tostadero → Almacén → dos puestos de café, con el camión) y el modo sombra; las reglas aprobadas no cambian hasta que el equipo lo apruebe con números. Informe de integración: documento «DISTRITO — Economía de mercado: auditoría e integración».
   - [x] Etapa 1 (2026-11-02): simulador `tools/Distrito.MarketSim` + mercado de prueba (`config/mercado.json`, `shared/Distrito.Shared/Market/`) con 6 escenarios (normal, sobreoferta, escasez, suba de precios, competencia, falta de abastecimiento). La plata se conserva en todos; tests en `MarketTests`; workflow rápido `mercado.yml`. No toca el juego.
   - [x] Mercado visible (2026-11-02): pizarras en los locales, libreta de precios, La Gaceta de Bellmont, Nino el informante y libro de cuentas con clientes perdidos (tecla P). Ver `docs/MERCADO-VISIBLE.md`.
   - [x] Negocios que crecen (2026-11-02): clientela del mercado simulado (9.000 habitantes), fama, mejoras sin tope (capacidad, calidad, atracción) pagadas al Taller Caruso, precio a elección, crítico de La Gaceta, eventos diarios y gente visible en los locales. Ver `docs/NEGOCIOS-QUE-CRECEN.md`. Pendiente: protección de las familias.
   - [x] Etapa 2 (2026-11-08): modo sombra. El mercado nuevo corre en el backend real después de cada ciclo, con los locales reales, y anota lo que habría vendido cada uno y cómo se moverían los precios de cada zona, con avisos. No decide nada. Ver `docs/MODO-SOMBRA.md`.
   - [ ] Etapa 3: el equipo revisa los números del modo sombra (una o dos semanas con jugadores) y decide si se activa y con qué valores.
- [x] **Interiores del Mercado y física compartida** (2026-11-02): galpón Moretti, Taller Caruso, Oficina Valenti, ramos generales, botica, sastrería y diner, con puertas, mostradores adentro, arte provisorio reemplazable por modelos (.glb) y objetos con física (Jolt) que ven todos igual. Ver `docs/INTERIORES.md`. Pendiente: aprobar los productos de las tiendas; interiores del club de jazz, casino, cuarteles y barrios.
2. [ ] **Vida autónoma de los NPC** (documento del equipo, 2026-11-01): habitantes con identidad, rutinas, necesidades, decisiones deterministas, relaciones y reacciones, simulados por niveles (cerca, intermedio, lejos), usando la economía de mercado sin duplicarla. Empieza con una auditoría y un ecosistema de prueba de 30 NPC.
   - [x] Auditoría e integración (2026-11-08) y decisiones del equipo: NPC visibles como ambientación (la plata la mueven los habitantes simulados), día de Bellmont de 12 horas reales (6 de día y 6 de noche), policía sin arrestos, casas sin interior. Ver `docs/VIDA-NPC.md`.
   - [x] Etapa 1 (2026-11-08): identidad y estado guardado de los 30 habitantes (`config/vida.json`, `ResidentGenerator`, `LifeStore`, `LifeNetwork`) y el reloj de Bellmont.
   - [x] Etapas 2 a 4 (2026-11-08): rutinas, necesidades y decisiones (`LifeSimulation`), con interrupciones, desplazamientos lógicos, patrullas y la inspección en el panel de administración.
   - [x] Etapa 5: navegación y los tres niveles (los cercanos se ven caminando por las veredas, entran a los locales y patrullan).
   - [x] Etapa 6: relaciones (vecinos y compañeros que se conocen, amigos que se buscan y se visitan, confianza en el comerciante, gente que se lleva mal).
   - [x] Etapa 7: reacciones a tiros, heridos y asaltos (huir, esconderse, avisar a la policía, la policía acude sin arrestar, o seguir con lo suyo).
   - [x] Etapa 8: economía y jugadores como ambientación (la gente vuelve con la bolsa; hablar con ellos con E: quiénes son, qué vieron, rumores entre amigos, a quién conocen y dónde compran).
   - [ ] Etapa 9: optimización y prueba de población.
- [x] Mapa, fase 1 (2026-11-08): El Centro, 600 × 600 m alrededor de lo que ya existía; grilla generada de manzanas de los 50, calles con autos y veredas, edificios de época sin interior, locales vacíos «SE ALQUILA» y muros en los barrios (solo se entra por su avenida). Ver `docs/CIUDAD.md`
- [x] Primera hora (2026-11-08): turnos de empleado en la Esquina del café ($3 cada 10 minutos + propinas de hasta $1), con el minijuego de la urna (como se hacía el café en 1952); el tutorial guía de empleado a dueño del puesto sin obligar. Ver `docs/PRIMERA-HORA.md`
- [x] Rubro café (2026-11-08): tope de 6 turnos por día, la competencia (Café La Perla y El Gato Negro) y las misiones «El café de Don Aldo» (plata de la familia que encarga + fama del puesto)
- [x] Día y noche visibles (2026-11-08): sol y luna con el reloj de Bellmont, cielo y luz por hora, faroles, neones y ventanas que se prenden de noche, y la hora de Bellmont en pantalla
- [ ] Mapa, fase 2: la bahía, el puerto grande, la aduana, el barrio obrero y la estación; el anillo para autos

## Más adelante (sin orden fijado)
Economía con oferta/demanda, producción, empresas, NPC con objetivos, organizaciones y jerarquías, territorio, política, transporte entre ciudades, varias ciudades.
