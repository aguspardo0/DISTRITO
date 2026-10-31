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
| Cupo | 8 miembros |
| Quién invita | Solo el jefe |
| Vencimiento de invitaciones | 10 minutos |
| Si el jefe se va | Hereda el miembro más antiguo |
| Cuadrilla vacía | Se disuelve |

La **sucesión real del liderazgo** (incluido el caso del fundador desconectado mucho tiempo y el sistema Legacy) está pendiente de diseño. La herencia al más antiguo es solo para que el sistema no quede en un estado inválido.

## Pendiente

Roles intermedios, permisos por rol, fondos de cuadrilla (usarán el mismo libro contable de ADR-003), relación con organizaciones/familias y subfacciones, NPCs como miembros.
