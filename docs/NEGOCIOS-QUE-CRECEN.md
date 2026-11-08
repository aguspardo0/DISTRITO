# Negocios que crecen

Aprobado por el equipo el 2026-11-02. Pedido: que el mercado sea entretenido, que los negocios suban de fama, mejoren
calidad y local, que den plata, que cada vez venga más gente y que tenga sentido real. **Sin tope**, con la clientela
saliendo del mercado simulado con mucha gente, y con una forma de que más calidad traiga **más** gente (no menos).
La fama baja de a poco si el local se descuida; las mejoras las cobra el Taller Caruso. La protección de las familias,
más adelante.

## Cómo funciona

**La clientela sale de la gente de Bellmont** (`config/negocios.json`): unas 9.000 personas en cinco zonas. En cada ciclo
(5 minutos), la gente de cada zona que quiere un café (o una noche en el club) se reparte entre **todos los locales del
rubro**, con la opción de no comprar. Cada local atrae según:

- **precio** contra el precio justo para su calidad (caro espanta, barato atrae);
- **calidad** (la gente prefiere lo bueno);
- **fama** (sin techo, cada punto suma un poco menos);
- **atracción** (toldo, neón, avisos…);
- **distancia** a cada barrio.

Además llegan **visitantes por la fama**: los que vienen por recomendación. Crecen sin techo (curva logarítmica), y
más calidad trae más.

**Cómo la calidad trae más gente aunque se cobre más:** cada nivel de calidad sube un 10 % el precio que la gente
considera justo, así que cobrando ese precio no se espanta nadie. Y además: la gente prefiere el local de más calidad,
cada cliente atendido da más fama, y llegan más visitantes por recomendación.

**Capacidad:** cuántos se atienden por ciclo (3 de base, +1 por nivel, +1 con el empleado, +1/+2 por Administración).
Los que no entran se van, y una cola larga resta un poco de fama.

**Fama:** sube con cada cliente atendido (más con calidad y precio justo), baja con los que se van sin comprar y se
olvida de a poco. Cerrado (sin mercadería), se olvida más rápido: cerca de un cuarto por día. Niveles: Nuevo ·
Conocido en la cuadra (150) · Popular (600) · Famoso (2.000) · Leyenda de Bellmont (6.000). Después siguen las
estrellas: una cada vez que se duplica.

**Mejoras sin tope**, tres ramas. Cada nivel cuesta un 60 % más que el anterior. Se paga con la caja del negocio y, si
falta, con el bolsillo del dueño. Va al Taller Caruso. El alquiler sube un 15 % por nivel.

| Rama | Café | Club | Efecto por nivel |
|---|---|---|---|
| Capacidad | Mostrador grande → Mesitas en la vereda → Salón con mesas → … | Más mesas → Reservados → Pista de baile → … | +1 cliente por ciclo |
| Calidad | Molinillo propio → Máquina de espresso → Café de primera → … | Barra de caoba → Banda estable → Escenario con luces → … | +25 % de preferencia, +10 % de precio justo, más fama |
| Atracción | Toldo a rayas → Cartel de neón → Aviso en La Gaceta → … | Rocola → Marquesina de neón → Afiches → … | +20 % de gente nueva |

**Categorías** según las mejoras: Puesto → Café → Confitería → Gran confitería (y Club → Club con banda → Gran club
nocturno → Palacio del jazz). Después, estrellas cada 12 mejoras.

**Precio libre** (aprobado 2026-11-08, como en Sim Companies): cada dueño cobra lo que quiera, desde un centavo hasta un
millón (límites técnicos), con los botones o escribiendo el precio. Lo frenan las consecuencias: caro viene menos gente,
sube menos la fama y el crítico pone mala nota; por debajo del costo pierde plata en cada venta. La ventana dice si está caro, justo o barato para
tu calidad.

**Algo para hacer cada día:**
- **Evento del rubro:** la *Mañana de feria* (café) y la *Noche de fiesta* (club) traen 2,5 veces más gente durante una
  hora o hora y media. La hora cambia cada día y sale en La Gaceta y en «Mi negocio»: hay que tener mercadería y lugar.
