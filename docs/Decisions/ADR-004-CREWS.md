# ADR-004 — Cuadrillas (versión mínima del hito 1)

- **Estado:** aceptada (provisional en reglas de juego)
- **Fecha:** 2026-10-31

## Decisión

- Una cuadrilla es una **estructura organizacional** (no geográfica: las sedes y sucursales son otra cosa, ver prompt maestro §5).
- Tablas: `crews`, `crew_members` (un personaje pertenece como máximo a una cuadrilla), `crew_invitations` (vencen).
- El cliente nunca manda ids de personaje: el servidor de juego los toma de la sesión autenticada.
- Cada acción es una transacción que bloquea la fila de la cuadrilla; el cupo se respeta aunque varios acepten a la vez (probado).
- El nombre de la cuadrilla se replica bajo el nombre del personaje.

## Reglas provisionales (no son diseño de juego definitivo)

| Regla | Provisional |
|---|---|
| Formación | **Decidido (equipo):** progresiva. Se funda solo y queda "en formación"; con 3 integrantes queda formada |
| Cupo | **Decidido:** por nivel; nivel 1 = 5 (documento de diseño). Niveles superiores pendientes |
| Quién invita | Solo el jefe (provisional) |
| Vencimiento de invitaciones | 10 minutos (provisional) |
| Si el jefe se va | Hereda el miembro más antiguo (provisional) |
| Jefe inactivo | **Decidido (equipo):** a los 14 días sin entrar, el mando pasa al miembro activo más antiguo; se registra en `crew_events` |
| Cuadrilla vacía | Se disuelve |

Pendiente: votación interna (el documento la menciona; el equipo eligió solo inactividad por ahora), requisito de nivel 5 para fundar (cuando exista la progresión de personaje), sistema Legacy.

## Pendiente

Roles intermedios, permisos por rol, fondos de cuadrilla (usarán el mismo libro contable de ADR-003), relación con organizaciones/familias y subfacciones, NPCs como miembros.
