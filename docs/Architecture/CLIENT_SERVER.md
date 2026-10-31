# Cliente y servidor

## Modelo: cliente-servidor autoritativo

El cliente **nunca** decide resultados económicos, recompensas, propiedad de mercancías ni resultados de contratos. Envía intenciones; el servidor valida y responde con el estado resultante.

## Un proyecto Godot, dos modos de ejecución

El proyecto Godot (`game/`) se ejecuta en uno de dos modos:

| Modo | Cómo se lanza | Qué carga |
|---|---|---|
| Cliente | `godot` normal (o el ejecutable exportado) | Escena 3D, cámara, input, UI |
| Servidor dedicado | `godot --headless -- --server` | Mundo (colisiones) sin render ni audio |

**Por qué un solo proyecto y no dos:** el servidor necesita la *misma geometría* del barrio que el cliente para validar movimiento y colisiones. Con dos proyectos Godot habría que duplicar o sincronizar escenas. Godot permite exportar un preset de *servidor dedicado* que elimina recursos visuales, así que el binario final del servidor sigue siendo liviano.

**Lo que sí se separa:** la *lógica autoritativa* vive en `server/Distrito.Server.Core` (C# puro). El modo servidor de Godot es solo un **host**: recibe paquetes, llama a `Server.Core`, y envía resultados. Esto mantiene la regla "debe poder ejecutarse sin interfaz gráfica" y permite reemplazar el host en el futuro si Godot headless no escala (ver decisión pendiente en ADR-001).

## Responsabilidades

### Cliente
- Render 3D (Forward+), cámara en tercera persona, controles, animaciones, UI, audio.
- Predicción local de movimiento del propio personaje (para que no se sienta lento) **corregida por el servidor**.
- Interpolación de las demás entidades.

### Servidor de juego
- Acepta conexiones y mantiene sesiones.
- Recibe input de movimiento, lo valida (velocidad máxima, colisiones) y es dueño de la posición final.
- Decide qué entidades ve cada jugador (interés de red).
- Será el punto de entrada de interacciones (NPC, contratos) que luego se confirman en el backend.

### Backend
- Login y cuentas; emite un token que el servidor de juego verifica al conectarse.
- Persistencia de personajes y de todo lo económico, con transacciones y registro auditable.

## Flujo de conexión objetivo (hito 1)

1. Cliente → Backend: login → recibe token de sesión.
2. Cliente → Servidor de juego: conecta por ENet y envía token.
3. Servidor de juego → Backend: valida token, carga personaje.
4. Servidor crea la entidad del jugador y empieza a replicar entidades cercanas.

En el **prototipo técnico (etapa D)** se omiten los pasos 1 y 3: se conecta directo al servidor sin cuenta. Está marcado como deuda explícita, no como diseño final.
