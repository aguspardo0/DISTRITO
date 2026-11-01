# Panel de administración (pruebas)

- Tecla **F10** en el juego. Da plata (+$100 / +$1.000 / +$10.000), cualquier objeto, fija el nivel, lleva a lugares del mapa y cura.
- Solo lo pueden usar las cuentas (nombre de usuario) listadas en `config/admins.json`:
  ```json
  { "cuentas": ["tu-usuario", "el-de-tu-socio"] }
  ```
  o en el servidor con la variable de entorno `Admin__Accounts=tu-usuario,el-de-tu-socio`.
- El backend verifica la cuenta en cada pedido; lo que se da queda en el libro como `admin_grant` (emisión).
