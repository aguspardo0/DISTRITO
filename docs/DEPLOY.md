# Despliegue

Servidor de pruebas actual: **Vultr**, Cloud Compute Shared CPU `vc2-1c-2gb` (1 vCPU, 2 GB), São Paulo, Ubuntu 24.04 x64. Usuario SSH: `root`.

También soportado: Oracle Cloud Always Free (Ubuntu ARM64, usuario `ubuntu`). La CI detecta la arquitectura por SSH y arma el release que corresponde.

## Cómo funciona

Cada push a `main` con CI en verde ejecuta el job `deploy`:
1. Publica el backend (`linux-arm64`, self-contained) y exporta el servidor dedicado de Godot (preset *Linux Server ARM64*).
2. Lo sube por SSH y ejecuta `deploy/setup-server.sh` (idempotente).
3. Prueba el servidor real: `GET https://<dominio>/health/ready` y un bot que inicia sesión, entra al barrio y se mueve.

| Servicio | Dónde | Expuesto |
|---|---|---|
| PostgreSQL 16 | 127.0.0.1:5432 | No |
| Backend | 127.0.0.1:5080 | Vía Caddy en `https://<ip-con-guiones>.sslip.io`, **sin** `/internal/*` |
| Servidor de juego | UDP 7777 | Sí |

Secretos del servidor (contraseña de la base, clave interna) se generan la primera vez en `/etc/distrito/distrito.env` y no salen de la máquina.

## Configuración en GitHub

Secretos del repositorio (*Settings → Secrets and variables → Actions*):
- `DEPLOY_HOST` y `DEPLOY_USER`: opcionales como secretos; si no están, se leen de `deploy/target.env` (no son datos secretos).
- `DEPLOY_SSH_KEY`: llave privada SSH.
- `DEPLOY_USER`: usuario SSH (`root` en Vultr; si falta, `ubuntu`).

Sin esos secretos el job arma el release pero no despliega.

## Vultr: firewall

La imagen de Ubuntu trae `ufw` activo; `setup-server.sh` abre 80/tcp, 443/tcp y 7777/udp. Si se usa un *Firewall Group* de Vultr, hay que abrir los mismos puertos ahí.

## Oracle: reglas de red necesarias

Security List de la subnet, ingreso desde `0.0.0.0/0`: UDP 7777, TCP 80, TCP 443.

## Operación

```bash
ssh ubuntu@<ip>
sudo systemctl status distrito-backend distrito-gameserver caddy
sudo journalctl -u distrito-gameserver -f
ls /opt/distrito/releases      # se conservan los últimos 3; /opt/distrito/current apunta al activo
```

Volver a un release anterior: `sudo ln -sfn /opt/distrito/releases/<commit> /opt/distrito/current && sudo systemctl restart distrito-backend distrito-gameserver`.
Ojo: si el release nuevo aplicó migraciones, volver atrás el código no alcanza; restaurá también la copia «predespliegue» de ese despliegue (abajo).

## Copias de seguridad de la base

- **Programadas:** cada 6 horas (00:15, 06:15, 12:15, 18:15 UTC, con hasta 5 min de variación), timer `distrito-backup.timer`. Se guardan las últimas **28** (una semana).
- **Antes de cada despliegue:** `setup-server.sh` hace una copia «predespliegue» antes de instalar el release (el backend aplica migraciones al arrancar). Se guardan las últimas **5**.
- **Manuales:** `sudo distrito-backup manual` (se guardan las últimas 10). `distrito-restore` hace una automáticamente antes de pisar la base.
- Formato: `pg_dump -Fc` de la base `distrito`, en `/var/backups/distrito/distrito-<fecha UTC>-<tipo>.dump` (carpeta 700, archivos 600, dueño root). Cada copia se verifica leyéndola entera con `pg_restore --list`; si queda menos de 300 MB libres no se hace y falla con error.
- La CI, después de desplegar, muestra el estado del timer y las últimas copias como aviso, y falla si el timer no está activo.

