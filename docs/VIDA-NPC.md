# Vida autónoma de los NPC

Documento del equipo (2026-11-01) e informe «DISTRITO — Vida autónoma de los NPC: auditoría e integración» (2026-11-08).

## Decisiones del equipo (2026-11-08)

- **Compras:** la plata la siguen moviendo los habitantes simulados de la economía (existen desde siempre: por eso un
  local vende apenas tiene mercadería). Los NPC visibles son **ambientación**: representan a esa gente (van al local,
  hacen la fila, salen con la compra) sin pagar aparte ni cambiar lo que vende el local.
- **Identidad liviana:** nombre, rutina y estado se guardan para muchos; solo los cercanos se dibujan y caminan.
- **Día y noche:** 6 horas reales de día y 6 de noche: dos días de Bellmont por día real (`BellmontClock`). La
  medianoche de Bellmont cae a las 0:00 y 12:00 de Argentina; amanece a las 3:00 y 15:00, anochece a las 9:00 y 21:00.
  La economía, La Gaceta y el crítico siguen con el día real.
- **Policía:** entra en la prueba; patrulla y acude a avisos, sin arrestar todavía.
- **Casas:** una puerta por vecino en su barrio, sin interior.
- **Gente comprando en el mundo:** vecinos que salen a comprar y vuelven con la bolsa (ambientación).

## Etapa 1 (2026-11-08): identidad y estado guardado

- `config/vida.json`: rasgos de personalidad (con opuestos que se complementan), barrios, lugares de trabajo,
  profesiones con turno, edad y plata, y listas de nombres de época. Se valida con errores claros.
- `ResidentGenerator` (compartido): crea los 30 habitantes de la prueba (8 trabajadores, 6 comerciantes,
  4 transportistas, 8 vecinos y 4 policías, uno de cada dos de noche). Determinista: la misma semilla da los mismos
  habitantes.
- Backend (`LifeStore`, migración 0036): los crea **una sola vez** la primera vez que se piden y guarda su último
  estado y los hechos importantes (`npc_residents`, `npc_resident_states`, `npc_resident_events`).
  Endpoints internos: `GET /internal/life/residents`, `POST /internal/life/save`,
  `GET /internal/life/residents/{id}/history`.
- Servidor del juego (`LifeNetwork`): los carga al arrancar (línea `[DISTRITO] VIDA habitantes=30 …` en el log) y
  guarda su estado cada 5 minutos. Todavía no se mueven: eso viene con las rutinas y la navegación.

La plata de cada habitante es solo un estado para sus decisiones (si le alcanza para algo), no una cuenta del libro.
