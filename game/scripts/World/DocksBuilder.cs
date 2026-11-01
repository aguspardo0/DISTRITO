using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Construye Los Muelles (zona libre al este del barrio) a partir de MapPresence.DocksBuildings, igual en el servidor
/// y en los clientes: piso, calle, galpones, pilas de cajones, muelle, agua y las paredes del borde.
/// </summary>
public static class DocksBuilder
{
    public static void Build(Node3D world)
    {
        var docks = new Node3D { Name = "Docks" };
        world.AddChild(docks);
        var width = MapPresence.MaxX - MapPresence.DocksStartX;
        var centerX = MapPresence.DocksStartX + width / 2;
        var depth = MapPresence.MaxZ - MapPresence.MinZ;

        Solid(docks, "Ground", new Vector3(width, 1, depth), new Vector3(centerX, -0.5f, 0), new Color(0.27f, 0.27f, 0.26f));
        Visual(docks, "Street", new Vector3(width, 0.02f, 6), new Vector3(centerX, 0.01f, 0), new Color(0.17f, 0.17f, 0.18f));
        // Tablones del muelle sobre el borde y agua más allá de la pared.
        Visual(docks, "Pier", new Vector3(3, 0.06f, depth), new Vector3(MapPresence.MaxX - 1.5f, 0.03f, 0), new Color(0.36f, 0.27f, 0.18f));
        Visual(docks, "Water", new Vector3(40, 0.02f, depth + 40), new Vector3(MapPresence.MaxX + 20, -0.35f, 0), new Color(0.12f, 0.2f, 0.26f));

        var i = 0;
        foreach (var b in MapPresence.DocksBuildings)
        {
            var crates = b.Width < 4 && b.Depth < 4;
            var height = crates ? 1.6f : 5.5f + i % 3;
            var color = crates ? new Color(0.48f, 0.36f, 0.22f) : (i % 2 == 0 ? new Color(0.38f, 0.24f, 0.2f) : new Color(0.33f, 0.33f, 0.3f));
            Solid(docks, crates ? $"Crates{i}" : $"Shed{i}", new Vector3(b.Width, height, b.Depth), new Vector3(b.X, height / 2, b.Z), color);
            i++;
        }

        // Paredes del borde (las del barrio original vienen en Main.tscn; la del este quedó en el muelle).
        Wall(docks, "EdgeDocksNorth", new Vector3(width + 1, 10, 1), new Vector3(centerX + 0.5f, 5, MapPresence.MinZ - 0.5f));
        Wall(docks, "EdgeDocksSouth", new Vector3(width + 1, 10, 1), new Vector3(centerX + 0.5f, 5, MapPresence.MaxZ + 0.5f));

        docks.AddChild(new Label3D
        {
            Name = "Sign",
            Text = "LOS MUELLES\nZona libre: acá se pelea y se roba sin protección",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48, OutlineSize = 10, Modulate = new Color(0.95f, 0.55f, 0.4f),
            Position = new Vector3(MapPresence.DocksStartX + 1.5f, 3.2f, -4),
        });
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

    private static void Wall(Node3D parent, string name, Vector3 size, Vector3 position)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }
}
