using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Construye Los Muelles (zona libre en el borde este de la ciudad, contra el agua) a partir de MapPresence.DocksBuildings, igual en el servidor
/// y en los clientes: piso, calle, galpones, pilas de cajones, muelle, agua y las paredes del borde.
/// </summary>
public static class DocksBuilder
{
    public static void Build(Node3D world)
    {
        var docks = new Node3D { Name = "Docks" };
        world.AddChild(docks);
        var area = CityLayout.Docks;
        var width = area.Width;
        var centerX = area.CenterX;
        var depth = area.Depth;

        Solid(docks, "Ground", new Vector3(width, 1, depth), new Vector3(centerX, -0.5f, 0), new Color(0.27f, 0.27f, 0.26f));
        Visual(docks, "Street", new Vector3(width, 0.02f, 6), new Vector3(centerX, 0.01f, 0), new Color(0.17f, 0.17f, 0.18f));
        // Tablones del muelle sobre el borde y agua más allá de la pared.
        Visual(docks, "Pier", new Vector3(3, 0.06f, depth), new Vector3(area.MaxX - 1.5f, 0.03f, 0), new Color(0.36f, 0.27f, 0.18f));
        // El agua: una bahía grande del lado este de toda la ciudad (los muelles quedan contra el borde).
        Visual(docks, "Water", new Vector3(400, 0.02f, 700), new Vector3(area.MaxX + 200, -0.35f, 0), new Color(0.12f, 0.2f, 0.26f));

        var i = 0;
        foreach (var b in MapPresence.DocksBuildings)
        {
            var shed = Distrito.Shared.Crews.CrewBaseRules.Sheds.ToList().FindIndex(s => s.X == b.X && s.Z == b.Z);
            if (shed >= 0)
            {
                BuildHollowShed(docks, shed, b, i % 2 == 0 ? new Color(0.38f, 0.24f, 0.2f) : new Color(0.33f, 0.33f, 0.3f));
                i++;
                continue;
            }
            var crates = b.Width < 4 && b.Depth < 4;
            var height = crates ? 1.6f : 5.5f + i % 3;
            var color = crates ? new Color(0.48f, 0.36f, 0.22f) : (i % 2 == 0 ? new Color(0.38f, 0.24f, 0.2f) : new Color(0.33f, 0.33f, 0.3f));
            Solid(docks, crates ? $"Crates{i}" : $"Shed{i}", new Vector3(b.Width, height, b.Depth), new Vector3(b.X, height / 2, b.Z), color);
            i++;
        }

        // Las paredes del borde las pone CityBuilder para toda la ciudad (CityLayout.Edges).

        docks.AddChild(new Label3D
        {
            Name = "Sign",
            Text = "LOS MUELLES\nZona libre: acá se pelea y se roba sin protección",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48, OutlineSize = 10, Modulate = new Color(0.95f, 0.55f, 0.4f),
            Position = new Vector3(area.MinX + 1.5f, 3.2f, -4),
        });
    }

