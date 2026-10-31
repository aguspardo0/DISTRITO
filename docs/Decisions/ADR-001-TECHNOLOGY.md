# ADR-001 — Tecnología base

- **Estado:** aceptada
- **Fecha:** 2026-10-31

## Contexto

DISTRITO es un MMORPG 3D persistente desarrollado por dos personas. El stack fue definido por el equipo en el prompt maestro y no se rediscute salvo impedimento técnico demostrado. Este ADR fija **versiones** y registra las decisiones derivadas.

## Decisión

| Elemento | Elección | Versión fijada |
|---|---|---|
| Motor | Godot .NET | **4.7.2-stable** (última estable a la fecha) |
| Renderizador | Forward+ | — |
| Lenguaje | C# | El que trae cada SDK |
| Runtime del juego | .NET | **8** (`net8.0`, requerido por Godot 4.7) |
| Backend | ASP.NET Core | **.NET 10** (`net10.0`, LTS) |
| SDK de compilación | .NET SDK | **10.0.x** (compila `net8.0` y `net10.0`) |
| Base de datos | PostgreSQL | **16** |
| Acceso a datos | Npgsql | 10.0.3 |
| Tests | xUnit + Microsoft.NET.Test.Sdk | 2.9.3 / 18.0.1 |

Todas las versiones de paquetes NuGet se fijan en un solo lugar: `Directory.Packages.props` (Central Package Management). El proyecto Godot queda afuera de ese mecanismo porque su SDK agrega sus propias referencias.
| Networking | ENet vía MultiplayerAPI | en evaluación |
| Control de versiones | Git + Git LFS | — |
| CI | GitHub Actions | — |

## Motivos

- **Godot 4.7.2 + .NET 8:** Godot 4.7 compila los proyectos C# contra `net8.0`. Usar otra versión para el código que carga Godot falla en tiempo de ejecución.
- **Backend en .NET 10:** el backend no lo carga Godot, así que puede usar el LTS actual. .NET 8 termina su soporte el 10 de noviembre de 2026.
- **Shared en net8.0:** un proyecto `net10.0` puede referenciar uno `net8.0`, pero no al revés. Por eso lo compartido apunta a `net8.0`.
- **GitHub Actions como verificación:** el desarrollo se hace en un entorno en la nube sin .NET ni Godot instalables; la CI compila y prueba cada cambio y el equipo ve el resultado en GitHub.

## Requisitos para cada desarrollador

- Godot **4.7.2 .NET** (la versión que dice *.NET* o *mono*).
- .NET SDK **10** y .NET SDK **8** (o al menos el runtime 8).
- Git y Git LFS.
- Docker Desktop (opcional, para PostgreSQL local).

## Consecuencias

- Hay dos versiones de .NET en el repo. Está controlado por `TargetFramework` por proyecto.
- Cuando Godot pase a una versión nueva de .NET, se actualizan juntos `game/`, `server/`, `shared/`.

## Decisiones pendientes

| Tema | Alternativas | Cuándo decidir |
|---|---|---|
| Host del servidor de juego | Godot headless (actual) vs. servidor .NET puro | Tras pruebas de 20 jugadores |
| ~~Aplicación de migraciones~~ | **Resuelto en ADR-002**: runner propio | — |
| ~~Acceso a datos~~ | **Resuelto en ADR-002**: Npgsql + SQL | — |
| Formato de mensajes de red | RPC de Godot / serialización manual binaria | Con las pruebas de 10 jugadores |
| ~~Autenticación~~ | **Resuelto en ADR-002**: token opaco | — |
| Reglas de Legacy (sucesión de personaje) | Abierto — no definir hasta que el diseño lo cierre | — |
