# Hito: Etapa A — Vertical slice (Hoja de ruta v0.2.1)

Fecha: 2026-10-31 · Estado: **completo en CI, falta la prueba manual con dos personas**

## 1. Qué se completó

| # | Objetivo | Estado |
|---|---|---|
| 1 | Dos jugadores juegan juntos | ✅ servidor autoritativo, predicción local, reconexión automática |
| 2 | Personaje y persistencia | ✅ ubicación segura, inventario, dinero, reputación y progreso en PostgreSQL |
| 3 | Negocio pequeño | ✅ puesto de café: caja, ingresos, alquiler, ayudante opcional, estado operativo, resultado |
| 4 | Tres contratos distintos | ✅ repuestos · pedido urgente con plazo y falla · cobranza en cuadrilla |
| 5 | Cuadrilla | ✅ cupo 3 (ampliable a 4 y 5), invitar/aceptar/rechazar/salir/expulsar, Jefe/Segundo/Miembro, historial |
| 6 | Familia y reputación persistente | ✅ Moretti, Caruso, Valenti; sube y baja; eventos auditables |
| 7 | Ciclo completo | ✅ probado con dos bots de la misma cuadrilla (cobranza → pago repartido → reputación → guardado) |

No completado: prueba manual del ciclo completo por dos personas.

## 2. Sistemas modificados en el hito

- `database/migrations/0008_roles_helper_contracts.sql`: roles, ayudante, contratos con plazo y 'failed', fondos de Moretti y Valenti.
- `shared/.../EconomyContracts.cs`: plantillas de contrato con pagador, plazo, reputación por falla, requisito de reputación y de cuadrilla.
- `backend/.../EconomyService*.cs`: aceptar con requisitos, vencimiento, entrega con reparto, ayudante en el ciclo del negocio.
- `backend/.../CrewService.cs`: roles, expulsar, ampliar cupo.
- `server/.../NpcInteractionRules.cs`: diálogos de los contratos nuevos y del ayudante.
- `game/scripts`: compañeros cercanos para la cobranza, cuenta regresiva en el HUD, panel de cuadrilla, ubicación segura, reconexión.

## 3. Build, pruebas y CI

Todos los jobs en verde en `main`: .NET (pruebas unitarias y contra PostgreSQL), Godot de punta a punta con bots,
escala (10 y 20 bots), exportación Windows y despliegue con prueba contra el servidor real.

## 4. Cómo probarlo

- PC: descargar `DISTRITO-windows.zip` de la release `prototipo`, abrir, crear cuenta y entrar (servidor ya configurado).
- Ciclo completo con dos personas: formar cuadrilla → contrato de repuestos (Almacén Moretti → Taller Caruso, deja +5 Caruso)
  → aceptar la cobranza en la Oficina Valenti → ir juntos al Taller Caruso y cobrar → salir y volver a entrar.
- Local: ver `docs/DEPLOY.md` y los scripts de `.github/scripts/`.

## 5. Evidencia

Anotaciones y resumen de la corrida de CI (pasos "Prueba de persistencia" y "Pruebas de escala").

## 6. Riesgos y deuda

- `system:poblacion` es una fuente de dinero sin límite (ver AUDIT).
- Los requisitos para ampliar la cuadrilla y los valores de los contratos son iniciales: ajustar con pruebas.
- La presencia para la cobranza usa el rango de interacción del NPC (3,5 m) sobre todos los conectados.

## 7. Próximo objetivo

Etapa B — estabilidad multijugador: pruebas con latencia y pérdida de paquetes, métricas de CPU, desconexiones y errores.