    /// <summary>Casino «El Trébol»: fachada verde con marquesina y luces cálidas (sin interior: se juega en la puerta).</summary>
    public static void BuildCasino(Node3D world)
    {
        var b = MapPresence.Casino;
        var node = new Node3D { Name = "Casino", Position = new Vector3(b.X, 0, b.Z) };
        world.AddChild(node);
        const float h = 7f;
        Solid(node, "Building", new Vector3(b.Width, h, b.Depth), new Vector3(0, h / 2, 0), new Color(0.16f, 0.24f, 0.18f));
        var front = -b.Depth / 2;
        Visual(node, "Door", new Vector3(2.4f, 3f, 0.1f), new Vector3(0, 1.5f, front - 0.05f), new Color(0.35f, 0.22f, 0.1f));
        Visual(node, "Marquee", new Vector3(b.Width * 0.8f, 0.9f, 0.4f), new Vector3(0, 4.2f, front - 0.25f), new Color(0.1f, 0.1f, 0.1f));
        for (var i = 0; i < 12; i++)
            node.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.35f), EmissionEnergyMultiplier = 2f },
                Position = new Vector3(-b.Width * 0.38f + i * b.Width * 0.76f / 11, 3.65f, front - 0.45f),
            });
        node.AddChild(new Label3D
        {
            Text = "♣ EL TRÉBOL ♣", FontSize = 96, OutlineSize = 12, Modulate = new Color(0.55f, 0.95f, 0.55f),
            Position = new Vector3(0, 4.25f, front - 0.5f), Rotation = new Vector3(0, Mathf.Pi, 0),
        });
        node.AddChild(new OmniLight3D { Position = new Vector3(0, 3.5f, front - 2f), LightColor = new Color(1f, 0.85f, 0.5f), LightEnergy = 2f, OmniRange = 8f });
    }

    public const float ShedHeight = 5.5f;
    public const float DoorWidth = 3.6f;

    /// <summary>
    /// Galpón hueco que puede ser sede de cuadrilla: paredes, techo y un portón abierto hacia la calle (entran personas y
    /// vehículos). Adentro hay una lamparita y el cartel de la puerta lo actualiza BaseNetwork (dueño o «se vende»).
    /// </summary>
    private static void BuildHollowShed(Node3D parent, int shed, MapBuilding b, Color color)
    {
        var node = new Node3D { Name = $"Shed{shed}", Position = new Vector3(b.X, 0, b.Z) };
        parent.AddChild(node);
        const float t = 0.3f;
        var side = Distrito.Shared.Crews.CrewBaseRules.DoorSide(shed); // +1: la puerta mira a +z
        float w = b.Width, d = b.Depth, h = ShedHeight;
        Solid(node, "Back", new Vector3(w, h, t), new Vector3(0, h / 2, -side * (d / 2 - t / 2)), color);
        Solid(node, "Left", new Vector3(t, h, d), new Vector3(-w / 2 + t / 2, h / 2, 0), color);
        Solid(node, "Right", new Vector3(t, h, d), new Vector3(w / 2 - t / 2, h / 2, 0), color);
        var front = (w - DoorWidth) / 2;
        Solid(node, "FrontL", new Vector3(front, h, t), new Vector3(-w / 2 + front / 2, h / 2, side * (d / 2 - t / 2)), color);
        Solid(node, "FrontR", new Vector3(front, h, t), new Vector3(w / 2 - front / 2, h / 2, side * (d / 2 - t / 2)), color);
        Solid(node, "Lintel", new Vector3(DoorWidth, 1.5f, t), new Vector3(0, h - 0.75f, side * (d / 2 - t / 2)), color);
        Solid(node, "Roof", new Vector3(w, 0.25f, d), new Vector3(0, h + 0.12f, 0), color.Darkened(0.3f));
        Visual(node, "Floor", new Vector3(w - 2 * t, 0.03f, d - 2 * t), new Vector3(0, 0.02f, 0), new Color(0.3f, 0.27f, 0.23f));
        node.AddChild(new OmniLight3D { Position = new Vector3(0, h - 0.8f, 0), LightColor = new Color(1f, 0.82f, 0.55f), LightEnergy = 1.6f, OmniRange = Mathf.Max(w, d) });
        node.AddChild(new Label3D
        {
            Name = "Sign", Text = "SE VENDE\nSede de cuadrilla — $250",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, OutlineSize = 8,
            Modulate = new Color(0.9f, 0.85f, 0.7f), Position = new Vector3(0, h + 0.9f, side * (d / 2 + 0.3f)),
        });
    }

    /// <summary>Cambia el cartel de un galpón (lo llama BaseNetwork en todos los equipos).</summary>
    public static void SetShedSign(Node world, int shed, string text, Color color)
    {
        if (world.GetNodeOrNull<Label3D>($"Docks/Shed{shed}/Sign") is { } sign)
        {
            sign.Text = text;
            sign.Modulate = color;
        }
    }

    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color };

    private static void Visual(Node3D parent, string name, Vector3 size, Vector3 position, Color color) =>
        parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color), Position = position });

    private static void Solid(Node3D parent, string name, Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

}
