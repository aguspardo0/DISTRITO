# Auditoría del proyecto — 2026-10-31 (contra Hoja de ruta v0.2.1)

Método: revisión del código en `main` (commit `ab83ec1`, sin cambios locales ni ajenos), resultado de la CI de ese commit
(build, 130 pruebas automáticas, pruebas de punta a punta con bots, carga con 10/20 bots, despliegue y prueba contra el servidor real).
La CI es la compilación de referencia: el entorno de desarrollo no tiene .NET ni Godot instalables.

Estados: ✅ terminado y probado · 🟡 implementado parcialmente · 🔌 creado pero no integrado · ⏳ pendiente · ⛔ bloqueado

## Matriz de estado

| Sistema | Estado | Evidencia | Falta |
|---|---|---|---|
| Cliente Godot (menú, HUD, cámara, controles) | ✅ | Export Windows en cada push; prueba manual del equipo OK | — |
| Servidor dedicado headless | ✅ | Arranque y bots en CI; corre en Vultr con systemd | — |
| Conexión | ✅ | Bots conectan en todas las pruebas | — |
| Reconexión | 🟡 | Al perder el servidor se vuelve al menú y se puede reentrar a mano | Reconexión automática; prueba de corte de servidor |
| Movimiento autoritativo + validación | ✅ | `MovementRules` + tests; servidor mueve, cliente solo envía intención | Pruebas con pérdida de paquetes (Etapa B) |
| Predicción local | ✅ | `LocalPrediction`; error final 0–0,17 m en todas las pruebas | Prueba con latencia simulada (Etapa B) |
| Sincronización + interés de red | ✅ | `InterestManager`; prueba de culling (radio 12 m) OK | Mapa/presencia separada (Etapa C) |
| Persistencia: personaje, posición | 🟡 | Ronda 1/2 en CI: vuelve al mismo lugar | **Ubicación segura**: una posición guardada inválida no se corrige |
| Persistencia: inventario, dinero, reputación, negocio | ✅ | Todo en PostgreSQL, transaccional, probado | — |
| Cuentas y sesiones | ✅ | PBKDF2, tokens opacos, tests | — |
| NPC / familias | 🟡 | 4 NPC (Moretti, Caruso, Valenti, esquina), diálogos validados en servidor | Familias como instituciones con estados (diseño v0.1) |
| Reputación | ✅ | 0–1000, eventos auditables, persiste, sube por contrato | Más fuentes (otros contratos) |
| Contratos | 🟡 | 1 contrato completo (comprar → entregar → cobrar) | **Tres contratos distintos** (hito 4) |
| Economía (libro contable) | ✅ | Doble entrada, idempotencia, concurrencia, conservación probada | — |
| Negocio | 🟡 | Puesto de café: licencia, stock, ventas por ciclo offline, alquiler, caja, retiro | **Empleados básicos y estado operativo** (hito 3) |
| Cuadrillas | 🟡 | Fundar, invitar, aceptar/rechazar, salir; historial en `crew_events` | **Capacidad inicial 3** (hoy 5); roles básicos; historial visible en el juego |
| Liderazgo / sucesión | 🟡 | Jefe; herencia al irse; sucesión por inactividad (14 días) | La hoja de ruta pide **posponer** la sucesión automática → decisión pendiente |
| Ciclo completo (hito 7) | ⏳ | Las piezas existen por separado | "Organizar integrantes" para una actividad conjunta |
| Métricas | 🟡 | `ServerMetrics` en CI | **No activas en el servidor real** |
| Bots de carga | ✅ | 10 y 20 bots, interés de red, métricas por corrida | — |
| CI | ✅ | 5 jobs; anotaciones legibles | — |
| Despliegue | ✅ | Vultr São Paulo, HTTPS, prueba post-despliegue con bot real | Copias de seguridad de la base (Etapa F) |

## Observaciones de economía (sin cambios, para revisar)

- El dinero entra al mundo por dos fuentes de sistema: fondos iniciales del personaje (`system:emision`) y clientes de los negocios (`system:poblacion`). Ambas están en el libro y suman cero, pero `system:poblacion` no tiene límite: es una fuente de dinero sin restricción de demanda. Cuando haya oferta/demanda real conviene acotarla. **No se cambió.**

## Orden de trabajo

1. Hecho en esta tanda (definido en la hoja de ruta): ubicación segura al entrar, reconexión automática, cuadrilla de 3 inicial, historial de cuadrilla visible, estado operativo del negocio, métricas en el servidor real.
2. Necesita decisión del equipo: sucesión automática (posponer vs. mantener), roles básicos de cuadrilla, empleados del negocio, contenido de los dos contratos nuevos, costos/requisitos para ampliar la cuadrilla a 4 y 5.
