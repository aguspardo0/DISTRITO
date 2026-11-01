using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Map;

/// <summary>Mapa de Bellmont (tecla M): barrios, edificios, lugares y todos los jugadores conectados.</summary>
public partial class MapPanel : Control
{
    // La ciudad es más alta que ancha: se fija la altura para que entre en pantalla.
    private const float HeightPx = 600f;
    private static readonly float MapScale = HeightPx / (MapPresence.MaxZ - MapPresence.MinZ);
    private static readonly float WidthPx = Mathf.Max(460f, MapScale * (MapPresence.MaxX - MapPresence.MinX));
    private static readonly Color Ground = new(0.2f, 0.19f, 0.17f);
    private static readonly Color AvenueColor = new(0.26f, 0.25f, 0.23f);
    private static readonly Color FreeZone = new(0.55f, 0.18f, 0.14f, 0.35f);
    private static readonly Color Paper = new(0.12f, 0.11f, 0.1f, 0.98f);
    private static readonly Color Building = new(0.32f, 0.29f, 0.25f);
    private static readonly Color Brass = new(0.83f, 0.68f, 0.38f);
    private static readonly Color Others = new(0.85f, 0.85f, 0.8f);
    private static readonly Color Self = new(0.95f, 0.35f, 0.25f);

    private GameNetwork _network = null!;
    private MapNetwork _map = null!;

    /// <summary>Ánimo de las familias para mostrar al pie del mapa (lo trae la economía del jugador).</summary>
    public Func<Distrito.Shared.Economy.EconomySnapshot?>? Economy { get; set; }