- **Horas pico y eventos con el reloj de Bellmont** (aprobado 2026-11-08): las horas pico (`picoDesde`/`picoHasta`) y el
  horario de los eventos son horas de Bellmont, cuyo día dura 12 horas reales. El pico pasa dos veces por día real con la
  mitad de duración cada vez, así que la gente por día es la misma; el evento sigue siendo uno por día real.
- **El crítico de La Gaceta** pasa una vez por día a una hora que nadie sabe. De 1 a 5 estrellas, según calidad, precio
  justo y si estaba abierto. Las 5 estrellas pueden ser el titular del diario. Cerrado: una estrella.
- **Competencia:** los locales del mismo rubro se reparten la misma gente.

**Se ve en el mundo:** gente haciendo cola frente al local (tantos como los atendidos en el último ciclo, hasta 16). La
pizarra de la esquina muestra cada local con su precio y su categoría, y cuál está de moda. La Gaceta publica las
críticas, los locales más nombrados y la agenda de eventos.

## Ritmo (simulador, `mercado.yml`)

Un puesto de café que reinvierte todo, con el precio justo:

| Día | Fama | Atendidos por día | Ganancia por día | Mejoras (cap/cal/atr) |
|---|---|---|---|---|
| 1 | Nuevo (64) | 864 | $1.440 | 1/0/0 |
| 3 | Conocido (254) | 1.440 | $2.506 | 2/1/1 |
| 5 | Popular (603) | 1.728 | $3.816 | 3/2/1 |
| 12 | Famoso (2.085) | 2.304 | $7.214 | 6/3/5 |
| 21 | Famoso (3.972) | 2.880 | $11.851 | 7/6/7 |

Leyenda de Bellmont, en más de un mes. Apostar a la calidad rinde más que solo ampliar: en 14 días, el de calidad gana
$10.900 por día contra $3.400 del que solo amplió. Tres días sin mercadería bajan la fama de Popular a cero, y en una
semana se recupera. El que nunca mejora pierde clientes contra el que reinvierte. Todos los valores están en
`config/negocios.json` (se releen sin reiniciar el backend).

## Archivos

- `shared/Distrito.Shared/Economy/BusinessGrowth.cs`: reglas, config y vista del dueño.
- `backend/Distrito.Backend/Economy/EconomyService.BusinessGrowth.cs` (mejoras, precio, config) y el ciclo en
  `EconomyService.Business.cs`. Migración `0034_business_growth.sql`.
- `game/scripts/Economy/MarketNetwork.cs` (gente en los locales, acciones) y `MarketPanel.cs` (pestaña «Mi negocio», tecla P).
- `tools/Distrito.MarketSim/Growth.cs`: escenarios de ritmo.
- Pruebas: `BusinessGrowthTests` y `BusinessGrowthApiTests`. En las pruebas del backend la ciudad tiene mil veces más
  gente (`Business:Crowd`): así manda la capacidad y las ventas son exactas.

## La competencia (aprobado 2026-11-08)

Además de los locales de los jugadores, cada rubro tiene un local de la competencia que no es de nadie
(`competidores` en `config/negocios.json`): el **Café La Perla** (en El Centro, a pasos del Mercado: $6 la taza,
calidad 1, fama 200, toldo) y el club **El Gato Negro** ($8,50, calidad 1, fama 250). Entran en el reparto de la
clientela como un local más, con precio, calidad, fama y atracción fijos: si tu local es peor o más caro, se llevan
parte de la gente; si es mejor o más barato, se la sacás. No venden mercadería ni mueven plata (los clientes que se
llevan simplemente no le compran a ningún jugador). Un puesto nuevo igual vende: pierde un poco de gente, pero sigue
por encima de lo que puede atender al principio.

## Turnos de empleado

Ver `docs/PRIMERA-HORA.md`: tope de **6 turnos cobrados por día** (aprobado 2026-11-08).
