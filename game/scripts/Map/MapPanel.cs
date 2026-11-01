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
        var economy = Economy?.Invoke();
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
            DrawRect(AreaRect(b.Area), Accent(b) with { A = 0.16f });

        foreach (var b in MapPresence.Buildings)
        {
            var corner = ToMap(b.X - b.Width / 2, b.Z - b.Depth / 2);
            DrawRect(new Rect2(corner, new Vector2(b.Width * MapScale, b.Depth * MapScale)), Building);
        }

        // Nombres de los distritos, encima de los edificios y sobre una franja oscura para que se lean.
        foreach (var b in CityLayout.Barrios)
        {
            var rect = AreaRect(b.Area);
            DrawRect(rect, Accent(b) with { A = 0.75f }, filled: false, width: 2);
            Plate(font, rect.Position + new Vector2(4, 4), b.Area.Name.ToUpperInvariant(), 13, Accent(b).Lightened(0.3f));
        }
        Plate(font, ToMap(CityLayout.Market.MinX, CityLayout.Market.MaxZ) + new Vector2(2, -17), "EL MERCADO", 11, Brass);
        Plate(font, ToMap(CityLayout.Docks.MinX, CityLayout.Docks.MaxZ) + new Vector2(2, -17), "LOS MUELLES", 11, new Color(0.95f, 0.55f, 0.4f));

        // Sedes de cuadrilla en Los Muelles.
        foreach (var b in Distrito.Game.Crews.BaseNetwork.Bases)
        {
            var s = Distrito.Shared.Crews.CrewBaseRules.Sheds[b.Shed];
            DrawString(font, ToMap(s.X - s.Width / 2, s.Z), $"«{b.CrewName}»", HorizontalAlignment.Left, s.Width * MapScale, 9, Brass);
        }

        // Lugares: los comercios del Mercado van numerados (la lista está en las referencias); la gente de cada familia
        // con un punto de su color y el capo con un rombo.
        var territory = economy?.Territory;
        var number = 0;
        foreach (var npc in NpcInteractionRules.All)
        {
            var at = ToMap(npc.Position.X, npc.Position.Z);
            if (CityLayout.BarrioAt(npc.Position.X, npc.Position.Z) is { } barrio)
            {
                var tone = Accent(barrio);
                if (NpcInteractionRules.Bosses.Contains(npc))
                {
                    DrawColoredPolygon([at + new Vector2(0, -8), at + new Vector2(8, 0), at + new Vector2(0, 8), at + new Vector2(-8, 0)], tone);
                    DrawPolyline([at + new Vector2(0, -8), at + new Vector2(8, 0), at + new Vector2(0, 8), at + new Vector2(-8, 0), at + new Vector2(0, -8)], Paper, 1.5f);
                    Plate(font, at + new Vector2(11, -9), npc.Name, 13, tone.Lightened(0.35f));
                }
                else
                {
                    DrawCircle(at, 4.5f, tone);
                    Plate(font, at + new Vector2(7, -7), npc.Name, 10, tone.Lightened(0.4f));
                }
                continue;
            }
            number++;
            // Territorio: un aro del color de la cuadrilla que controla la zona del local.
            if (Distrito.Shared.Economy.TerritoryRules.ZoneOfNpc(npc.Code) is { } zoneCode && territory?.Zone(zoneCode) is { ControllerCrew: { } owner })
                DrawArc(at, 9, 0, Mathf.Tau, 24, CrewColor(owner), 2.5f);
            DrawCircle(at, 6, Brass);
            DrawString(font, at + new Vector2(-3.5f, 4), number.ToString(), HorizontalAlignment.Left, -1, 10, Paper);
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
            DrawArc(ToMap(p.X, p.Z), 8, 0, Mathf.Tau, 20, Paper, 1.5f);
            Plate(font, ToMap(p.X, p.Z) + new Vector2(9, -18), "Vos", 12, Self);
        }

        DrawLegend(font, territory);

        if (economy?.Families is { } families)
        {
            var line = string.Join("   ", families.Select(f => $"{f.Name.Replace("Familia ", "")}: {Distrito.Shared.Economy.FamilyRules.Display(f.Mood).Split(' ')[0]}"));
            DrawString(font, new Vector2(8, HeightPx + 41), line, HorizontalAlignment.Left, WidthPx - 16, 13, Brass);
        }
        DrawRect(new Rect2(0, 24, WidthPx, HeightPx), Brass, filled: false, width: 2);
        DrawString(font, new Vector2(8, 17), $"BELLMONT — {_map.Markers.Count} conectado(s) · [M] cerrar", HorizontalAlignment.Left, -1, 14, Brass);
    }

    /// <summary>Referencias en la esquina vacía de arriba a la izquierda: símbolos y la lista numerada del Mercado.</summary>
    private void DrawLegend(Font font, Distrito.Shared.Economy.TerritoryView? territory)
    {
        var origin = new Vector2(10, 34);
        var y = origin.Y;
        DrawString(font, new Vector2(origin.X, y + 11), "REFERENCIAS", HorizontalAlignment.Left, -1, 12, Brass);
        y += 22;
        var accent = Accent(CityLayout.Moretti);
        DrawColoredPolygon([new Vector2(origin.X + 5, y - 5), new Vector2(origin.X + 10, y), new Vector2(origin.X + 5, y + 5), new Vector2(origin.X, y)], accent);
        DrawString(font, new Vector2(origin.X + 16, y + 4), $"Capo (recibe desde {Distrito.Shared.Economy.FamilyPeople.BossReputation} de reputación)", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawCircle(new Vector2(origin.X + 5, y), 4, accent);
        DrawString(font, new Vector2(origin.X + 16, y + 4), "Su gente: con ellos se empieza", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawRect(new Rect2(origin.X, y - 5, 10, 10), FreeZone with { A = 0.9f });
        DrawString(font, new Vector2(origin.X + 16, y + 4), "Zona libre: PvP sin protección", HorizontalAlignment.Left, -1, 10, Others);
        y += 20;
        DrawString(font, new Vector2(origin.X, y + 4), "El Mercado:", HorizontalAlignment.Left, -1, 11, Brass);
        y += 16;
        var number = 0;
        foreach (var npc in NpcInteractionRules.Market)
        {
            number++;
            DrawCircle(new Vector2(origin.X + 5, y), 6, Brass);
            DrawString(font, new Vector2(origin.X + 1.5f, y + 4), number.ToString(), HorizontalAlignment.Left, -1, 10, Paper);
            var owner = Distrito.Shared.Economy.TerritoryRules.ZoneOfNpc(npc.Code) is { } zone && territory?.Zone(zone) is { ControllerCrew: { } crew } ? crew : null;
            DrawString(font, new Vector2(origin.X + 16, y + 4), npc.Name, HorizontalAlignment.Left, -1, 10, Others);
            if (owner is not null)
                DrawString(font, new Vector2(origin.X + 130, y + 4), $"«{owner}»", HorizontalAlignment.Left, -1, 10, CrewColor(owner));
            y += 15;
        }
    }

    /// <summary>Texto sobre una franja oscura (para que se lea encima de edificios y colores).</summary>
    private void Plate(Font font, Vector2 topLeft, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2(topLeft, new Vector2(width + 8, size + 6)), Paper with { A = 0.82f });
        DrawString(font, topLeft + new Vector2(4, size + 1), text, HorizontalAlignment.Left, -1, size, color);
    }

    private static Rect2 AreaRect(District d) => new(ToMap(d.MinX, d.MinZ), new Vector2(d.Width * MapScale, d.Depth * MapScale));

    private static Color Accent(BarrioPlan b) => new(b.Accent.R, b.Accent.G, b.Accent.B);

    private static Color CrewColor(string crew) => Color.FromHsv(crew.Sum(ch => ch) * 37 % 360 / 360f, 0.6f, 0.9f); // igual en todos los equipos
}
