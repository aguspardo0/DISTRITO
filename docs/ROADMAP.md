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
- [ ] Interés de red por grilla espacial (hoy compara todos contra todos: suficiente hasta ~60 jugadores por zona)
- [ ] Reducir ancho de banda en zonas densas (frecuencia por distancia, serialización propia)
- [x] Decisión sobre el host del servidor (Vultr São Paulo)

## Etapa 4 — Alinear con el documento de diseño (en curso)
- [x] Bellmont 1934; NPCs de las familias Moretti y Caruso
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
- [ ] Coaliciones entre cuadrillas
- [ ] Eventos de distrito (+10 a +20 de influencia, programados)
- [ ] Subfacciones, diplomacia y votaciones
- [ ] Prueba manual del equipo de la Etapa E

## Más adelante (sin orden fijado)
Economía con oferta/demanda, producción, empresas, NPC con objetivos, organizaciones y jerarquías, territorio, política, transporte entre ciudades, varias ciudades.
