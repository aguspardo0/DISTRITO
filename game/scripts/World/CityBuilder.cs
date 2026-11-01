using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Construye la ciudad que rodea al Mercado a partir de CityLayout, igual en el servidor y en los clientes:
/// las tres avenidas, el barrio de cada familia (suelo, calles, manzanas, plaza, cuartel, faroles y carteles)
/// y las paredes invisibles de todo el borde. El Mercado (Main.tscn) y Los Muelles (DocksBuilder) ya existían.
/// </summary>
public static class CityBuilder
{
    private static readonly Color Cobble = new(0.17f, 0.17f, 0.18f);
    private static readonly Color Sidewalk = new(0.33f, 0.32f, 0.3f);

    public static void Build(Node3D world)
    {
        var city = new Node3D { Name = "City" };
        world.AddChild(city);
        foreach (var avenue in new[] { CityLayout.WestAvenue, CityLayout.NorthAvenue, CityLayout.SouthAvenue })
            BuildAvenue(city, avenue);
        foreach (var barrio in CityLayout.Barrios)
            BuildBarrio(city, barrio);
        BuildEdges(city);
    }

    private static void BuildAvenue(Node3D city, District a)
    {
        var node = new Node3D { Name = a.Code };
        city.AddChild(node);
        Solid(node, "Ground", new Vector3(a.Width, 1, a.Depth), new Vector3(a.CenterX, -0.5f, a.CenterZ), Sidewalk);
        var alongX = a.Width > a.Depth;
        var road = alongX ? new Vector3(a.Width, 0.02f, a.Depth - 3) : new Vector3(a.Width - 3, 0.02f, a.Depth);
        Visual(node, "Road", road, new Vector3(a.CenterX, 0.01f, a.CenterZ), Cobble);
    }

    private static void BuildBarrio(Node3D city, BarrioPlan b)
    {
        var area = b.Area;
        var node = new Node3D { Name = area.Code };
        city.AddChild(node);
        var accent = new Color(b.Accent.R, b.Accent.G, b.Accent.B);
        var baseTone = new Color(b.Palette[0].R, b.Palette[0].G, b.Palette[0].B);

        // Suelo y calles: las dos calles a lo largo, las dos a lo ancho y el eje central (bulevar y plaza) empedrados.
        Solid(node, "Ground", new Vector3(area.Width, 1, area.Depth), new Vector3(area.CenterX, -0.5f, area.CenterZ), Sidewalk.Lerp(baseTone, 0.12f));
        PaveLocal(node, b, "StreetU1", 24, 38, -50, 50);
        PaveLocal(node, b, "StreetU2", 62, 76, -50, 50);
        PaveLocal(node, b, "StreetV1", 0, 100, -26, -12);
        PaveLocal(node, b, "StreetV2", 0, 100, 12, 26);
        PaveLocal(node, b, "Boulevard", 0, 76, -5, 5);
        var plaza = b.Rect(38, 62, -12, 12);
        Visual(node, "Plaza", new Vector3(plaza.Width, 0.025f, plaza.Depth), new Vector3(plaza.X, 0.015f, plaza.Z), baseTone.Lerp(Sidewalk, 0.6f));

        // Manzanas: edificios con cornisa; el cuartel con puerta, estandartes, nombre y luces.
        var i = 0;
        foreach (var building in CityLayout.BuildingsOf(b))
        {
            var plan = building.Plan;
            var tone = b.Palette[building.Tone];
            var color = new Color(tone.R, tone.G, tone.B);
            var size = new Vector3(plan.Width, building.Height, plan.Depth);
            Solid(node, building.Headquarters ? "Headquarters" : $"Building{i++}", size, new Vector3(plan.X, building.Height / 2, plan.Z), color);
            Visual(node, $"Cornice{i}", new Vector3(plan.Width + 0.4f, 0.35f, plan.Depth + 0.4f), new Vector3(plan.X, building.Height + 0.17f, plan.Z), color.Darkened(0.35f));
            if (building.Headquarters)
                DecorateHeadquarters(node, b, building, accent);
        }

        BuildLandmark(node, b, accent);

        // Faroles: poste y farol cálido; luz real cada dos (alcanza para la noche y no pesa).
        var lamp = 0;
        foreach (var spot in CityLayout.LampsOf(b))
        {
            node.AddChild(new MeshInstance3D
            {
                Name = $"LampPost{lamp}", Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 3.4f },
                MaterialOverride = Material(new Color(0.08f, 0.08f, 0.08f)), Position = new Vector3(spot.X, 1.7f, spot.Y),
            });
            node.AddChild(new MeshInstance3D
            {
                Name = $"Lamp{lamp}", Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
                MaterialOverride = Glow(new Color(1f, 0.82f, 0.5f), 2.2f), Position = new Vector3(spot.X, 3.5f, spot.Y),
            });
            if (lamp % 2 == 0)
                node.AddChild(new OmniLight3D { Position = new Vector3(spot.X, 3.3f, spot.Y), LightColor = new Color(1f, 0.8f, 0.5f), LightEnergy = 1.4f, OmniRange = 11f });
            lamp++;
        }

