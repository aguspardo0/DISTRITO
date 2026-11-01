namespace Distrito.Shared.Economy;

/// <summary>Un arma (propuesta aprobada por el equipo, 2026-10-31). Daño hasta FalloffStart; baja hasta 0 en Range.</summary>
public sealed record WeaponType(
    string Code, string Name, long PriceCents, int MinLevel, int Damage, float Range, float CooldownSeconds,
    int Magazine, string AmmoItem, string AmmoName, bool Automatic = false, float FalloffStart = float.MaxValue)
{
    public int DamageAt(float distance)
    {
        if (distance >= Range)
            return 0;
        if (distance <= FalloffStart || FalloffStart >= Range)
            return Damage;
        var t = (distance - FalloffStart) / (Range - FalloffStart);
        return Math.Max(1, (int)MathF.Round(Damage * (1 - t)));
    }
}

/// <summary>Caja de munición que se vende junta: suma Rounds unidades de AmmoItem por PriceCents.</summary>
public sealed record AmmoPack(string Code, string Name, string AmmoItem, int Rounds, long PriceCents);

public static class Weapons
{
    public const string Fists = "punos";
    public const float ReloadSeconds = 1.5f;

    public static readonly WeaponType Pistol = new("pistola", "Pistola", 4_000, 3, 25, 20f, 0.5f, 7, "bala-pistola", "balas de pistola");
    public static readonly WeaponType Shotgun = new("escopeta", "Escopeta", 7_000, 5, 60, 12f, 1.2f, 2, "cartucho", "cartuchos", FalloffStart: 6f);
    public static readonly WeaponType Thompson = new("thompson", "Thompson", 15_000, 8, 12, 25f, 0.1f, 50, "bala-thompson", "balas de Thompson", Automatic: true);

    public static readonly IReadOnlyList<WeaponType> All = [Pistol, Shotgun, Thompson];

    public static readonly IReadOnlyList<AmmoPack> Packs =
    [
        new("caja-balas-pistola", "Caja de 7 balas de pistola", Pistol.AmmoItem, 7, 200),
        new("caja-cartuchos", "Caja de 6 cartuchos", Shotgun.AmmoItem, 6, 300),
        new("tambor-thompson", "Tambor de 50 balas de Thompson", Thompson.AmmoItem, 50, 800),
    ];

    public static WeaponType? Find(string? code) => All.FirstOrDefault(w => w.Code == code);
    public static AmmoPack? Pack(string? code) => Packs.FirstOrDefault(p => p.Code == code);
}

/// <summary>El servidor de juego descuenta la munición que se gastó (en tandas).</summary>
public sealed record AmmoUseRequest(Guid CharacterId, string AmmoItem, int Rounds, string RequestId);
