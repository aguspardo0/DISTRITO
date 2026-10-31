# Estructura del repositorio

Monorepo. Una sola solución C# (`Distrito.sln`) agrupa todos los proyectos .NET, incluido el de Godot, para que `dotnet build` compile todo de una vez.

```
DISTRITO/
├── Distrito.sln
├── Directory.Build.props        # opciones comunes de compilación (nullable, warnings)
├── global.json                  # versión del .NET SDK
├── docker-compose.yml           # PostgreSQL local
├── game/                        # Proyecto Godot (cliente + host del servidor dedicado)
│   ├── project.godot
│   ├── Distrito.Game.csproj
│   ├── scenes/
│   └── scripts/
├── server/
│   └── Distrito.Server.Core/    # Lógica autoritativa en C# puro (sin Godot) — se crea en la etapa D
├── shared/
│   └── Distrito.Shared/         # Constantes de red, IDs, contratos de mensajes
├── backend/
│   └── Distrito.Backend/        # ASP.NET Core
├── database/
│   ├── init/                    # scripts que corre Postgres al crear el contenedor
│   └── migrations/              # SQL numerado
├── tests/
│   ├── Distrito.Shared.Tests/
│   └── Distrito.Backend.Tests/
├── docs/
└── .github/workflows/ci.yml     # compilación y pruebas automáticas
```

## Diferencias con la estructura sugerida en el prompt

- **`game/` en lugar de `client/`:** el proyecto Godot contiene el cliente y también el modo servidor dedicado (ver `CLIENT_SERVER.md`). Llamarlo `client/` sería engañoso.
- **`server/` sí existe**, pero contiene la lógica autoritativa en C# puro, que es lo que realmente importa proteger y probar.

## Dos archivos de solución

- `Distrito.sln` (raíz): todo el repositorio. Es la que usan la CI y los IDE.
- `game/Distrito.Game.sln`: solo juego + shared. Existe porque el editor de Godot busca una solución con el nombre del ensamblado dentro de la carpeta del proyecto para su botón **Build**.

## Versiones de .NET por proyecto

| Proyecto | Target | Motivo |
|---|---|---|
| `Distrito.Game` | `net8.0` | Godot 4.7 corre sobre .NET 8 |
| `Distrito.Server.Core` | `net8.0` | Lo carga Godot |
| `Distrito.Shared` | `net8.0` | Lo usan Godot y el backend |
| `Distrito.Backend` | `net10.0` | .NET 10 es LTS; .NET 8 pierde soporte en noviembre de 2026 |
| Tests | igual que el proyecto que prueban | |

## Modelos compartidos

No se duplican modelos entre proyectos. Si un tipo lo necesitan el juego y el backend (p. ej. un identificador de personaje o un mensaje), va en `Distrito.Shared`. Las entidades de base de datos **no** se comparten con el cliente.
