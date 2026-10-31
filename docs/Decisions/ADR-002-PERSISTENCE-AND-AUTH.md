# ADR-002 — Persistencia, migraciones y autenticación

- **Estado:** aceptada
- **Fecha:** 2026-10-31
- **Resuelve:** decisiones pendientes de ADR-001 (migraciones, acceso a datos, autenticación)

## Decisiones

### 1. Acceso a datos: Npgsql con SQL explícito
Sin ORM por ahora. Cada módulo tiene su "store" con consultas SQL escritas a mano.
- **Por qué:** las operaciones económicas futuras necesitan control fino de transacciones, bloqueos (`SELECT … FOR UPDATE`) e idempotencia. Con SQL explícito eso es visible y revisable.
- **Costo:** más código repetitivo que con EF Core. Se revisa si el repetitivo se vuelve un problema real.

### 2. Migraciones: runner propio mínimo
`MigrationRunner` en el backend aplica `database/migrations/NNNN_*.sql` al arrancar, en orden, una transacción por archivo, con candado consultivo.
- **Por qué:** ~80 líneas sin dependencias; SQL puro que cualquiera puede leer. DbUp hace lo mismo con una dependencia extra; EF Migrations obliga a usar EF.

### 3. Contraseñas: PBKDF2-HMAC-SHA256
600.000 iteraciones (recomendación de OWASP), sal aleatoria de 16 bytes, formato autodescriptivo `pbkdf2-sha256$iteraciones$sal$hash`, comparación en tiempo constante. Usa solo la biblioteca estándar de .NET.

### 4. Sesiones: token opaco, guardado como hash
Al iniciar sesión, el backend genera 32 bytes aleatorios. El cliente recibe el token; la base guarda solo su SHA-256 y la expiración (24 h).
- **Por qué no JWT:** un token opaco se puede revocar borrando la fila, y no hay claves de firma que administrar. El costo (una consulta por validación) es irrelevante a esta escala.

### 5. Servidor de juego ↔ backend: API interna con clave compartida
El servidor de juego nunca toca la base directamente. Llama a `/internal/*` con el encabezado `X-Distrito-Server-Key`.
- `POST /internal/sessions/resolve`: token + personaje → datos del personaje si el token es válido y el personaje es de esa cuenta.
- `PUT /internal/characters/{id}/state`: guarda posición.
- Si la clave no está configurada, la API interna responde 503 (apagada por defecto).
- La clave llega al servidor de juego por variable de entorno (`DISTRITO_SERVER_KEY`), no por argumentos, para que no aparezca en la lista de procesos.

### 6. Modo invitado
Un servidor de juego sin `--backend` funciona como antes: cualquiera entra como invitado y nada se guarda. Sirve para pruebas en LAN / Radmin. El modo anfitrión (crear partida desde el menú) es siempre invitado, porque requeriría que el anfitrión tenga la clave del servidor.

## Valores provisionales (no son diseño de juego)

| Valor | Provisional | Motivo |
|---|---|---|
| Personajes por cuenta | máx. 3 | Límite técnico anti-abuso. El diseño real está pendiente. |
| Nombre de personaje | 3–24 letras y espacios | Nombres "de época" sin números. A revisar con el diseño. |
| Usuario | 3–20, letras/números/_ | |
| Contraseña | 8–128 caracteres | |
| Guardado de posición | al desconectar y cada 15 s | |
