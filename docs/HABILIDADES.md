# Árbol de habilidades

Aprobado por el equipo (2026-11-01). Tecla **K**. Un **punto por nivel** desde el 2 (nivel 30 = 29 puntos). Los oficios
(Logística, Negociación, Administración) siguen subiendo con la práctica; el árbol es aparte.
Definición: `shared/Distrito.Shared/Progression/SkillTree.cs` — para sumar habilidades se agrega un nodo ahí y su efecto.

Escalones: nivel 1, nivel 5 y nivel 10. Cada nodo puede pedir otro de su rama.

| Rama | Nodo | Escalón | Rangos | Efecto | Pide |
|---|---|---|---|---|---|
| Calle | Piel dura | 1 | 2 | +10 vida máxima por rango | — |
| Calle | Puños de hierro | 1 | 2 | +5 daño de piña por rango | — |
| Calle | Pulso firme | 5 | 2 | +10 % daño con armas por rango | Piel dura |
| Calle | Intimidación | 5 | 1 | NPC se rinden 25 % más rápido | Puños de hierro |
| Calle | Recarga rápida | 10 | 1 | recarga 30 % más rápida | Pulso firme |
| Calle | Sangre fría | 10 | 1 | +15 s antes de desangrarse | Pulso firme |
| Negocios | Regateo | 1 | 2 | 5 % menos en compras a NPC por rango | — |
| Negocios | Buen vendedor | 1 | 1 | +10 % al venderle a NPC | — |
| Negocios | Cobrador | 5 | 2 | +5 % paga de contratos por rango | Regateo |
| Negocios | Contactos | 5 | 1 | 5 % menos en pedidos por mayor | Buen vendedor |
| Negocios | Mesa alta | 10 | 1 | apuestas hasta $100 en el casino | Cobrador |
| Logística | Puntual | 1 | 2 | +60 s en contratos con plazo por rango | — |
| Logística | Buen conductor | 1 | 1 | vehículos 10 % más rápidos | — |
| Logística | Estiba | 5 | 2 | +15 % de carga en vehículos por rango | Buen conductor |
| Logística | Ojo de proveedor | 5 | 1 | pedidos listos un ciclo antes | Puntual |
| Logística | Cerrajero | 10 | 1 | forzar vehículos 2 s más rápido | Estiba |

Por ahora no se pueden reiniciar los puntos (se puede sumar más adelante, por ejemplo pagando).
