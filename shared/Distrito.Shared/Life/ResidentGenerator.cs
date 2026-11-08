namespace Distrito.Shared.Life;

/// <summary>
/// Crea los habitantes de Bellmont desde config/vida.json. Determinista: la misma config y la misma semilla dan siempre
/// los mismos habitantes (mismos ids, nombres y personalidades), así se pueden reconstruir y probar.
/// Se usa una sola vez: después los habitantes viven en la base.
/// </summary>
public static class ResidentGenerator
{
    public static List<Resident> Generate(LifeConfig cfg)
    {
        var rng = new Random(cfg.Seed);
        var residents = new List<Resident>();
        var usedNames = new HashSet<string>();
        var index = 0;
        foreach (var profession in cfg.Professions)
            for (var i = 0; i < profession.Count; i++, index++)
            {
                var woman = profession.Code != "policia" && rng.NextDouble() < 0.45; // la policía de 1952: solo hombres
                string first, last;
                var attempts = 0;
                do
                {
                    first = Pick(rng, woman ? cfg.Names.Women : cfg.Names.Men);
                    last = Pick(rng, cfg.Names.Surnames);
                } while (!usedNames.Add($"{first} {last}") && ++attempts < 50); // con pocos nombres, se repiten

                var district = cfg.Districts[rng.Next(cfg.Districts.Count)];
                var angle = rng.NextDouble() * Math.Tau;
                var distance = Math.Sqrt(rng.NextDouble()) * district.Radius;
                var workplace = profession.Places.Count == 0 ? null : profession.Places[i % profession.Places.Count];

                var (from, to) = (profession.ShiftFrom, profession.ShiftTo);
                if (profession.NightShiftEvery > 0 && i % profession.NightShiftEvery == profession.NightShiftEvery - 1)
                    (from, to) = (to, from); // turno noche: el mismo horario al revés

                residents.Add(new Resident(
                    IdFor(cfg.Seed, index),
                    first, last, woman,
                    rng.Next(profession.AgeMin, profession.AgeMax + 1),
                    profession.Code,
                    district.Code,
                    Math.Round(district.X + Math.Cos(angle) * distance, 2),
                    Math.Round(district.Z + Math.Sin(angle) * distance, 2),
                    workplace, from, to,
                    Personality(rng, cfg),
                    profession.MoneyMin + (long)(rng.NextDouble() * (profession.MoneyMax - profession.MoneyMin))));
            }
        return residents;
    }

    /// <summary>Rasgos de 0 a 1. Los opuestos se complementan (muy sociable = poco reservado).</summary>
    private static Dictionary<string, double> Personality(Random rng, LifeConfig cfg)
    {
        var traits = new Dictionary<string, double>();
        foreach (var pair in cfg.Opposites)
        {
            var v = Math.Round(Bell(rng), 2);
            traits[pair[0]] = v;
            traits[pair[1]] = Math.Round(1 - v, 2);
        }
        foreach (var trait in cfg.Traits.Where(t => !traits.ContainsKey(t)))
            traits[trait] = Math.Round(Bell(rng), 2);
        return traits;
    }

    /// <summary>Entre 0 y 1, con más gente en el medio que en los extremos.</summary>
    private static double Bell(Random rng) => (rng.NextDouble() + rng.NextDouble() + rng.NextDouble()) / 3;

    private static string Pick(Random rng, List<string> list) => list[rng.Next(list.Count)];

    /// <summary>Id estable por semilla y número de habitante (no depende del orden de la base).</summary>
    public static Guid IdFor(int seed, int index)
    {
        var bytes = new byte[16];
        new Random(unchecked(seed * 7919 + index * 104729)).NextBytes(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40); // versión 4
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante
        return new Guid(bytes);
    }

    public static ResidentState InitialState(Resident r, DateTimeOffset now) =>
        new(r.Id, LifeActivities.AtHome, LifeSimulation.HomePlace, r.HomeX, r.HomeZ, now,
            ArrivesAt: now, Until: now, FromX: r.HomeX, FromZ: r.HomeZ, Money: r.PocketMoneyCents, NeedsAt: now, Indoor: true);
}

/// <summary>
/// El reloj de Bellmont (decisión del equipo, 2026-11-08): 6 horas reales de día y 6 de noche, así que hay dos días de
/// Bellmont por cada día real y el reloj corre al doble. La medianoche de Bellmont cae a las 0:00 y a las 12:00 de
/// Argentina: amanece (6:00) a las 3:00 y 15:00, y anochece (18:00) a las 9:00 y 21:00.
/// Por ahora lo usa la vida de los NPC; la economía, La Gaceta y el crítico siguen con el día real.
/// </summary>
public static class BellmontClock
{
    public const int RealSecondsPerDay = 12 * 3600;
    public const int Speed = 86400 / RealSecondsPerDay; // 2
    public const int Dawn = 6;
    public const int Dusk = 18;
    private static readonly TimeSpan Argentina = TimeSpan.FromHours(-3);

    /// <summary>Segundos reales desde la primera medianoche de Bellmont (1/1/1970 a las 0:00 de Argentina).</summary>
    private static long RealSeconds(DateTimeOffset now) => now.ToUnixTimeSeconds() + (long)Argentina.TotalSeconds;

    public static TimeSpan TimeOfDay(DateTimeOffset now) =>
        TimeSpan.FromSeconds(Mod(RealSeconds(now), RealSecondsPerDay) * Speed);

    public static int Hour(DateTimeOffset now) => (int)TimeOfDay(now).TotalHours;

    public static bool IsNight(DateTimeOffset now) => Hour(now) is < Dawn or >= Dusk;

    /// <summary>Número de día de Bellmont (sube dos veces por día real).</summary>
    public static long Day(DateTimeOffset now) => FloorDiv(RealSeconds(now), RealSecondsPerDay);

    /// <summary>Cuándo (en tiempo real) empieza la próxima hora <paramref name="hour"/> de Bellmont.</summary>
    public static DateTimeOffset Next(DateTimeOffset now, int hour)
    {
        var startOfDay = now.AddSeconds(-Mod(RealSeconds(now), RealSecondsPerDay));
        var at = startOfDay.AddSeconds(hour * 3600.0 / Speed);
        return at > now ? at : at.AddSeconds(RealSecondsPerDay);
    }

    private static long Mod(long a, long b) => ((a % b) + b) % b;
    private static long FloorDiv(long a, long b) => (a - Mod(a, b)) / b;
}
