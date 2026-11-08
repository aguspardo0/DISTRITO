using Distrito.Shared.Api;

namespace Distrito.Shared.Life;

/// <summary>
/// Un habitante como lo ve un jugador que está cerca (vida de los NPC, etapa 5): quién es y el tramo que está haciendo
/// (de dónde sale, por qué esquinas pasa, a dónde llega y cuándo). Con eso el cliente lo hace caminar solo, sin que el
/// servidor mande la posición a cada rato: se manda una vez por decisión.
/// </summary>
public sealed record ResidentView(Guid Id, string Name, string Profession, bool Woman, string Activity,
    long DepartMs, long ArriveMs, float[] Points, bool HiddenOnArrival, bool Bag = false)
{
    /// <summary>Posición y hacia dónde mira a una hora dada (ms Unix del servidor).</summary>
    public (float X, float Z, float DirX, float DirZ, bool Walking) At(long nowMs)
    {
        var count = Points.Length / 2;
        if (count == 0)
            return (0, 0, 0, 1, false);
        if (count == 1 || nowMs >= ArriveMs || ArriveMs <= DepartMs)
        {
            var (dx, dz) = count > 1 ? Dir(count - 2, count - 1) : (0f, 1f);
            return (Points[^2], Points[^1], dx, dz, false);
        }
        var t = Math.Clamp((nowMs - DepartMs) / (double)(ArriveMs - DepartMs), 0, 1);
        var total = 0.0;
        for (var i = 1; i < count; i++)
            total += Seg(i - 1, i);
        var left = total * t;
        for (var i = 1; i < count; i++)
        {
            var seg = Seg(i - 1, i);
            if (left <= seg && seg > 0)
            {
                var f = (float)(left / seg);
                var (dx, dz) = Dir(i - 1, i);
                return (Points[2 * (i - 1)] + (Points[2 * i] - Points[2 * (i - 1)]) * f,
                        Points[2 * (i - 1) + 1] + (Points[2 * i + 1] - Points[2 * (i - 1) + 1]) * f, dx, dz, true);
            }
            left -= seg;
        }
        return (Points[^2], Points[^1], 0, 1, false);
    }

    public bool Visible(long nowMs) => nowMs < ArriveMs || !HiddenOnArrival;

    private double Seg(int a, int b)
    {
        double dx = Points[2 * b] - Points[2 * a], dz = Points[2 * b + 1] - Points[2 * a + 1];
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private (float X, float Z) Dir(int a, int b)
    {
        float dx = Points[2 * b] - Points[2 * a], dz = Points[2 * b + 1] - Points[2 * a + 1];
        var len = MathF.Sqrt(dx * dx + dz * dz);
        return len < 1e-4f ? (0, 1) : (dx / len, dz / len);
    }

    /// <summary>Lo que ve un jugador de un habitante en este momento.</summary>
    public static ResidentView Of(Resident r, ResidentState s)
    {
        var points = s.Waypoints();
        if (s.ArrivesAt <= s.Since)
            points = [(s.X, s.Z)];
        return new ResidentView(r.Id, r.FullName, r.Profession, r.Woman, s.Activity,
            s.Since.ToUnixTimeMilliseconds(), s.ArrivesAt.ToUnixTimeMilliseconds(),
            points.SelectMany(p => new[] { (float)Math.Round(p.X, 2), (float)Math.Round(p.Z, 2) }).ToArray(), s.Indoor, s.Bag);
    }
}

/// <summary>Formato compacto para la red: un paquete con los tramos nuevos y los que dejaron de verse.</summary>
public static class ResidentSyncCodec
{
    public const int MaxPoints = 64;

    public static byte[] Encode(long serverNowMs, IReadOnlyList<ResidentView> views, IReadOnlyList<Guid> removed)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1); // versión
        w.Write(serverNowMs);
        w.Write((ushort)views.Count);
        foreach (var v in views)
        {
            w.Write(v.Id.ToByteArray());
            w.Write(v.Name);
            w.Write(v.Profession);
            w.Write(v.Activity);
            w.Write((byte)((v.Woman ? 1 : 0) | (v.HiddenOnArrival ? 2 : 0) | (v.Bag ? 4 : 0)));
            w.Write(v.DepartMs);
            w.Write(v.ArriveMs);
            var count = Math.Min(v.Points.Length / 2, MaxPoints);
            w.Write((byte)count);
            for (var i = 0; i < count * 2; i++)
                w.Write(v.Points[i]);
        }
        w.Write((ushort)removed.Count);
        foreach (var id in removed)
            w.Write(id.ToByteArray());
        w.Flush();
        return ms.ToArray();
    }

    public static (long ServerNowMs, List<ResidentView> Views, List<Guid> Removed) Decode(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            if (r.ReadByte() != 1)
                throw new ArgumentException("versión desconocida");
            var now = r.ReadInt64();
            var views = new List<ResidentView>();
            for (int i = 0, n = r.ReadUInt16(); i < n; i++)
            {
                var id = new Guid(r.ReadBytes(16));
                var name = r.ReadString();
                var profession = r.ReadString();
                var activity = r.ReadString();
                var flags = r.ReadByte();
                var depart = r.ReadInt64();
                var arrive = r.ReadInt64();
                var points = new float[r.ReadByte() * 2];
                for (var p = 0; p < points.Length; p++)
                    points[p] = r.ReadSingle();
                views.Add(new ResidentView(id, name, profession, (flags & 1) != 0, activity, depart, arrive, points, (flags & 2) != 0, (flags & 4) != 0));
            }
            var removed = new List<Guid>();
            for (int i = 0, n = r.ReadUInt16(); i < n; i++)
                removed.Add(new Guid(r.ReadBytes(16)));
            return (now, views, removed);
        }
        catch (EndOfStreamException)
        {
            throw new ArgumentException("paquete cortado");
        }
    }
}