        // Cartel de bienvenida en la entrada (se lee al llegar por la avenida).
        var gate = b.ToWorld(2, 0);
        node.AddChild(new Label3D
        {
            Name = "WelcomeSign", Text = $"{area.Name.ToUpperInvariant()}\n{b.Motto}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, OutlineSize = 12,
            Modulate = accent.Lightened(0.2f), Position = new Vector3(gate.X, 4.6f, gate.Y),
        });
        // Arco de entrada: dos columnas y un dintel del color de la familia.
        foreach (var side in new[] { -6.5f, 6.5f })
        {
            var p = b.ToWorld(1.5f, side);
            Solid(node, $"GatePillar{side}", new Vector3(0.8f, 5.2f, 0.8f), new Vector3(p.X, 2.6f, p.Y), baseTone.Darkened(0.2f));
        }
        var lintel = b.Rect(1.1f, 1.9f, -6.9f, 6.9f);
        Visual(node, "GateLintel", new Vector3(lintel.Width, 0.6f, lintel.Depth), new Vector3(lintel.X, 5.5f, lintel.Z), accent.Darkened(0.2f));

        // En el Mercado, un cartel indica para dónde queda cada barrio.
        var avenue = b.Avenue;
        var back = b.Inward.X != 0 ? avenue.Width - 2 : avenue.Depth - 2; // hacia el Mercado, casi al final de la avenida
        var sign = new Vector2(b.Entrance.X - b.Inward.X * back, b.Entrance.Y - b.Inward.Y * back);
        city.AddChild(new Label3D
        {
            Name = $"Signpost_{area.Code}", Text = $"→ {area.Name}\n{b.Boss}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, OutlineSize = 10,
            Modulate = accent.Lightened(0.3f), Position = new Vector3(sign.X, 3.4f, sign.Y),
        });
    }

    /// <summary>Puerta hacia la plaza, dos estandartes con el color de la familia, el nombre del cuartel y faroles.</summary>
    private static void DecorateHeadquarters(Node3D node, BarrioPlan b, CityBuilding hq, Color accent)
    {
        var front = b.ToWorld(CityLayout.HqFront - 0.06f, 0);
        var facing = Mathf.Atan2(-b.Inward.X, -b.Inward.Y); // la fachada mira hacia la entrada del barrio
        var door = new Node3D { Name = "HqFacade", Position = new Vector3(front.X, 0, front.Y), Rotation = new Vector3(0, facing, 0) };
        node.AddChild(door);
        Visual(door, "Door", new Vector3(2.8f, 3.4f, 0.12f), new Vector3(0, 1.7f, 0), new Color(0.24f, 0.15f, 0.08f));
        Visual(door, "Steps", new Vector3(5f, 0.25f, 1.4f), new Vector3(0, 0.12f, 0.7f), new Color(0.45f, 0.43f, 0.4f));
        foreach (var x in new[] { -5.5f, 5.5f })
        {
            Visual(door, $"Banner{x}", new Vector3(1.4f, 5f, 0.08f), new Vector3(x, 6f, 0.05f), accent);
            door.AddChild(new OmniLight3D { Position = new Vector3(x * 0.55f, 3.6f, 0.8f), LightColor = new Color(1f, 0.82f, 0.55f), LightEnergy = 1.8f, OmniRange = 7f });
        }
        // Guardaespaldas a un costado de la puerta (decorativo: el que decide si pasás es el diálogo del capo).
        foreach (var x in new[] { -2.4f, 2.4f })
        {
            door.AddChild(new MeshInstance3D
            {
                Name = $"Bodyguard{x}", Mesh = new CapsuleMesh { Radius = 0.42f, Height = 1.9f },
                MaterialOverride = Material(new Color(0.09f, 0.09f, 0.1f)), Position = new Vector3(x, 0.95f, 1.6f),
            });
            door.AddChild(new MeshInstance3D
            {
                Name = $"BodyguardHat{x}", Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.18f },
                MaterialOverride = Material(new Color(0.05f, 0.05f, 0.05f)), Position = new Vector3(x, 1.95f, 1.6f),
            });
        }
        door.AddChild(new Label3D
        {
            Text = b.Title, FontSize = 110, OutlineSize = 14, Modulate = accent.Lightened(0.35f),
            Position = new Vector3(0, hq.Height - 2.2f, 0.12f),
        });
    }

    /// <summary>Lo que distingue a cada plaza: la fuente de los Moretti, el obelisco de los Valenti, el monumento al trabajo de los Caruso.</summary>
    private static void BuildLandmark(Node3D node, BarrioPlan b, Color accent)
    {
        var c = b.ToWorld(50, 0);
        var at = new Vector3(c.X, 0, c.Y);
        switch (b.Family)
        {
            case Distrito.Shared.Economy.EconomyCatalog.Moretti:
                SolidCylinder(node, "Fountain", 2.4f, 0.8f, at + new Vector3(0, 0.4f, 0), new Color(0.55f, 0.52f, 0.48f));
                Visual(node, "FountainWater", new Vector3(3.6f, 0.05f, 3.6f), at + new Vector3(0, 0.82f, 0), new Color(0.2f, 0.35f, 0.42f));
                SolidCylinder(node, "FountainColumn", 0.35f, 2.2f, at + new Vector3(0, 1.1f, 0), new Color(0.6f, 0.57f, 0.52f));
                break;
            case Distrito.Shared.Economy.EconomyCatalog.Valenti:
                Solid(node, "ObeliskBase", new Vector3(3f, 0.8f, 3f), at + new Vector3(0, 0.4f, 0), new Color(0.3f, 0.3f, 0.32f));
                Solid(node, "Obelisk", new Vector3(1.1f, 7f, 1.1f), at + new Vector3(0, 4.3f, 0), new Color(0.78f, 0.74f, 0.64f));
                node.AddChild(new MeshInstance3D { Name = "ObeliskTop", Mesh = new SphereMesh { Radius = 0.45f, Height = 0.9f }, MaterialOverride = Glow(accent, 0.6f), Position = at + new Vector3(0, 8.1f, 0) });
                break;
            default:
                Solid(node, "MonumentBase", new Vector3(3.2f, 0.6f, 2.2f), at + new Vector3(0, 0.3f, 0), new Color(0.3f, 0.3f, 0.3f));
                Solid(node, "Anvil", new Vector3(2f, 1f, 0.9f), at + new Vector3(0, 1.1f, 0), new Color(0.18f, 0.18f, 0.2f));
                Solid(node, "Crates", new Vector3(1.2f, 1.2f, 1.2f), at + new Vector3(2.8f, 0.6f, 1.2f), new Color(0.48f, 0.36f, 0.22f));
                Solid(node, "Crates2", new Vector3(1.2f, 1.2f, 1.2f), at + new Vector3(-2.6f, 0.6f, -1f), new Color(0.44f, 0.33f, 0.2f));
                break;
        }
        node.AddChild(new OmniLight3D { Position = at + new Vector3(0, 4f, 0), LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.5f, OmniRange = 12f });
    }

    /// <summary>Paredes invisibles en todo el borde de la ciudad, del lado de afuera de la línea.</summary>
    private static void BuildEdges(Node3D city)
    {
        var edges = new Node3D { Name = "Edges" };
        city.AddChild(edges);
        var i = 0;
        foreach (var e in CityLayout.Edges())
        {
            var mx = (e.X1 + e.X2) / 2;
            var mz = (e.Z1 + e.Z2) / 2;
            // Para qué lado queda afuera: el que no es caminable a medio metro.
            float ox = 0, oz = 0;
            if (e.Horizontal)
                oz = CityLayout.IsWalkable(mx, mz - 0.5f) ? 0.5f : -0.5f;
            else
                ox = CityLayout.IsWalkable(mx - 0.5f, mz) ? 0.5f : -0.5f;
            var size = e.Horizontal ? new Vector3(e.Length, 10, 1) : new Vector3(1, 10, e.Length);
            var body = new StaticBody3D { Name = $"Edge{i++}", Position = new Vector3(mx + ox, 5, mz + oz) };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            edges.AddChild(body);
        }
    }

    private static void PaveLocal(Node3D node, BarrioPlan b, string name, float u1, float u2, float v1, float v2)
    {
        var r = b.Rect(u1, u2, v1, v2);
        Visual(node, name, new Vector3(r.Width, 0.02f, r.Depth), new Vector3(r.X, 0.01f, r.Z), Cobble);
    }

    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color };

    private static StandardMaterial3D Glow(Color color, float energy) =>
        new() { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = energy };

    private static void Visual(Node3D parent, string name, Vector3 size, Vector3 position, Color color) =>
        parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color), Position = position });

    private static void Solid(Node3D parent, string name, Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    private static void SolidCylinder(Node3D parent, string name, float radius, float height, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height }, MaterialOverride = Material(color) });
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = height } });
        parent.AddChild(body);
    }
}
