using System.Numerics;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Combat;

/// <summary>Estado de PvP del barrio que el servidor de juego consulta al backend cada pocos segundos.</summary>
public sealed record CombatState(IReadOnlySet<string> DisputedZones, IReadOnlySet<string> EventZones)
{
    public static CombatState None { get; } = new(new HashSet<string>(), new HashSet<string>());
}

/// <summary>
/// PvP regulado (decisión del equipo, 2026-10-31). Se puede pelear: en zonas en disputa (dos o más cuadrillas o
/// coaliciones con 10+ de influencia en la temporada), en la zona de un evento mientras dura, y entre cuadrillas en
/// guerra (pendiente). Nunca cerca de un NPC (zona segura) ni con personajes de nivel 1 o 2.
/// Vida 100; quien llega a 0 queda herido y reaparece a los 10 s; pierde la mercadería que lleva, nunca la plata.
/// </summary>
public static class CombatRules
{
    public const int MaxHealth = 100;
    public const int PunchDamage = 15;
    public const float PunchRange = 1.8f;
    public const float PunchCooldownSeconds = 0.6f;
    public const float RespawnSeconds = 10f;
    public const float SafeRadius = 5f;
    public const int MinLevel = 3;
    public const int DisputeInfluence = 10;

    /// <summary>Cono de golpe: el objetivo tiene que estar adelante (±50°).</summary>
    public static readonly float PunchConeCos = MathF.Cos(50f * MathF.PI / 180f);

    /// <summary>Zona de un punto del barrio: la del NPC más cercano.</summary>
    public static string ZoneAt(Vector3 p) =>
        NpcInteractionRules.All
            .Select(n => (Zone: TerritoryRules.ZoneOfNpc(n.Code), Dist: Vector2.Distance(new(n.Position.X, n.Position.Z), new(p.X, p.Z))))
            .Where(x => x.Zone is not null)
            .OrderBy(x => x.Dist).First().Zone!;

    public static bool InSafeZone(Vector3 p) =>
        NpcInteractionRules.All.Any(n => Vector2.Distance(new(n.Position.X, n.Position.Z), new(p.X, p.Z)) <= SafeRadius);

    /// <summary>¿El golpe llega? (distancia y que el objetivo esté adelante del que pega). Yaw: rotación Y; el frente es −Z.</summary>
    public static bool InReach(Vector3 attacker, float attackerYaw, Vector3 target)
    {
        var to = new Vector2(target.X - attacker.X, target.Z - attacker.Z);
        var dist = to.Length();
        if (dist > PunchRange || MathF.Abs(target.Y - attacker.Y) > 1.2f)
            return false;
        if (dist < 0.05f)
            return true;
        var forward = new Vector2(-MathF.Sin(attackerYaw), -MathF.Cos(attackerYaw));
        return Vector2.Dot(forward, to / dist) >= PunchConeCos;
    }

    /// <summary>
    /// ¿Se permite el daño? levels: null si el servidor no tiene cuentas (partida local de prueba: todo vale salvo zona segura).
    /// </summary>
    public static string? WhyNot(Vector3 attacker, Vector3 target, int? attackerLevel, int? targetLevel, CombatState state, bool atWar = false)
    {
        if (InSafeZone(attacker) || InSafeZone(target))
            return "Cerca de los locales no se pelea (zona segura).";
        if (attackerLevel is null || targetLevel is null)
            return null;
        if (attackerLevel < MinLevel)
            return $"Hasta el nivel {MinLevel} no podés pelear.";
        if (targetLevel < MinLevel)
            return $"No se puede atacar a alguien de nivel menor a {MinLevel}.";
        var zone = ZoneAt(target);
        if (atWar || state.DisputedZones.Contains(zone) || state.EventZones.Contains(zone))
            return null;
        return $"{TerritoryRules.NameOf(zone)} está tranquila: solo se pelea en zonas en disputa, en eventos o en guerra.";
    }
}