/// <summary>
/// Quién le toca a cada jugador (niveles de simulación): <b>cerca</b> (se dibuja caminando), <b>intermedio</b> (no se
/// dibuja pero ya está por aparecer) y <b>lejos</b> (solo vive en la simulación). Con margen para que no parpadeen en
/// el borde.
/// </summary>
public static class ResidentLevels
{
    public const double Near = 65;
    public const double KeepNear = 75;
    public const double Middle = 150;

    /// <summary>Como mucho, cuántos se dibujan cerca de un jugador (los más cercanos).</summary>
    public const int MaxDrawn = 40;

    public static bool InRange(double dx, double dz, bool alreadySeen) =>
        dx * dx + dz * dz <= (alreadySeen ? KeepNear * KeepNear : Near * Near);
}

/// <summary>Ropa de los habitantes (provisoria, con las piezas de los personajes): la policía de azul, el resto variado.</summary>
public static class ResidentLooks
{
    private static readonly string[] Skins = ["#c99a7a", "#b98466", "#c49474", "#a87556", "#d4ab8c", "#8f5f44"];
    private static readonly string[] Hairs = ["#1c1612", "#2a2018", "#4a3424", "#6b5a48", "#8a8378", "#141010"];
    private static readonly string[] Shirts = ["#b9ae9c", "#d6cdb9", "#8d9aa3", "#a8957a", "#c7b8a0"];
    private static readonly string[] Pants = ["#2e2620", "#3a3028", "#26262c", "#45403a", "#3a3a44"];
    private static readonly string[] Tops = ["#4a3626", "#2a2826", "#5b4632", "#36404a", "#6e5a44", "#4d2f2a"];
    private static readonly string[] Dresses = ["#6e2f36", "#3f5a6b", "#7a6a4a", "#4f6a4a", "#8a5a3c", "#5a4a6a"];

    public static CharacterLook For(ResidentView v)
    {
        var h = (ulong)Math.Abs(v.Id.GetHashCode() == int.MinValue ? 1 : v.Id.GetHashCode()); // Guid.GetHashCode es estable
        string Pick(string[] list, int salt) => list[(int)((h >> (salt * 4)) % (ulong)list.Length)];
        if (v.Profession == "policia")
            return new CharacterLook("policia", "Policía", Pick(Skins, 0), Pick(Hairs, 1), "#c9d0d8", "#1f2a44", "#26345a",
                "#1a1a1a", "#141210", Hat: "#1f2a44", Jacket: true);
        if (v.Woman)
            return new CharacterLook("vecina", "Vecina", Pick(Skins, 0), Pick(Hairs, 1), Pick(Shirts, 2), Pick(Dresses, 3),
                Pick(Dresses, 3), Pick(Tops, 4), "#2a1e16", Hat: null, LongCoat: true);
        var hat = (h >> 20) % 3 == 0 ? null : Pick(Tops, 5);
        return new CharacterLook("vecino", "Vecino", Pick(Skins, 0), Pick(Hairs, 1), Pick(Shirts, 2), Pick(Pants, 3),
            Pick(Tops, 4), Pick(Tops, 6), "#2a1e16", hat, Vest: (h >> 24) % 2 == 0, Jacket: v.Profession is "obrero" or "transportista",
            Mustache: (h >> 28) % 3 == 0);
    }
}
