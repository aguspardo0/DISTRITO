# Rendimiento (2026-11-09)

## Calidad gráfica (Opciones → «Calidad gráfica»)

| | Baja (notebooks) | Media (por defecto) | Alta (placa de video) |
|---|---|---|---|
| Sombras del sol | no | hasta 60 m | hasta 130 m, más definidas |
| Suavizado de bordes | FXAA | FXAA | MSAA 4× |
| Resolución interna | 75 % | 100 % | 100 % |
| Manzanas y barrios lejanos | hasta 200 m | hasta 360 m | siempre |
| Muebles de los locales | hasta 30 m | hasta 50 m | hasta 90 m |
| Luces (faroles, locales) | hasta 25 m | hasta 45 m | hasta 80 m |
| Lluvia (gotas) | 700 | 1.500 | 2.500 |
| Distancia máxima de la cámara | 320 m | 520 m | 900 m |

Se guarda con las demás opciones y se aplica a todo lo que ya está y a lo que aparece después (`GraphicsQuality`).

## Menos llamadas de dibujo

Las piezas simples se juntan en una sola malla con colores por vértice (`MeshMerge`): una persona pasa de unas 20 a 5
(el cuerpo y cada brazo y pierna, que se animan), un auto de unas 40 a 3 o 4 (faros, vidrios y balizas quedan aparte) y
cada mueble provisorio a una. Los modelos `.glb` no se tocan.

## Medición automática (`--benchmark`)

En cada CI se juega una partida local con lluvia, un músico con público y una discusión, y se recorren cuatro lugares
(la Calle Principal, adentro del diner, una esquina del Centro y Los Muelles) en las tres calidades. Queda una tabla en
el resumen de la CI. La máquina de la CI no tiene placa de video (dibuja con el procesador): los FPS sirven para
comparar, no como número final.

Medición del 2026-11-09 (CI, sin placa de video):

| Lugar | Calidad | FPS | Llamadas de dibujo (antes → después de juntar) |
|---|---|---|---|
| Calle Principal | Baja | 15,6 | 1.431 → 758 |
| Calle Principal | Media | 8,0 | 2.266 → 1.226 |
| Calle Principal | Alta | 7,5 | 4.726 → 2.288 |
| Adentro del diner | Media | 10,8 | 855 → 615 |
| Esquina del Centro | Media | 9,2 | 2.331 → 1.443 |
| Los Muelles | Media | 18,9 | 331 → 181 |

- Con placa de video, menos llamadas de dibujo es lo que más pesa: juntar las piezas las bajó a la mitad.
- Baja anda al doble que Media (sin sombras y con 75 % de resolución).
- Para la prueba cerrada falta medir en una PC real (con F3 se ven los FPS en pantalla). Lo que sigue si hace falta:
  menos triángulos en las personas, apagar las sombras de los objetos chicos y juntar los edificios de los barrios.
