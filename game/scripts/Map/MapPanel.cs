using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Map;

/// <summary>Mapa de Bellmont (tecla M): barrios, edificios, lugares y todos los jugadores conectados.
/// Rueda del mouse: acercar o alejar (hacia donde apunta el mouse); arrastrar: mover; Espacio: centrar en vos.</summary>
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

    private const float MinZoom = 1f, MaxZoom = 6f;
    private const float LegendWidth = 290f; // las referencias van en una columna a la derecha del mapa

    private GameNetwork _network = null!;
    private float _zoom = 1f;
    private Vector2 _pan; // corrimiento del dibujo en píxeles
    private bool _dragging;

    /// <summary>Escala actual: metros a píxeles con el zoom.</summary>
    private float S => MapScale * _zoom;
    private MapNetwork _map = null!;

    /// <summary>Ánimo de las familias para mostrar al pie del mapa (lo trae la economía del jugador).</summary>
    public Func<Distrito.Shared.Economy.EconomySnapshot?>? Economy { get; set; }

    /// <summary>Destinos del tutorial (aros dorados).</summary>
    public Func<IEnumerable<string>>? ExtraTargets { get; set; }

    public void Initialize(GameNetwork network, MapNetwork map)
    {
        _network = network;
        _map = map;
        _map.Updated += QueueRedraw;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(WidthPx + LegendWidth, HeightPx + 48);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_zoom > MinZoom)
                CenterOnMe(); // si lo habías acercado, vuelve a abrir mirando dónde estás
        }
        else if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAt(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                _dragging = click.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                _pan += motion.Relative;
                Clamp();
                AcceptEvent();
                break;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Space })
        {
            FocusMe();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Acerca el mapa a donde estás (Espacio).</summary>
    public void FocusMe()
    {
        if (_zoom < 2.5f)
            _zoom = 2.5f;
        CenterOnMe();
        QueueRedraw();
    }

    /// <summary>Acerca o aleja dejando quieto el punto que está debajo del mouse.</summary>
    private void ZoomAt(Vector2 screen, float factor)
    {
        var zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var world = (screen - new Vector2(0, 24) - _pan) / _zoom;
        _zoom = zoom;
        _pan = screen - new Vector2(0, 24) - world * _zoom;
        Clamp();
    }

    private void CenterOnMe()
    {
        if (_network?.LocalPlayer is not { } me)
            return;
        var p = me.VisualPosition;
        var at = new Vector2((p.X - MapPresence.MinX) * S, (p.Z - MapPresence.MinZ) * S);
        _pan = new Vector2(WidthPx / 2, HeightPx / 2) - at;
        Clamp();
    }

    /// <summary>Que el mapa no se vaya del marco.</summary>
    private void Clamp()
    {
        var w = (MapPresence.MaxX - MapPresence.MinX) * S;
        var h = HeightPx * _zoom;
        _pan.X = w <= WidthPx ? 0 : Mathf.Clamp(_pan.X, WidthPx - w, 0);
        _pan.Y = Mathf.Clamp(_pan.Y, HeightPx - h, 0);
    }

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw(); // la posición propia se dibuja en vivo
    }

    private Vector2 ToMap(float x, float z) =>
        new Vector2((x - MapPresence.MinX) * S, 24 + (z - MapPresence.MinZ) * S) + _pan;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var economy = Economy?.Invoke();
        DrawRect(new Rect2(0, 0, WidthPx, HeightPx + 48), Paper);

        // Distritos: el Mercado (neutral), las avenidas, Los Muelles (zona libre, en rojo) y el barrio de cada familia con su color.
        // El Centro (fase 1): primero, las calles; los distritos de antes van encima.
        DrawRect(new Rect2(ToMap(CityLayout.Downtown.MinX, CityLayout.Downtown.MinZ),
            new Vector2(CityLayout.Downtown.Width * S, CityLayout.Downtown.Depth * S)), AvenueColor);
        foreach (var d in CityLayout.Districts)
        {
            var rect = new Rect2(ToMap(d.MinX, d.MinZ), new Vector2(d.Width * S, d.Depth * S));
            DrawRect(rect, d.Kind == DistrictKind.Avenue ? AvenueColor : Ground);
            if (d.Kind == DistrictKind.Docks)
                DrawRect(rect, FreeZone);
        }
        foreach (var b in CityLayout.Barrios)
            DrawRect(AreaRect(b.Area), Accent(b) with { A = 0.16f });

        foreach (var b in MapPresence.Buildings)
        {
            var corner = ToMap(b.X - b.Width / 2, b.Z - b.Depth / 2);
            DrawRect(new Rect2(corner, new Vector2(b.Width * S, b.Depth * S)), Building);
        }

        // Nombres de los distritos, encima de los edificios y sobre una franja oscura para que se lean.
        foreach (var b in CityLayout.Barrios)
        {
            var rect = AreaRect(b.Area);
            DrawRect(rect, Accent(b) with { A = 0.75f }, filled: false, width: 2);
            // El nombre va del lado opuesto a la entrada (donde está la gente de la familia).
            var corner = b.Inward.Y > 0 ? new Vector2(rect.Position.X + 4, rect.End.Y - 23) : rect.Position + new Vector2(4, 4);
            Plate(font, corner, b.Area.Name.ToUpperInvariant(), 13, Accent(b).Lightened(0.3f));
        }
        Plate(font, ToMap(CityLayout.Market.MinX, CityLayout.Market.MaxZ) + new Vector2(2, -17), "EL MERCADO", 11, Brass);
        Plate(font, ToMap(CityLayout.Downtown.MinX, CityLayout.Downtown.MinZ) + new Vector2(6, 6), "EL CENTRO", 13, Brass);
        // Locales vacíos del Centro: un cuadradito dorado (todavía no se alquilan).
        foreach (var shop in CityLayout.EmptyShops)
            DrawRect(new Rect2(ToMap(shop.X, shop.Z) - new Vector2(2, 2), new Vector2(4, 4)), Brass);
        Plate(font, ToMap(CityLayout.Docks.MinX, CityLayout.Docks.MaxZ) + new Vector2(2, -17), "LOS MUELLES", 11, new Color(0.95f, 0.55f, 0.4f));

        // Sedes de cuadrilla en Los Muelles.
        foreach (var b in Distrito.Game.Crews.BaseNetwork.Bases)
        {
            var s = Distrito.Shared.Crews.CrewBaseRules.Sheds[b.Shed];
            DrawString(font, ToMap(s.X - s.Width / 2, s.Z), $"«{b.CrewName}»", HorizontalAlignment.Left, s.Width * S, 9, Brass);
        }

        // Lugares: los comercios del Mercado van numerados (la lista está en las referencias); la gente de cada familia
        // con un punto de su color y el capo con un rombo.
        var territory = economy?.Territory;
        var number = 0;
        foreach (var npc in NpcInteractionRules.All)
        {
            var spot = npc.MapPosition; // si atiende adentro, en la puerta de su local
            var at = ToMap(spot.X, spot.Y);
            if (CityLayout.BarrioAt(spot.X, spot.Y) is { } barrio)
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

        // Encargos en curso: a dónde hay que ir ahora (aro del color de la familia y una línea desde tu posición).
        var pulse = 10 + 3 * Mathf.Sin(Time.GetTicksMsec() / 250f);
        foreach (var e in economy?.ActiveEncargos ?? [])
        {
            if (Distrito.Game.Economy.EncargoLook.PositionOf(e.NextNpc) is not { } target)
                continue;
            var color = Distrito.Game.Economy.EncargoLook.FamilyColor(e.Family);
            var at = ToMap(target.X, target.Z);
            DrawArc(at, pulse, 0, Mathf.Tau, 28, color, 3);
            DrawArc(at, pulse + 4, 0, Mathf.Tau, 28, color with { A = 0.35f }, 2);
            if (_network.LocalPlayer is { } walker)
                DrawDashedLine(ToMap(walker.VisualPosition.X, walker.VisualPosition.Z), at, color with { A = 0.8f }, 2, 6);
        }

        foreach (var code in ExtraTargets?.Invoke() ?? [])
            if (Distrito.Game.Economy.EncargoLook.PositionOf(code) is { } spot)
            {
                DrawArc(ToMap(spot.X, spot.Z), pulse, 0, Mathf.Tau, 28, Brass, 3);
                DrawArc(ToMap(spot.X, spot.Z), pulse + 4, 0, Mathf.Tau, 28, Brass with { A = 0.35f }, 2);
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
            // Una flecha que apunta hacia donde mira la cámara (con yaw 0 se mira a −Z, que en el mapa es arriba).
            var p = local.VisualPosition;
            var at = ToMap(p.X, p.Z);
            var f = GetViewport().GetCamera3D() is { } cam ? -cam.GlobalTransform.Basis.Z : new Vector3(0, 0, -1);
            var dir = new Vector2(f.X, f.Z);
            dir = dir.LengthSquared() < 1e-4f ? Vector2.Up : dir.Normalized();
            var side = new Vector2(-dir.Y, dir.X);
            Vector2[] arrow = [at + dir * 11, at - dir * 7 + side * 7, at - dir * 3, at - dir * 7 - side * 7];
            DrawColoredPolygon(arrow, Self);
            DrawPolyline([.. arrow, arrow[0]], Paper, 1.5f);
            Plate(font, at + new Vector2(11, -20), "Vos", 12, Self);
        }

        // Lo que se pasó del marco al acercar queda tapado por la cabecera y el pie.
        var full = new Vector2(Mathf.Max(Size.X, WidthPx), Mathf.Max(Size.Y, HeightPx + 48));
        DrawRect(new Rect2(0, 0, full.X, 24), Paper);
        DrawRect(new Rect2(0, 24 + HeightPx, full.X, full.Y - 24 - HeightPx), Paper);
        DrawRect(new Rect2(WidthPx, 0, full.X - WidthPx, full.Y), Paper);
        DrawLegend(font, territory);

        if (economy?.Families is { } families)
        {
            var line = string.Join("   ", families.Select(f => $"{f.Name.Replace("Familia ", "")}: {Distrito.Shared.Economy.FamilyRules.Display(f.Mood).Split(' ')[0]}"));
            DrawString(font, new Vector2(8, HeightPx + 41), line, HorizontalAlignment.Left, WidthPx + LegendWidth - 16, 13, Brass);
        }
        DrawRect(new Rect2(0, 24, WidthPx, HeightPx), Brass, filled: false, width: 2);
        DrawString(font, new Vector2(8, 17), $"BELLMONT — {_map.Markers.Count} conectado(s)", HorizontalAlignment.Left, -1, 14, Brass);
        DrawString(font, new Vector2(0, 17), "rueda: zoom · arrastrar: mover · Espacio: vos · M: cerrar", HorizontalAlignment.Right, WidthPx + LegendWidth - 8, 11, Others with { A = 0.7f });
    }

    /// <summary>Referencias en la esquina vacía de arriba a la izquierda: símbolos y la lista numerada del Mercado.</summary>
    private void DrawLegend(Font font, Distrito.Shared.Economy.TerritoryView? territory)
    {
        var origin = new Vector2(WidthPx + 12, 34);
        var y = origin.Y;

        DrawString(font, new Vector2(origin.X, y + 11), "REFERENCIAS", HorizontalAlignment.Left, -1, 12, Brass);
        y += 22;
        var accent = Accent(CityLayout.Moretti);
        DrawColoredPolygon([new Vector2(origin.X + 5, y - 5), new Vector2(origin.X + 10, y), new Vector2(origin.X + 5, y + 5), new Vector2(origin.X, y)], accent);
        DrawString(font, new Vector2(origin.X + 16, y + 4), $"Capo (recibe desde {Distrito.Shared.Economy.FamilyPeople.BossReputation} de reputación)", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawCircle(new Vector2(origin.X + 5, y), 4, accent);
        DrawString(font, new Vector2(origin.X + 16, y + 4), "Su gente: dan los encargos (L)", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawRect(new Rect2(origin.X, y - 5, 10, 10), FreeZone with { A = 0.9f });
        DrawString(font, new Vector2(origin.X + 16, y + 4), "Zona libre: PvP sin protección", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawArc(new Vector2(origin.X + 5, y), 5, 0, Mathf.Tau, 16, Self, 2);
        DrawString(font, new Vector2(origin.X + 16, y + 4), "A dónde ir por tus encargos", HorizontalAlignment.Left, -1, 10, Others);
        y += 16;
        DrawString(font, new Vector2(origin.X, y + 4), "Lugares:", HorizontalAlignment.Left, -1, 11, Brass);
        y += 16;
        var number = 0;
        foreach (var npc in NpcInteractionRules.Market.Append(NpcInteractionRules.DocksContact))
        {
            number++;
            DrawCircle(new Vector2(origin.X + 5, y), 5.5f, Brass);
            DrawString(font, new Vector2(origin.X + 1.5f, y + 4), number.ToString(), HorizontalAlignment.Left, -1, 10, Paper);
            var owner = Distrito.Shared.Economy.TerritoryRules.ZoneOfNpc(npc.Code) is { } zone && territory?.Zone(zone) is { ControllerCrew: { } crew } ? crew : null;
            DrawString(font, new Vector2(origin.X + 16, y + 4), npc.Name, HorizontalAlignment.Left, -1, 10, Others);
            if (owner is not null)
                DrawString(font, new Vector2(origin.X + 130, y + 4), $"«{owner}»", HorizontalAlignment.Left, -1, 10, CrewColor(owner));
            y += 12;
        }
    }

    /// <summary>Texto sobre una franja oscura (para que se lea encima de edificios y colores).</summary>
    private void Plate(Font font, Vector2 topLeft, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2(topLeft, new Vector2(width + 8, size + 6)), Paper with { A = 0.82f });
        DrawString(font, topLeft + new Vector2(4, size + 1), text, HorizontalAlignment.Left, -1, size, color);
    }

    private Rect2 AreaRect(District d) => new(ToMap(d.MinX, d.MinZ), new Vector2(d.Width * S, d.Depth * S));

    private static Color Accent(BarrioPlan b) => new(b.Accent.R, b.Accent.G, b.Accent.B);

    private static Color CrewColor(string crew) => Color.FromHsv(crew.Sum(ch => ch) * 37 % 360 / 360f, 0.6f, 0.9f); // igual en todos los equipos
}
