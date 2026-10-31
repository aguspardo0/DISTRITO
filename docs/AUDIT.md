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
| Reconexión | ✅ | Reconexión automática (10 intentos); CI reinicia el servidor y el cliente vuelve solo | — |
| Movimiento autoritativo + validación | ✅ | `MovementRules` + tests; servidor mueve, cliente solo envía intención | Pruebas con pérdida de paquetes (Etapa B) |
| Predicción local | ✅ | `LocalPrediction`; error final 0–0,17 m en todas las pruebas | Prueba con latencia simulada (Etapa B) |
| Sincronización + interés de red | ✅ | `InterestManager`; prueba de culling (radio 12 m) OK | Mapa/presencia separada (Etapa C) |
| Persistencia: personaje, posición | ✅ | Ronda 1/2 en CI; ubicación segura (fuera del mundo u ocupada → punto de aparición); altura vigilada | — |
| Persistencia: inventario, dinero, reputación, negocio | ✅ | Todo en PostgreSQL, transaccional, probado | — |
| Cuentas y sesiones | ✅ | PBKDF2, tokens opacos, tests | — |
| NPC / familias | 🟡 | 4 NPC (Moretti, Caruso, Valenti, esquina), diálogos validados en servidor | Familias como instituciones con estados (diseño v0.1) |
| Reputación | ✅ | 0–1000, eventos auditables, persiste; sube y baja con 3 contratos (también por vencimiento) | — |
| Contratos | ✅ | 3 contratos: entrega de repuestos; pedido urgente con plazo y falla (−3 Moretti); cobranza en cuadrilla con reparto del pago. Backend + reglas + bots de punta a punta para los 3 | — |
| Economía (libro contable) | ✅ | Doble entrada, idempotencia, concurrencia, conservación probada | — |
| Negocio | ✅ | Puesto de café: licencia, stock, ventas por ciclo offline, alquiler, ayudante opcional ($1/ciclo, vende hasta 3 si hay caja), estado operativo, caja, retiro | — |
| Cuadrillas | ✅ | Capacidad 3 (niveles 4 y 5 preparados), roles Jefe/Segundo/Miembro, expulsar, historial visible ; ampliación a 4 (5 contratos + 30 de reputación entre todos) y a 5 (15 + 100), la decide el jefe | — |
| Liderazgo / sucesión | ✅ | Jefe y Segundo; herencia al irse; sucesión por inactividad de 14 días (el equipo decidió mantenerla) | — |
| Ciclo completo (hito 7) | ✅ | Entrar → contrato → cuadrilla → cobranza en grupo → cobrar (repartido) → negocio paga costos → reputación → guardar → volver. CI: dos bots de la misma cuadrilla hacen la cobranza y la base confirma la reputación de ambos | Prueba manual de dos personas |
| Métricas | ✅ | `ServerMetrics` en CI y en el servidor real (cada 60 s) | — |
| Bots de carga | ✅ | 10 y 20 bots, interés de red, métricas por corrida | — |
| CI | ✅ | 5 jobs; anotaciones legibles | — |
| Despliegue | ✅ | Vultr São Paulo, HTTPS, prueba post-despliegue con bot real | Copias de seguridad de la base (Etapa F) |

## Observaciones de economía (sin cambios, para revisar)

- El dinero entra al mundo por dos fuentes de sistema: fondos iniciales del personaje (`system:emision`) y clientes de los negocios (`system:poblacion`). Ambas están en el libro y suman cero, pero `system:poblacion` no tiene límite: es una fuente de dinero sin restricción de demanda. Cuando haya oferta/demanda real conviene acotarla. **No se cambió.**

## Orden de trabajo

1. Hecho en esta tanda (definido en la hoja de ruta): ubicación segura al entrar, reconexión automática, cuadrilla de 3 inicial, historial de cuadrilla visible, estado operativo del negocio, métricas en el servidor real.
2. Necesita decisión del equipo: sucesión automática (posponer vs. mantener), roles básicos de cuadrilla, empleados del negocio, contenido de los dos contratos nuevos, costos/requisitos para ampliar la cuadrilla a 4 y 5.

## Avance (2026-10-31, después de la auditoría)

- Hecho y en CI verde: ubicación segura (incluye la corrección de personajes que se elevaban al aparecer encimados), reconexión automática,
  cuadrilla de 3 con roles Jefe/Segundo/Miembro y expulsión, ayudante del puesto, contratos 2 y 3, métricas en producción.
- Pruebas de punta a punta agregadas: pedido urgente y cobranza en cuadrilla (dos bots).
- Pendiente: prueba manual con dos personas.
- Ampliación de cuadrilla decidida por el equipo: por reputación y contratos cumplidos (sin dinero).
