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
- [ ] Prueba manual del equipo (cámara, controles, sensación)

Deuda conocida del prototipo: sin predicción del lado del cliente (con latencia alta el movimiento propio se siente demorado), sin interés de red (todos ven a todos), sin login.

## Etapa 2 — Hito jugable 1
- [ ] Cuentas y login (backend)
- [ ] Crear / cargar personaje
- [ ] Un NPC con interacción
- [ ] Un contrato simple: conseguir y entregar una mercancía
- [ ] Pago y transferencia validados por el servidor, con ledger
- [ ] Formar una cuadrilla
- [ ] Guardado y recarga correcta del estado

## Etapa 3 — Escala
- [ ] Interés de red por grilla
- [ ] Pruebas con 10 y 20 clientes automatizados, con métricas
- [ ] Decisión sobre el host del servidor

## Más adelante (sin orden fijado)
Economía con oferta/demanda, producción, empresas, NPC con objetivos, organizaciones y jerarquías, territorio, política, transporte entre ciudades, varias ciudades.
