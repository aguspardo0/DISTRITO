using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Map;

/// <summary>Mapa del barrio (tecla M): edificios, lugares y todos los jugadores conectados.</summary>
public partial class MapPanel : Control
{
    private const float SizePx = 420f;
    private static readonly Color Paper = new(0.12f, 0.11f, 0.1f, 0.98f);
    private static readonly Color Building = new(0.32f, 0.29f, 0.25f);
    private static readonly Color Brass = new(0.83f, 0.68f, 0.38f);
    private static readonly Color Others = new(0.85f, 0.85f, 0.8f);
    private static readonly Color Self = new(0.95f, 0.35f, 0.25f);

    private GameNetwork _network = null!;
    private MapNetwork _map = null!;

    public void Initialize(GameNetwork network, MapNetwork map)
    {
        _network = network;
        _map = map;
        _map.Updated += QueueRedraw;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(SizePx, SizePx + 24);
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
        new((x + MapPresence.HalfExtent) / (2 * MapPresence.HalfExtent) * SizePx,
            24 + (z + MapPresence.HalfExtent) / (2 * MapPresence.HalfExtent) * SizePx);

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, SizePx, SizePx + 24), Paper);
        DrawRect(new Rect2(0, 24, SizePx, SizePx), Brass, filled: false, width: 2);
        DrawString(font, new Vector2(8, 17), $"BELLMONT — {_map.Markers.Count} conectado(s) · [M] cerrar",
            HorizontalAlignment.Left, -1, 14, Brass);

        var scale = SizePx / (2 * MapPresence.HalfExtent);
        foreach (var b in MapPresence.Buildings)
        {
            var corner = ToMap(b.X - b.Width / 2, b.Z - b.Depth / 2);
            DrawRect(new Rect2(corner, new Vector2(b.Width * scale, b.Depth * scale)), Building);
        }

        foreach (var npc in NpcInteractionRules.All)
        {
            var at = ToMap(npc.Position.X, npc.Position.Z);
            DrawRect(new Rect2(at - new Vector2(4, 4), new Vector2(8, 8)), Brass);
            DrawString(font, at + new Vector2(7, 4), npc.Name, HorizontalAlignment.Left, -1, 11, Brass);
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
