# Modelos 3D sin tocar código (2026-11-09)

El equipo hace los modelos con Meshy. Para que entren al juego **no hace falta programar**: se tira el `.glb` en la
carpeta que corresponde y el juego lo usa en lugar de la forma provisoria.

## Dónde va cada uno

| Carpeta (`game/modelos/…`) | Nombre del archivo | Reemplaza a |
|---|---|---|
| `personas/` | `trabajador`, `ejecutivo`, `operativo`, `asociado` (los cuatro estilos del personaje), `vecino`, `vecina`, `policia` (habitantes), `comerciante` (todos los NPC fijos) o el código de un NPC fijo (por ejemplo `deposito-delgado`, `cafe-esquina`) | las personas armadas con piezas |
| `autos/` | `camioneta`, `camion` | la camioneta y el camión |
| `muebles/` | el tipo del mueble: `mostrador`, `estante-alto`, `mesa`, `silla-fija`, `heladera`, `rocola`… (ver `Sizes` en `InteriorBuilder.cs`) | los muebles de los interiores |
| `objetos/` | `cajon`, `barril`, `tacho`, `caja`, `neumatico`, `bolsa`, `silla`, `botella` | los objetos con física |
| `edificios/` | `hotel-roma`, `club-jazz`, `barberia`, `ferreteria`, `correo` | las fachadas de la Calle Principal |

- **Variantes:** `vecino.glb`, `vecino-2.glb`, `vecino-3.glb`… Cada persona usa siempre la misma.
- **Tamaño y orientación automáticos:** una persona se escala a 1,8 m; un auto, al largo de su tipo; un mueble o un
  objeto, para que entre en su caja; una fachada, para que entre en su lote. Se apoya en el piso, se centra y se gira
  para mirar hacia adelante. **La colisión no cambia** (sigue siendo la caja de siempre).
- **Animaciones:** si la persona trae esqueleto y animaciones (Meshy las genera al «rigear»), el juego usa las que se
  llamen *idle / walk / run / punch / death* (o *quieto, caminar, correr, golpe, muerte*). El arma se cuelga del hueso de
  la mano derecha si lo encuentra (`RightHand`, `hand_r`…).

## Si algo queda mal: `game/modelos/ajustes.json`

```json
{
  "personas": { "girar": 180 },
  "personas/vecina": { "altura": 1.68 },
  "autos/camion": { "girar": 0, "subir": 0.05 },
  "muebles/mostrador": { "escala": 0.9 }
}
```

`girar` (grados; por defecto 180, porque los `.glb` suelen mirar a +Z y el juego a −Z), `altura` (metros, personas),
`subir` (metros) y `escala` (multiplicador). Clave «tipo/código» o solo «tipo» para toda la carpeta.

## Cómo subirlos

Los `.glb` van por **Git LFS** (ya está configurado en `.gitattributes`): hace falta tener `git lfs` instalado
(`git lfs install` una vez). Se suben con `git add`, `git commit` y `git push` como cualquier archivo. La CI los baja
con caché, así no se gasta el cupo de descarga de GitHub en cada prueba.

**Consejos para Meshy:** exportar en `.glb`, con texturas incluidas; para personas, «rigear» con las animaciones de
caminar, correr y quieto; apuntar a menos de 30.000 triángulos por persona y 60.000 por auto (el juego genera solo los
niveles de detalle para la distancia).
