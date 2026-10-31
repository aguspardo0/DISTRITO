# DISTRITO — Visión general de la arquitectura

> Estado: borrador inicial (etapa B). Este documento describe la arquitectura **objetivo** y qué parte existe hoy.
> Cada decisión importante tiene su ADR en `docs/Decisions/`.

## 1. Principio rector

DISTRITO es un **mundo persistente**, no un juego al que se le agrega multijugador. Por eso:

- El **servidor es la única autoridad** sobre el estado del mundo (posición válida, dinero, mercancías, contratos).
- El **cliente solo presenta** el mundo y envía *intenciones* ("quiero moverme hacia acá", "quiero aceptar este contrato").
- La **lógica de reglas** (economía, contratos, inventarios, validación de movimiento) vive en **bibliotecas C# puras, sin dependencia de Godot**, para poder probarlas con tests unitarios y reutilizarlas en cualquier host.

## 2. Componentes

```
┌──────────────────────┐        ENet (UDP)        ┌──────────────────────────────┐
│  Cliente Godot (PC)  │ ◄──────────────────────► │  Servidor de juego           │
│  render, input, UI   │   intenciones / estado   │  Godot headless + C#         │
│  audio, cámara       │                          │  sesiones, interés de red,   │
└──────────┬───────────┘                          │  validación, simulación      │
           │ HTTPS (login, cuenta)                └──────────────┬───────────────┘
           ▼                                                     │ HTTP interno / directo
┌──────────────────────┐                                         ▼
│  Backend ASP.NET Core│ ◄────────────── persistencia ──────────►┌──────────────┐
│  auth, cuentas,      │                                         │  PostgreSQL  │
│  personajes, ledger  │ ───────────────────────────────────────►│              │
└──────────────────────┘                                         └──────────────┘
```

| Componente | Responsabilidad | Tecnología | Existe hoy |
|---|---|---|---|
| Cliente | Render 3D, cámara, controles, animación, UI, audio | Godot 4.7 .NET, C# | Esqueleto |
| Servidor de juego | Conexiones, sesiones, movimiento validado, sincronización, interés de red | Godot 4.7 headless + `Distrito.Server.Core` | Esqueleto |
| Simulación | Economía, NPC, organizaciones, contratos… (módulos independientes) | C# puro | No (etapas futuras) |
| Backend | Autenticación, cuentas, persistencia, historial, servicios globales | ASP.NET Core (.NET 10) | Health check |
| Base de datos | Estado persistente y registro económico auditable | PostgreSQL 16 | Esquema vacío |
| Shared | Contratos de red, identificadores, constantes | C# (net8.0) | Mínimo |

## 3. Reglas de dependencia

```
game (Godot)  ──►  Distrito.Server.Core  ──►  Distrito.Shared
backend       ──►  Distrito.Shared
```

- `Distrito.Shared` no depende de nada (ni Godot ni ASP.NET).
- `Distrito.Server.Core` no depende de Godot. El host Godot lo *usa*; nunca al revés.
- El backend **no** referencia al proyecto Godot.
- Prohibidas las dependencias circulares.

## 4. Simulación por niveles (diseño, no implementado)

No todo se procesa cada frame:

| Nivel | Ejemplo | Frecuencia |
|---|---|---|
| Detallado | NPC cerca de un jugador | cada tick del servidor |
| Intermedio | NPC trabajando en una operación activa | cada pocos segundos |
| Eventos / intervalos | NPC lejanos | por evento o cada minutos |
| Programado | Empresas / organizaciones sin jugadores | lotes programados (backend) |

## 5. Ciudades

La primera versión contiene **un solo barrio**. Para no bloquear varias ciudades más adelante:

- Todo identificador importante es global (UUID), no relativo a una ciudad.
- Las entidades persistentes llevan `city_id` / ubicación explícita.
- El servidor de juego representa **una zona**; los servicios globales (identidad, mercado, organizaciones) viven en el backend.

## 6. Qué NO se hace todavía

Economía, NPC con IA, organizaciones, contratos, política, transporte. Se diseñan cuando lleguen sus etapas (ver `docs/ROADMAP.md`).
