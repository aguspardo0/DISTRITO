# Dirección de arte (v0.1)

Base: documento de diseño conceptual v0.1, sección 11. Objetivo: que el equipo y cualquier artista que se sume produzcan piezas coherentes entre sí.

## Estilo
**Semi-realista estilizado.** Proporciones humanas reales, materiales creíbles (tela, ladrillo, madera, cromo), formas algo simplificadas. **No fotorrealismo.** Prioridades, en orden: legibilidad → rendimiento → producción asumible por un equipo chico.

## Bellmont, 1934
- **Arquitectura:** art déco en Downtown (fachadas escalonadas, líneas verticales, letreros de neón); ladrillo rojo y escaleras de incendio en Little Italy; galpones, grúas y madera húmeda en Old Harbor; chimeneas y chapa en Industrial Quarter.
- **Calles:** adoquín y asfalto gastado, faroles a gas/eléctricos, postes de teléfono, kioscos de diarios, toldos de comercios.
- **Vehículos:** sedanes y camiones de reparto de fines de los años 20 y principios de los 30, tranvías.

## Luz y color
- **Día:** luz cálida y polvorienta; colores apagados (ocres, marrones, verde botella, gris piedra).
- **Noche (noir):** contraste fuerte, charcos que reflejan, neón rojo/ámbar/verde, ventanas cálidas.
- **Paleta por familia** (detalles en ropa, autos y letreros, nunca como uniforme):
  | Familia | Acento |
  |---|---|
  | Moretti (comercio, puertos) | azul marino, bronce |
  | Valenti (política, finanzas) | bordó, dorado |
  | Caruso (industria, transporte) | verde oliva, acero |

## Personajes
- Proporción humana real (1,60–1,90 m). Silueta legible a distancia por **sombrero + abrigo + postura**.
- Vestimenta por clase: trajes de tres piezas y fedora (alta); camisa, tirantes, gorra (trabajadores); overol y botas (puerto/industria).
- Un esqueleto humanoide estándar compartido por todos los personajes, para reutilizar animaciones.

## Presupuesto técnico (guía inicial, a medir)
| Pieza | Triángulos | Texturas |
|---|---|---|
| Personaje | 15–30 k | 2 × 2048 (cuerpo, ropa) |
| Vehículo | 20–40 k | 1–2 × 2048 |
| Edificio modular (por módulo) | 2–10 k | atlas compartidos 2048 |
| Objeto chico | < 2 k | atlas compartido |

Materiales PBR (Forward+). Reutilización por módulos y atlas antes que piezas únicas.

## Fuentes de arte
- **Preferir CC0** mientras el repositorio sea público (Kenney, Quaternius, Poly Haven, MakeHuman para personajes base).
- Assets con licencias que prohíben redistribuir los archivos (por ejemplo, Mixamo) **no se suben** a un repositorio público.
- Registrar en `assets/LICENSES.md` el origen y la licencia de cada pieza que se incorpore.

## Personajes (2026-10-31)

Cuatro láminas del equipo: El Ejecutivo, El Operativo, El Trabajador y El Asociado. Hoy el juego usa modelos
provisionales armados con piezas (`game/scripts/Player/CharacterModel.cs`) con la silueta y la paleta de cada lámina,
y los retratos en el menú de creación (`game/assets/portraits`). Para los modelos definitivos: un `.glb` por aspecto,
con esqueleto humanoide y animaciones (quieto, caminar, correr, saltar), ~10–20 k triángulos, mirando a −Z,
altura 1,8 m con el origen en los pies. Se reemplaza `CharacterModel.Build` por la carga del `.glb` sin tocar red ni datos.