```bash
sudo systemctl list-timers distrito-backup.timer     # próxima ejecución
sudo journalctl -u distrito-backup -n 20            # resultado de las últimas
sudo ls -lt /var/backups/distrito
sudo distrito-backup manual                         # copia ahora
sudo distrito-restore --ultima                      # restaurar la más reciente
sudo distrito-restore /var/backups/distrito/<archivo>.dump
```

`distrito-restore` hace una copia manual del estado actual, para el juego y el backend, recrea la base desde la copia, levanta el backend (espera `/health/ready`) y después el servidor de juego. Los jugadores conectados se desconectan durante el proceso (segundos con el tamaño actual).

### Copia cifrada fuera del servidor

Cada 6 horas (00:45, 06:45, 12:45 y 18:45 UTC; GitHub puede demorarlas un rato) el workflow `copia-externa.yml` saca por SSH la copia más reciente. La copia ya viene **cifrada en el servidor** con [age](https://github.com/FiloSottile/age) para las llaves públicas de `deploy/backup-recipients.txt`, y se guarda como artefacto de GitHub por **30 días**. La copia sin cifrar nunca sale del servidor, y la CI se niega a subir algo que no esté cifrado. También se puede lanzar a mano: Actions → «Copia cifrada fuera del servidor» → Run workflow.

- El repo es público: cualquiera con cuenta de GitHub puede descargar los artefactos, pero sin una llave privada son bytes ilegibles. Solo se ve la fecha y el tamaño.
- **Si se pierden todas las llaves privadas, las copias externas no se pueden abrir.** Cada uno guarda la suya en dos lugares (por ejemplo, el gestor de contraseñas y un pendrive).
- GitHub apaga los workflows programados de un repo público después de 60 días sin commits; si eso pasa, avisa por mail y se reactiva desde Actions.

**Generar una llave (una vez por persona, en Windows):**
1. Bajar de https://github.com/FiloSottile/age/releases el archivo que termina en `windows-amd64.zip` y descomprimirlo.
2. En PowerShell, dentro de esa carpeta: `.\age-keygen.exe -o clave-copias-distrito.txt`
3. Muestra `Public key: age1…`: esa línea (la pública) se agrega a `deploy/backup-recipients.txt`. **El archivo `clave-copias-distrito.txt` es la llave privada: no se manda a nadie** (ni por chat) y no se sube al repo.

**Recuperar desde una copia externa:**
1. Actions → «Copia cifrada fuera del servidor» → una ejecución → Artifacts → descargar y descomprimir (queda `distrito-….dump.age`).
2. Descifrar: `.\age.exe -d -i clave-copias-distrito.txt -o copia.dump distrito-….dump.age`
3. Subir y restaurar: `scp copia.dump root@<ip>:/var/backups/distrito/` y luego `ssh root@<ip> distrito-restore /var/backups/distrito/copia.dump`.

Si se perdió el servidor entero: crear uno nuevo, poner su IP en `deploy/target.env`, dejar que la CI despliegue (arranca con la base vacía) y hacer el paso 3 sobre el servidor nuevo.

## Riesgos conocidos

- Oracle puede **recuperar instancias Always Free inactivas** y dar de baja cuentas sin uso. Conviene entrar a la consola de vez en cuando.
- Hasta que haya al menos una llave en `deploy/backup-recipients.txt`, las copias están solo en el servidor: perder la máquina es perder los datos.

## Prueba visual de la interfaz

En cada push, CI abre el juego con pantalla virtual (Xvfb + OpenGL) en 1280×720, 1920×1080 pantalla completa y 1024×600,
y saca capturas del menú, el juego, el diálogo, la cuadrilla, el mapa y las opciones (`--ui-snapshot <carpeta>`).
Las capturas quedan en la rama `ui-capturas` del repositorio.

## Actualización automática del cliente

La CI escribe la versión (commit corto) en `server_defaults.json` del cliente y publica `version.txt` junto al zip
en la release `prototipo`. Al abrir, el cliente de Windows compara versiones; si hay una nueva la descarga,
la descomprime en `%APPDATA%\Godot\app_userdata\DISTRITO\actualizacion`, se cierra y un script de PowerShell
copia los archivos sobre la carpeta del juego y lo reabre (`game/scripts/Backend/GameUpdater.cs`).
El servidor se actualiza solo con cada despliegue.
