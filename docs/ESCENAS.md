# Escenas de la ciudad y tránsito (2026-11-09)

Pedido del equipo: «más variedad de secuencias en el mapa, vida, más vida, eventos, secuencias». Respuesta: **todo**
(escenas de calle, eventos de ciudad, secuencias del crimen y vida más rica).

## Cómo funciona

- Las escenas **pasan solas** según la hora de Bellmont, el día de la semana y el lugar. Se editan sin tocar código en
  `config/escenas.json`: cada cuántos minutos se prueba suerte, con qué probabilidad, a qué horas, qué días, cuánto dura,
  en qué lugares puede pasar, qué dicen y cuántas iguales puede haber a la vez.
- El servidor decide qué empieza, dónde y con qué semilla, y avisa a todos con eso solo. Cada equipo arma la misma
  coreografía (`SceneScripts` en `Server.Core/World/CityScenes.cs`) y la dibuja si está a menos de 170 m (la lluvia, en
  toda la ciudad). No se mandan posiciones por la red.
- Son **ambientación**: no tocan la economía. Las que tienen tiros les avisan a los habitantes, que huyen, se esconden y
  llaman a la policía como con los tiros de verdad.
- Las personas usan el mismo modelo que todos (y el `.glb` de `game/modelos/personas/<papel>.glb` si existe: `canillita`,
  `obrero`, `maton`, `musico`, `borracho`, `luto`, `chico`, `nena`, `lustrabotas`, `comerciante`…). Lo que dicen aparece
  arriba de la cabeza.

## Las escenas

| Tipo | Escena | Cuándo | Qué pasa |
|---|---|---|---|
| Calle | **Canillita** | 6 a 12 h | Va y viene por la vereda y grita los titulares de La Gaceta. |
| Calle | **Camión que descarga** | 7 a 16 h | Llega un camión a la puerta de un local, dos changarines bajan cajones y se van. |
| Calle | **Discusión** | 10 a 21 h | Dos vecinos se pelean a los gritos; se juntan curiosos; uno se va enojado. |
| Calle | **Borracho** | 21 a 4 h | Sale tambaleando del diner, del club o del hotel y canta. |
| Calle | **Lustrabotas** | 8 a 17 h | En la esquina; llegan clientes, esperan y pagan. |
| Calle | **Chicos jugando** | 14 a 19 h | Corren detrás de una pelota en La Esquina. |
| Calle | **Músico callejero** | 16 a 23 h | Toca el acordeón (generado, sin archivos) y la gente se para a escuchar y aplaude. |
| Calle | **Paseo del domingo** | domingos | Una familia camina despacio por la vereda. |
| Evento | **Lluvia** | cualquier hora | Toda la ciudad: lluvia, cielo gris, más niebla, menos sol, sonido de lluvia y truenos. |
| Evento | **Feria** | sábados | Puestos en la calzada de la Calle Principal, vendedores que pregonan y gente; los autos se estacionan. |
| Evento | **Apagón** | de noche | Se apagan los faroles de una zona por unos minutos (con el chispazo). |
| Evento | **Barco** | de día | Un carguero entra al puerto, toca la bocina, descargan y se va. |
| Evento | **Redada** | 9 a 22 h | Dos patrulleros con sirena y balizas, cuatro agentes en la puerta de un local, el comerciante protesta. |
| Evento | **Cortejo fúnebre** | 9 a 16 h | El auto negro a paso de hombre por la calle, la gente de luto atrás, la campana. |
| Crimen | **Persecución** | cualquier hora | Un auto a toda velocidad por la Calle Principal y la Avenida del Puerto, el patrullero atrás con sirena. |
| Crimen | **Tiroteo entre familias** | 20 a 3 h | Dos autos cruzados, tres de cada lado se tirotean; uno cae, los demás escapan; llega la policía. |
| Crimen | **Extorsión** | 10 a 20 h | Dos matones le «piden colaboración» a un comerciante; él les da un sobre. |
| Crimen | **Contrabando** | 22 a 4 h | Una lancha en el muelle, cajones a una camioneta, uno vigila. |

Sonidos nuevos, todos generados: trueno, chispazo, acordeón, sirena de patrullero, bocina de barco, campana, chirrido de
gomas y lluvia.

## Vida más rica

- **Tránsito:** 16 autos circulan solos (`Server.Core/World/Traffic.cs`): la mitad este de la Calle Principal y la Avenida
  del Puerto, un anillo grande por el Centro y otro al este. Van por su mano y frenan a tiempo si tienen a alguien
  adelante (una persona o el auto de adelante); los que cruzan no los frenan. Los simula el servidor y tienen colisión.
  La mitad oeste de la Calle Principal (los locales de las familias, donde aparece la gente) quedó peatonal. Durante la
  feria esos autos se estacionan contra el cordón.
- **Los vecinos entran a los locales:** van caminando por la puerta (la abren al pasar y se cierra sola), se paran
  frente al mostrador y se los ve por la vidriera. Los empleados de cada local trabajan adentro. En `config/vida.json`,
  `"seVe": true` marca los locales que se ven por dentro (siguen siendo refugio si hay tiros).
- Los NPC fijos (los del mostrador y los de la calle) ahora tienen cuerpo de persona en lugar de una cápsula y miran
  hacia donde llega la gente.

## Pendiente

- Que los jugadores puedan meterse en las escenas del crimen (frenar la extorsión, sumarse al tiroteo, perseguir).
- Botón en el panel de administración para lanzar una escena a pedido.
- Horarios de los negocios (abren y cierran): queda para cuando se decida, porque cerrarlos de noche cambia el juego.
