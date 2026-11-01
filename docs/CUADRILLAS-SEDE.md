# Sede, prestigio y misiones de cuadrilla

Aprobado por el equipo (2026-11-01). Reglas en `shared/Distrito.Shared/Crews/CrewProgress.cs`.
El cupo de integrantes (3 → 4 → 5 por contratos y reputación) no cambia: el **prestigio** es un rango aparte.

## Sede
- Los 5 galpones grandes de **Los Muelles** se pueden comprar como sede: el jefe, parado en la puerta, desde el tablero (J).
  Cuesta **$250** de la caja y **$2 por ciclo** de alquiler (siempre, es el local de la cuadrilla). Si la caja no alcanza, queda deuda.
- Los galpones ahora son huecos, con portón hacia la calle (entran personas y vehículos), techo y una lamparita.
  El cartel de la puerta dice «SE VENDE» o «SEDE DE «…»» con el rango (dorado en rango 10).
- **Adentro es zona segura** (afuera sigue siendo zona libre). Los miembros **reaparecen adentro** al morir.
- **Depósito de 500 kg** (+100 en rango 2, +200 más en rango 7): el depósito de la cuadrilla pasa a ser el de la sede
  (si no tenían, se crea sin costo de apertura). La mercadería se ve apilada al fondo del galpón. Se usa desde la sede
  (o desde el galpón de depósitos de Valenti).

## Misiones (tablero, tecla J)
- Cada **3 horas** aparecen **3 misiones** nuevas (4 desde rango 4, 5 desde rango 9), armadas al azar por el generador a
  partir de 7 tipos. El tablero es el mismo para toda la cuadrilla. Pagan **$20–80 a la caja** y **10–40 de prestigio**,
  más difíciles y mejor pagas con el rango. La paga sale de la caja real de la familia que encarga.
- Tipos:
  - **Entrega**: llevar N × mercadería a un local y entregarla desde el tablero estando ahí.
  - **Acopio**: juntar N × mercadería en el depósito y entregarla desde la sede (paga Valenti).
  - **Cobranza**: ir con 1 o 2 de la cuadrilla a un local y cobrar desde el tablero.
  - **Camión**: asaltar el camión de reparto de Moretti (encargo de los Caruso).
  - **Contrabando**: bajar guardias del contrabando en Los Muelles.
  - **Caja**: asaltar la caja de un local puntual (encargo de la familia rival).
  - **Vigilar**: estar en la sede al menos dos de la cuadrilla durante unos minutos.

## Rangos de prestigio
| Rango | Prestigio | Beneficio |
|---|---|---|
| 2 | 100 | Depósito +100 kg |
| 3 | 300 | 5 % menos en el Almacén Moretti |
| 4 | 600 | Cuarta misión en el tablero |
| 5 | 1000 | Armería: 10 % menos en el Taller Caruso |
| 6 | 1500 | Mesa VIP en el casino (hasta $200) |
| 7 | 2100 | Depósito +200 kg más |
| 8 | 2800 | Misiones +25 % de pago |
| 9 | 3600 | Quinta misión |
| 10 | 4500 | Sede de honor: cartel dorado y misiones +50 % |

# Casino «El Trébol»
Local nuevo de la familia Valenti en el barrio (al sur del Taller Caruso, marquesina verde). Se entra hablando con el
portero (E). **Ruleta** europea (color, paridad, mitad 1 a 1; número 35 a 1), **blackjack** (crupier se planta en 17,
blackjack 3 a 2) y **dados** de línea de pase. Apuestas de **$1 a $50**; la **mesa VIP** (hasta $200) es para cuadrillas de
rango 6. El azar lo tira el backend con un generador criptográfico; las apuestas van a la caja de Valenti y los premios
salen de ahí. La casa tiene una ventaja chica y real (2,7 % / 1,4 % / ~0,5 %).
