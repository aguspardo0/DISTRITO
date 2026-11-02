# Dirección de arte (v0.2)

Base: documento de diseño conceptual v0.1, sección 11. Objetivo: que el equipo y cualquier artista que se sume produzcan piezas coherentes entre sí.

## Estilo
**Semi-realista estilizado.** Proporciones humanas reales, materiales creíbles (tela, ladrillo, madera, cromo), formas algo simplificadas. **No fotorrealismo.** Prioridades, en orden: legibilidad → rendimiento → producción asumible por un equipo chico.

## Bellmont, 1952

**Época (decisión del equipo, 2026-11-02):** la ciudad pasa de 1934 a 1952. Posguerra: la mafia ya no vive del
contrabando de alcohol sino de los puertos, los sindicatos, el juego, la protección y los negocios «limpios».
Cambia la ambientación; las reglas, la economía y los datos no cambian.

- **Arquitectura:** el art déco de los años 20–30 sigue en pie (bancos, cines y oficinas del barrio Valenti), ya algo gastado;
  al lado aparece lo nuevo: diners de chapa cromada, fachadas lisas con letras sueltas, vidrieras grandes. Ladrillo rojo
  y escaleras de incendio en el barrio Moretti; galpones, grúas y contenedores de madera en Los Muelles; talleres,
  garajes y chimeneas en el barrio Caruso.
- **Techos:** antenas de televisión y carteles de neón en las azoteas (rosa, celeste, rojo, verde, ámbar).
- **Calles:** asfalto con parches de adoquín, alumbrado eléctrico en postes altos con brazo y luz blanca, semáforos,
  postes de teléfono, kioscos de diarios, toldos a rayas, rocolas en los bares.
- **Vehículos:** camionetas y camiones de reparto de 1947–1952: guardabarros redondeados, dos tonos (techo crema),
  parrillas anchas cromadas, paragolpes cromados, faros redondos y cubiertas de banda blanca. Colores de época:
  turquesa, verde, rojo, crema. Los tranvías van quedando y crecen los colectivos.
- **Lo que no va:** autos con alerones grandes y estética de fines de los 50 (eso es 1957 en adelante).

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
- Vestimenta por clase (1952): traje cruzado o de dos piezas con solapa ancha, corbata estampada y fedora de ala más
  corta (alta); camisa arremangada, gorra y campera de trabajo o de cuero (trabajadores y puerto); overol y botas (industria).
  Las cuatro láminas actuales (Ejecutivo, Operativo, Trabajador, Asociado) se dibujaron para 1934: gorras y tiradores
  todavía pasan en el puerto de 1952, pero **el equipo tiene que rehacerlas para los 50** (ver «Pendiente» abajo).
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

## Pendiente por el cambio a 1952

- **Láminas y retratos de personajes** (`game/assets/portraits`): versiones de los 50 de los cuatro aspectos. Cuando
  estén, se ajustan los modelos provisionales (`CharacterModel.cs` y `CharacterLooks.cs`) a la nueva ropa.
- **Ya actualizado en el juego (2026-11-02):** camioneta y camión con diseño de principios de los 50
  (`VehicleBody.BuildModel`), alumbrado eléctrico, antenas de televisión y carteles de neón en los barrios (`CityBuilder`).
- **El Mercado y Los Muelles** (`Main.tscn`, `DocksBuilder`) siguen con su decorado original; se revisan cuando se
  rehaga el arte de esos distritos.