    public void Initialize(GameNetwork network, MapNetwork map)
    {
        _network = network;
        _map = map;
        _map.Updated += QueueRedraw;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(WidthPx, HeightPx + 48);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Toggle()
    {
        Visible = !Visible;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw(); // la posición propia se dibuja en vivo
    }

    private static Vector2 ToMap(float x, float z) =>
        new((x - MapPresence.MinX) * MapScale, 24 + (z - MapPresence.MinZ) * MapScale);

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, WidthPx, HeightPx + 48), Paper);
        // Distritos: el Mercado (neutral), las avenidas, Los Muelles (zona libre, en rojo) y el barrio de cada familia con su color.
        foreach (var d in CityLayout.Districts)
        {
            var rect = new Rect2(ToMap(d.MinX, d.MinZ), new Vector2(d.Width * MapScale, d.Depth * MapScale));
            DrawRect(rect, d.Kind == DistrictKind.Avenue ? AvenueColor : Ground);
            if (d.Kind == DistrictKind.Docks)
                DrawRect(rect, FreeZone);
        }
        foreach (var b in CityLayout.Barrios)
        {
            var rect = new Rect2(ToMap(b.Area.MinX, b.Area.MinZ), new Vector2(b.Area.Width * MapScale, b.Area.Depth * MapScale));
            var accent = new Color(b.Accent.R, b.Accent.G, b.Accent.B);
            DrawRect(rect, accent with { A = 0.16f });
            DrawRect(rect, accent with { A = 0.7f }, filled: false, width: 2);
            DrawString(font, rect.Position + new Vector2(6, 15), b.Area.Name.ToUpperInvariant(), HorizontalAlignment.Left, -1, 13, accent.Lightened(0.25f));
        }
        var docks = CityLayout.Docks;
        DrawString(font, ToMap(docks.MinX, docks.MaxZ) + new Vector2(4, -6), "LOS MUELLES — zona libre",
            HorizontalAlignment.Left, docks.Width * MapScale - 6, 11, new Color(0.95f, 0.55f, 0.4f));
        DrawString(font, ToMap(CityLayout.Market.MinX, CityLayout.Market.MaxZ) + new Vector2(4, -6), "EL MERCADO",
            HorizontalAlignment.Left, -1, 11, Brass with { A = 0.8f });
        if (Economy?.Invoke()?.Families is { } families)
        {
            var line = string.Join("   ", families.Select(f => $"{f.Name.Replace("Familia ", "")}: {Distrito.Shared.Economy.FamilyRules.Display(f.Mood).Split(' ')[0]}"));
            DrawString(ThemeDB.FallbackFont, new Vector2(8, HeightPx + 41), line, HorizontalAlignment.Left, WidthPx - 16, 13, Brass);
        }
        DrawRect(new Rect2(0, 24, WidthPx, HeightPx), Brass, filled: false, width: 2);
        DrawString(font, new Vector2(8, 17), $"BELLMONT — {_map.Markers.Count} conectado(s) · [M] cerrar",
            HorizontalAlignment.Left, -1, 14, Brass);

        var scale = MapScale;
        foreach (var b in MapPresence.Buildings)
        {
            var corner = ToMap(b.X - b.Width / 2, b.Z - b.Depth / 2);
            DrawRect(new Rect2(corner, new Vector2(b.Width * scale, b.Depth * scale)), Building);
        }

        // Sedes de cuadrilla en Los Muelles.
        foreach (var b in Distrito.Game.Crews.BaseNetwork.Bases)
        {
            var s = Distrito.Shared.Crews.CrewBaseRules.Sheds[b.Shed];
            DrawString(font, ToMap(s.X - s.Width / 2, s.Z), $"«{b.CrewName}»", HorizontalAlignment.Left, s.Width * MapScale, 11, Brass);
        }

        var territory = Economy?.Invoke()?.Territory;
        foreach (var npc in NpcInteractionRules.All)
        {
            var at = ToMap(npc.Position.X, npc.Position.Z);
            // Territorio: un aro de color si la zona del lugar tiene dueño, con el nombre de la cuadrilla.
            if (Distrito.Shared.Economy.TerritoryRules.ZoneOfNpc(npc.Code) is { } zoneCode && territory?.Zone(zoneCode) is { ControllerCrew: { } owner })
            {
                var color = Color.FromHsv(owner.Sum(ch => ch) * 37 % 360 / 360f, 0.6f, 0.9f); // mismo color en todos los equipos
                DrawArc(at, 22, 0, Mathf.Tau, 32, color, 3);
                DrawString(font, at + new Vector2(-30, 36), $"«{owner}»", HorizontalAlignment.Left, -1, 11, color);
            }
            if (CityLayout.BarrioAt(npc.Position.X, npc.Position.Z) is { } barrio && !NpcInteractionRules.Bosses.Contains(npc))
            {
                // La gente de la familia: punto del color del barrio, con su nombre.
                var tone = new Color(barrio.Accent.R, barrio.Accent.G, barrio.Accent.B);
                DrawCircle(at, 4.5f, tone);
                DrawString(font, at + new Vector2(6, 4), npc.Name, HorizontalAlignment.Left, -1, 10, tone.Lightened(0.35f));
                continue;
            }
            if (CityLayout.BarrioAt(npc.Position.X, npc.Position.Z) is { } home)
            {
                // Jefe de familia: rombo grande con el color del barrio.
                var accent = new Color(home.Accent.R, home.Accent.G, home.Accent.B);
                DrawColoredPolygon([at + new Vector2(0, -8), at + new Vector2(8, 0), at + new Vector2(0, 8), at + new Vector2(-8, 0)], accent);
                DrawString(font, at + new Vector2(10, 5), npc.Name, HorizontalAlignment.Left, -1, 13, accent.Lightened(0.3f));
                continue;
            }
            DrawRect(new Rect2(at - new Vector2(3, 3), new Vector2(6, 6)), Brass);
            DrawString(font, at + new Vector2(5, 4), npc.Name, HorizontalAlignment.Left, -1, 9, Brass);
        }

        var me = Multiplayer.MultiplayerPeer is null ? 0 : Multiplayer.GetUniqueId();
        foreach (var m in _map.Markers.Where(m => m.Peer != me))
        {
            var at = ToMap(m.X, m.Z);
            DrawCircle(at, 4, Others);
            DrawString(font, at + new Vector2(6, -6), m.Name, HorizontalAlignment.Left, -1, 11, Others);
        }

        if (_network.LocalPlayer is { } local)
        {
            var p = local.VisualPosition;
            DrawCircle(ToMap(p.X, p.Z), 6, Self);
            DrawString(font, ToMap(p.X, p.Z) + new Vector2(8, -8), "Vos", HorizontalAlignment.Left, -1, 12, Self);
        }
    }
}
