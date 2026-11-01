# Inventario, objetos y logística

## Cómo está armado
- **Tipos de objeto** — tabla `item_definitions`: código, nombre, descripción, clase (`kind`), subcategoría, peso, volumen,
  precio de referencia, estado legal, si se apila y `properties` (JSON con lo propio de cada tipo, p. ej. qué munición usa un arma).
  Para agregar un objeto nuevo alcanza con una migración que inserte su fila; no hay que tocar el núcleo.
- **Clases y comportamientos** — `shared/Distrito.Shared/Economy/Inventory.cs` (`ItemKinds`): si se equipa, se gasta,
  tiene durabilidad o se puede pasar a otro.
- **Dónde está cada cosa** — `item_stacks`: dueño (cuenta económica) + ubicación + cantidad. Encima del personaje es la
  ubicación `encima`; negocios, depósitos de proveedores y bultos en la calle (`bulto:<id>`) son otras ubicaciones del
  mismo sistema. Almacenes y vehículos van a ser ubicaciones nuevas con capacidad y permisos, no inventarios aparte.
- **Movimientos** — `item_movements`: cada movimiento con transacción del libro (motivo y fecha), origen, destino,
  cantidad y quién lo hizo. Todo movimiento es atómico (una transacción de base), bloquea las cuentas involucradas y
  es idempotente: repetir un pedido con el mismo id no mueve nada dos veces.
- **Autoridad** — el cliente solo pide; el servidor de juego identifica al personaje por su sesión, valida distancias y
  reglas de zona, y el backend mueve los objetos.

## Capacidad
`config/inventario.json`:
```json
{ "personal": { "kg": null, "litros": null } }
```
`null` = sin límite (decisión del equipo por ahora). Con un número, comprar, levantar o recibir algo que no entra se rechaza.

## En el juego
- **I** abre el inventario: objetos agrupados por clase, peso y volumen total, detalle del objeto elegido.
- **Equipar / Guardar** para las armas.
- **Soltar en la calle**: queda un bulto delante del personaje; dura lo que diga la zona (`segundosBulto` en
  `config/zonas.json`) y lo levanta el primero que pase por encima si la zona permite recoger y saquear.
  El que lo soltó no lo vuelve a levantar sin querer durante 4 segundos.
- **Dar**: a alguien a menos de 3 m, la cantidad elegida.

## Depósito de la cuadrilla (etapa 3)
- Lo abre el jefe en **La Oficina** con plata de la caja común. Números en `config/inventario.json` → `depositoCuadrilla`
  (aprobados 2026-11-01: apertura $20, alquiler $1 por ciclo de negocios, 300 kg, sin límite de volumen).
- El alquiler se cobra solo en los ciclos en que hay algo guardado. Si la caja no alcanza queda como **deuda**: mientras
  haya deuda se puede guardar pero no retirar; al poner plata en la caja se cobra en el próximo ciclo.
- **Permisos**: el jefe siempre puede todo y es el único que administra. El segundo siempre ve y guarda; si retira lo
  decide el jefe. Para los miembros, el jefe decide si ven, guardan y retiran.
- Para guardar o retirar hay que estar en La Oficina (lo valida el servidor). Ver lo guardado y el historial se puede desde cualquier lado.
- Si la cuadrilla se disuelve, lo guardado pasa a manos del último que se va.
- En el juego: pestaña **Depósito** del inventario (con ● si estás en el lugar), **Historial** y **Permisos** (el jefe).

## Historial (etapa 4)
- Botón **Historial** en la pestaña Encima: los últimos 20 movimientos de lo que llevás (qué, cuánto, cuándo y el motivo del libro).
- En el depósito: quién guardó o retiró qué y cuándo.

## Pedidos a proveedores (etapa 5)
- En el mostrador del **Almacén Moretti** (E) → «Pedidos por mayor». Reglas en `SupplierRules` (`shared/.../Suppliers.cs`),
  aprobadas 2026-11-01: precio de mostrador, **10 % menos desde 20 unidades**, mínimo 5 y máximo 500 por pedido.
- Se paga al pedir; la mercadería sale del stock real del proveedor y queda apartada (`pedido:<id>`), no aparece en el inventario.
- Preparación: 1 ciclo cada 10 unidades; el proveedor prepara 50 unidades por ciclo entre todos los pedidos (cola).
- **Retiro** en el mostrador, gratis, cuando está listo. **Envío** al depósito de la cuadrilla: +10 % (mínimo $1), llega 1 ciclo
  después de listo. Si al llegar el depósito ya no existe o no entra, se devuelve el envío y queda para retirar.
- Se puede cancelar con reintegro completo mientras se prepara. Pedir y retirar se hacen en el mostrador (lo valida el servidor).
- Para sumar un proveedor o un producto: agregarlo a `SupplierRules.Catalog` (y su stock en la ubicación del proveedor).

## Vehículos (etapa 6)
- Se compran en el **Taller Caruso** (E → «Vehículos»): camioneta de reparto $120 (nivel 3, 400 kg, ~47 km/h) y camión de
  carga $350 (nivel 6, 1500 kg, ~32 km/h). Propios, o el jefe los compra para la cuadrilla con la caja (`shared/.../Vehicles.cs`).
- Los maneja el dueño o cualquiera de su cuadrilla: **G** para subir y bajar, W/S acelerar y frenar, A/D doblar, Espacio freno.
  El servidor simula el manejo. Decisión del equipo (2026-11-01): al que maneja se le puede disparar, y como ventaja él dispara
  por la ventanilla con el arma en la mano (2–4 y mouse). Piñas no (ni dar ni recibir). Los vehículos en marcha no frenan
  balas; los estacionados sirven de cobertura. Si el conductor queda herido, se baja y el vehículo queda ahí.
- La carga vive en el libro central (`vehiculo:<id>` de la cuenta dueña), con capacidad en kg. Se carga y descarga desde la
  pestaña **Vehículo** del inventario estando al lado o manejando.
- Al bajarse o si el conductor queda herido, el vehículo queda estacionado con la carga. Uno ajeno puede **forzar la caja**
  (5 segundos al lado) solo donde la zona permite robos (disputa, eventos, Los Muelles): la carga cae a la calle en un bulto.
- Persiste dónde quedó estacionado. Si una cuadrilla se disuelve, sus vehículos y la carga pasan al último miembro.

## Existencias visibles (etapa 7)
- El servidor de juego consulta `GET /internal/world/stock` (cada 5 s y enseguida después de guardar, retirar, cargar,
  pedir o forzar) y manda los datos a todos solo si cambiaron. Los clientes dibujan; no deciden nada.
- **Depósitos Valenti**: galpón techado en el borde oeste (al lado de La Oficina) con 6 bahías. Cada depósito de cuadrilla
  ocupa una bahía por orden de apertura, con cartel («nombre» y kg / capacidad) y su mercadería apilada. Guardar y retirar
  también se puede parado en el galpón, además de en la Oficina Valenti.
- **Almacén Moretti**: un estante por producto, con 3, 2 o 1 tablas llenas según el stock; los pedidos listos para retirar
  aparecen en pallets al lado, con cartel de listos y en preparación.
- **Vehículos**: caja abierta (baranda en la camioneta, estacas en el camión) con la carga real adentro.
- Bultos agrupados: un cajón o bolsa por unidad hasta 12 por tipo; la munición en cajas de 50 (`StockVisualRules`).
