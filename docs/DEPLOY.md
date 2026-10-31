# Despliegue

Servidor de pruebas: Oracle Cloud Always Free, Ubuntu 24.04 ARM64 (Ampere A1, máx. 2 OCPU / 12 GB para seguir gratis).

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
- `DEPLOY_HOST`: IP pública.
- `DEPLOY_SSH_KEY`: llave privada SSH del usuario `ubuntu`.

Sin esos secretos el job arma el release pero no despliega.

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

## Riesgos conocidos

- Oracle puede **recuperar instancias Always Free inactivas** y dar de baja cuentas sin uso. Conviene entrar a la consola de vez en cuando.
- Sin copias de seguridad de la base todavía. Pendiente antes de que haya datos que importen.
