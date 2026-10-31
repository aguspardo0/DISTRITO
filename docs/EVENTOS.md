# Cómo editar los eventos de distrito

Los eventos están en `config/eventos.json`. Para cambiarlos o agregar uno:

1. Abrí el archivo en GitHub (https://github.com/aguspardo0/DISTRITO/blob/main/config/eventos.json) y tocá el lápiz.
2. Copiá un bloque `{ ... }` de un evento existente, pegalo después de una coma y cambiá los valores.
3. Guardá con "Commit changes". Las pruebas revisan el archivo; si hay un error, la corrida falla, dice qué está mal y el servidor no se toca.
   Si está bien, el servidor se actualiza solo en unos minutos.

| Campo | Qué es | Ejemplo |
|---|---|---|
| codigo | identificador único, sin espacios | `feria` |
| nombre | lo que ven los jugadores | `Feria en La Esquina` |
| descripcion | opcional | `Puestos y música.` |
| zona | `calle-almacen`, `los-talleres`, `la-oficina` o `la-esquina` | `la-esquina` |
| dias | `lunes` … `domingo` (sin tildes) o `todos` | `["lunes", "viernes"]` |
| inicio | hora de Argentina, HH:MM | `21:00` |
| minutos | duración (5 a 1440) | `30` |
| puntosPorContrato | influencia por contrato entregado en la zona durante el evento | `10` |
| topePorCuadrilla | máximo por cuadrilla en cada evento (aparte del tope diario de 30) | `20` |
