# Lo que el jugador ve del mercado

Aprobado por el equipo el 2026-11-02 (todo menos la radio). Muestra la economía **real** de hoy: no cambia precios ni
reglas. Cuando la economía de mercado tome el control (etapa 3), estas mismas pantallas van a mostrar los precios nuevos
sin cambios.

| Qué | Dónde | Costo | Qué muestra |
|---|---|---|---|
| **Pizarras** | Caballete al lado del Almacén Moretti, el Taller Caruso, la Esquina del café, el Club de jazz y el kiosco | Gratis | Lo que vende cada local y a cuánto, «AGOTADO», ▲▼ contra el diario anterior, lo que compra («COMPRAMOS») y una nota (locales abiertos, titular del día) |
| **Libreta de precios** | Tecla **P**, hoja 1 | Gratis | Lo que viste en cada pizarra, con la hora. Más de 6 horas: en gris («viejo»). Avisa si algo se consigue más barato de lo que otro lo compra |
| **La Gaceta de Bellmont** | Kiosco de diarios (vereda oeste de la calle principal); se lee con **P**, hoja 2 | $0,10 por día | Fecha de 1952, titular, noticias de la ciudad, cotizaciones del Mercado, lo que pagan los locales y avisos clasificados. Sin comprarlo se ve solo el titular |
| **Nino, el que sabe** | Callejón del este del Mercado, detrás de los galpones; **P**, hoja 3 | $5 por una hora | Existencias del Almacén, cola de pedidos por mayor, la puerta de atrás del Taller (armas y precios), ánimo de las familias, mercadería de los otros locales y fletes en la calle. En vivo durante la hora |
| **Libro de cuentas** | En tu local («Ver el libro de cuentas») o **P**, hoja 4 | Solo dueños | Mercadería, caja, tu precio contra el de la zona, costo y margen; hoy: vendidas, ingresos, mercadería, alquiler, sueldos, ganancia y **clientes que se fueron sin comprar**; los últimos 12 ciclos |

## Cómo funciona

- **Pizarras:** el servidor de juego pide `GET /internal/market/boards` cada 20 s y se las manda a todos. Precio = el del
  mostrador con el ánimo de la familia (los descuentos personales se ven al hablar). Las armas no van en la pizarra.
- **Libreta:** el servidor ve quién está a menos de 6 m de una pizarra y llama a `POST /internal/market/look` (como mucho
  una vez por minuto por local). Se guarda el último precio visto (`price_notes`).
- **Diario:** una edición por día (hora de Argentina), escrita la primera vez que alguien la pide con lo que pasó en las
  últimas 24 horas (`MarketInfoRules.Write`, determinista) y guardada en `newspaper_editions`. Ese día se guardan también
  los precios (`market_prices`) para las flechas del día siguiente. Titular, en orden: familia en rivalidad, ola de
  asaltos (3 o más), faltante, suba, evento del día, calma.
- **Nino y el diario se pagan en el libro contable:** la plata va a las cuentas `npc:informante` y `npc:kiosco`.
- **Clientes perdidos:** cada ciclo del negocio anota cuántos quisieron comprar (`business_cycles.units_wanted`). Con el
  local sin mercadería se anota el ciclo con 0 ventas y sin plata de por medio (antes no quedaba registro).
- Todo lo de un personaje viene junto en `GET /internal/market/info/{personaje}`; las compras, en `POST /internal/market/buy`.

## Archivos

- `shared/Distrito.Shared/Economy/MarketInfo.cs`: datos, reglas, texto de la pizarra y el redactor del diario.
- `backend/Distrito.Backend/Economy/EconomyService.MarketInfo.cs` y la migración `0033_market_info.sql`.
- `server/Distrito.Server.Core/Npcs/NpcInteractionRules.cs`: kiosco, Nino y la opción del libro de cuentas.
- `game/scripts/Economy/MarketNetwork.cs` (pizarras, kiosco, libreta) y `MarketPanel.cs` (ventana de la tecla P).
- Pruebas: `MarketInfoRulesTests` y `MarketInfoApiTests`. Capturas: `pizarras`, `libreta-precios`, `diario`, `informante`, `libro-cuentas`.
