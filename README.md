# DISTRITO

MMORPG 3D persistente de sociedad, economía y organizaciones, ambientado en Estados Unidos en los años 30.

> Estado: **etapa 0 — base técnica**. Todavía no hay juego jugable. Ver [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Qué hay en el repositorio

| Carpeta | Contenido |
|---|---|
| `game/` | Proyecto Godot 4.7.2 .NET (cliente y servidor dedicado headless) |
| `shared/` | Código C# compartido (opciones de arranque, constantes de red) |
| `backend/` | API ASP.NET Core (.NET 10) — por ahora solo health checks |
| `database/` | SQL de PostgreSQL |
| `tests/` | Pruebas automáticas (xUnit) |
| `docs/` | Arquitectura, decisiones (ADR) y roadmap |

## Requisitos

- [Godot 4.7.2 **.NET**](https://godotengine.org/download) (la versión que dice *.NET*, no la normal)
- [.NET SDK 10](https://dotnet.microsoft.com/download) **y** .NET SDK 8
- Git + Git LFS (`git lfs install` una vez)
- Docker Desktop (opcional, para la base de datos local)

## Cómo ejecutar

### El juego
1. Abrí Godot 4.7.2 .NET → **Importar** → elegí `game/project.godot`.
2. Botón **Build** (martillo) y después **Play** (F5).

Modo servidor desde la terminal:
```bash
godot --headless --path game -- --server --port 7777
```

### Base de datos y backend
```bash
docker compose up -d                       # levanta PostgreSQL con el esquema inicial
dotnet run --project backend/Distrito.Backend
# Probar: http://localhost:5000/health/ready  (el puerto exacto lo muestra la consola)
```

### Pruebas
```bash
dotnet test Distrito.sln                                 # todas (requiere PostgreSQL)
dotnet test Distrito.sln --filter "Category!=Database"   # sin base de datos
```

## Verificación automática

Cada push ejecuta GitHub Actions (`.github/workflows/ci.yml`):
1. Compila toda la solución y corre las pruebas contra un PostgreSQL real.
2. Descarga Godot 4.7.2 .NET, importa el proyecto y lo arranca headless en modo servidor y cliente.

El resultado se ve en la pestaña **Actions** del repositorio.

## Documentación

- [Visión general](docs/Architecture/OVERVIEW.md)
- [Cliente / servidor](docs/Architecture/CLIENT_SERVER.md)
- [Networking](docs/Architecture/NETWORKING.md)
- [Base de datos](docs/Architecture/DATABASE.md)
- [Estructura del proyecto](docs/Architecture/PROJECT_STRUCTURE.md)
- [ADR-001 — Tecnología](docs/Decisions/ADR-001-TECHNOLOGY.md)
