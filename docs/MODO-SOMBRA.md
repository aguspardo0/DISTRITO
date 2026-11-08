# Modo sombra de la economía de mercado (etapa 2)

**Qué es:** el mercado nuevo (`shared/Distrito.Shared/Market/`) corre dentro del backend real, al lado de la economía
del juego, y anota lo que *habría* pasado. **No decide nada:** no cobra, no paga y no mueve mercadería. Los jugadores no
ven ninguna diferencia. Sirve para ver con números reales si el mercado nuevo es justo y divertido antes de activarlo;
las reglas aprobadas no cambian hasta que el equipo lo apruebe.

## Cómo funciona

1. Después de cada ciclo económico real (5 minutos), `MarketShadowService` lee los locales reales de los productos que
   tiene el mercado (hoy, el café): precio, mercadería que tenían al empezar el ciclo, capacidad, calidad, si tienen
   empleado, y cuánto vendieron de verdad.
2. Se los pasa a `MarketShadow`, que avanza el mercado simulado un ciclo: la gente de las 5 zonas
   (`config/mercado.json`) elige local por precio, calidad y distancia, con la plata que tiene.
3. Anota en `distrito.market_shadow_cycles` lo real y lo simulado de cada local, la plata y el precio de referencia de
   cada zona (se guardan los últimos 7 días). Cada 288 ciclos (un día) escribe un resumen en
   `distrito.market_shadow_days`.
4. Si el backend se reinicia, retoma la plata y los precios de cada zona del último ciclo anotado.

La cadena de abastecimiento (tostadero, Almacén y camión) es la simulada de la etapa 1, porque en el juego todavía no
existe; la mercadería de cada local se toma de la real en cada ciclo. Lo que pase en el mercado simulado nunca vuelve al
juego.

## Los avisos

| Aviso | Qué quiere decir |
|---|---|
| «X vendió R y con el mercado nuevo habría vendido S (±P %)» | En el día, lo real y lo simulado se separan más de un 50 % (con al menos 20 unidades). Hay que mirar si el mercado nuevo cambiaría mucho cuánto gana ese local. |
| «zona: el precio de referencia llegó al piso / al techo» | El precio que la gente considera normal tocó el límite (50 % o 200 % del precio base): falta o sobra mucha mercadería. |
| «zona: faltó mercadería para el N %» | Más del 30 % de lo que la gente quiso comprar no se pudo atender. |
| «zona: la gente se quedó casi sin plata» | La zona tiene menos del 5 % de un día de ingreso: la demanda se va a secar. |
| «ERROR: la plata del mercado simulado no cierra» | Es un error del simulador (no del juego): la plata simulada apareció o desapareció. Avisar. |

Los avisos también quedan en el log del backend (`journalctl -u distrito-backend`), con «Modo sombra».

## Cómo ver el informe

En el servidor:

```bash
curl -s -H "X-Distrito-Server-Key: $DISTRITO_SERVER_KEY" "http://127.0.0.1:5080/internal/market/shadow?days=7"
```

Devuelve un resumen en palabras (`summary`), los últimos días (`days`, con cada local y cada zona) y el último ciclo
(`last`). La clave está en el archivo de entorno del backend; el endpoint no es público (Caddy no expone `/internal`).

## Apagarlo

`Market:Shadow = false` en la configuración del backend. Un error del modo sombra nunca frena la economía real: corre
aparte en el mismo ciclo y, si falla, solo queda el error en el log.
