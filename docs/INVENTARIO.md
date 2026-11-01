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
