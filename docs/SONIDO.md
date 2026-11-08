# Sonido (2026-11-08)

## Efectos generados por código

No hay archivos de audio para los efectos: `Sfx` los sintetiza al arrancar (ruido filtrado, senos, decaimientos y
ecos) y quedan en memoria. Sirven hasta que haya sonidos grabados de verdad.

| Sonido | Cuándo suena |
|---|---|
| Pasos (4 variantes) | cada 1,7 m que camina cualquier personaje cercano |
| Tiro, con eco de la calle | cada disparo (se oye hasta 140 m) |
| Piña | cada golpe |
| El chorro de la urna | mientras se sirve el café en el turno (Espacio) |
| La taza | al entregar un café |
| Motor | los autos con chofer o en movimiento: más agudo y fuerte cuanto más rápido |
| La calle | murmullo y tránsito de fondo: más fuerte de día, bajito de noche |
| Bocinas | de vez en cuando, a lo lejos (menos de noche) |

`SoundPlayer` los reproduce en el mundo con un grupo de 24 reproductores que se reusan. Todo va por el bus **Efectos**.

## Música

Ver `game/audio/musica/CREDITOS.md`: discos de jazz de dominio público (grabaciones de 1925 o antes). El reproductor
(`MusicPlayer`) ya está; faltan los archivos.

## Volumen

En Esc → Opciones: **Volumen de la música** y **Volumen de los efectos y la calle**. Se guardan en `user://settings.cfg`.
