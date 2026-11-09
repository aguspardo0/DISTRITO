using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// La Estación de Bellmont y su barrio (ver <see cref="Station"/>): el hall de ladrillo con su torre y reloj, el andén
/// con techo sobre postes, las vías que se pierden en el campo, el depósito del ferrocarril, el tanque de agua, la plaza
/// con fuente, la avenida que baja al Centro y las casas obreras. El tren lo dibuja la escena «tren».
/// </summary>
public static partial class CityBuilder
{
    private static readonly Color Brick = new(0.5f, 0.26f, 0.19f);
    private static readonly Color Stone = new(0.72f, 0.67f, 0.58f);
    private static readonly Color DarkWood = new(0.25f, 0.16f, 0.1f);
    private static readonly Color Iron = new(0.16f, 0.17f, 0.18f);

    private static void BuildStation(Node3D city)
    {
        var node = new Node3D { Name = "Station" };
        city.AddChild(node);
        var a = Station.Area;
        var paint = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };

        // Suelo caminable del barrio (se llama Ground: el chequeo de lugar seguro lo ignora).
        var ground = new StaticBody3D { Name = "Ground", Position = new Vector3(a.CenterX, -0.5f, a.CenterZ) };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(a.Width, 1, a.Depth) } });
        node.AddChild(ground);

        // Todo lo que no choca, en una sola malla: pisos, vías, campo, fachadas finas.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Box(st, new Vector3(a.CenterX, -0.03f, a.CenterZ), new Vector3(a.Width, 0.02f, a.Depth), Sidewalk);
        // Afuera: el campo del otro lado de las vías (para que no se vea el vacío), con terraplén de piedra.
        Box(st, new Vector3(a.CenterX, -0.12f, Station.TrackZ - 160), new Vector3(1600, 0.1f, 320), new Color(0.3f, 0.34f, 0.22f));
        Box(st, new Vector3(a.CenterX, 0.02f, Station.TrackZ), new Vector3(1600, 0.25f, 7), new Color(0.34f, 0.32f, 0.3f));
        for (var x = a.CenterX - 800; x < a.CenterX + 800; x += 1.2f)
            Box(st, new Vector3(x, 0.18f, Station.TrackZ), new Vector3(0.3f, 0.1f, 2.6f), DarkWood); // durmientes
        foreach (var off in new[] { -0.72f, 0.72f })
            Box(st, new Vector3(a.CenterX, 0.28f, Station.TrackZ + off), new Vector3(1600, 0.12f, 0.08f), Iron); // rieles
        // Andén de piedra, con la línea amarilla del borde.
        Box(st, new Vector3((Station.PlatformMinX + Station.PlatformMaxX) / 2, 0.01f, (Station.PlatformMinZ + Station.PlatformMaxZ) / 2),
            new Vector3(Station.PlatformMaxX - Station.PlatformMinX, 0.04f, Station.PlatformMaxZ - Station.PlatformMinZ), Stone);
        Box(st, new Vector3((Station.PlatformMinX + Station.PlatformMaxX) / 2, 0.035f, Station.PlatformMinZ + 0.7f),
            new Vector3(Station.PlatformMaxX - Station.PlatformMinX, 0.02f, 0.3f), new Color(0.85f, 0.7f, 0.2f));
        // Plaza empedrada y la avenida (calzada con veredas) que baja al Centro.
        Box(st, new Vector3((Station.PlazaMinX + Station.PlazaMaxX) / 2, 0.01f, (Station.PlazaMinZ + Station.PlazaMaxZ) / 2),
            new Vector3(Station.PlazaMaxX - Station.PlazaMinX, 0.03f, Station.PlazaMaxZ - Station.PlazaMinZ), Cobble.Lightened(0.15f));
        var avenueLength = a.MaxZ - Station.PlazaMaxZ;
        Box(st, new Vector3(Station.AxisX, 0.01f, Station.PlazaMaxZ + avenueLength / 2), new Vector3(11, 0.03f, avenueLength), Asphalt);
        foreach (var side in new[] { -1f, 1f })
            Box(st, new Vector3(Station.AxisX + side * 5.6f, 0.03f, Station.PlazaMaxZ + avenueLength / 2), new Vector3(0.25f, 0.06f, avenueLength), Curb);
        // Piso del hall: baldosas en damero.
        var hall = Station.Hall;
        for (var ix = 0; ix < (int)(hall.Width / 2); ix++)
        for (var iz = 0; iz < (int)(hall.Depth / 2); iz++)
            Box(st, new Vector3(hall.X - hall.Width / 2 + 1 + ix * 2, 0.025f, hall.Z - hall.Depth / 2 + 1 + iz * 2), new Vector3(2, 0.02f, 2),
                (ix + iz) % 2 == 0 ? new Color(0.78f, 0.74f, 0.66f) : new Color(0.32f, 0.28f, 0.26f));
        // Veredas de las manzanas de casas.
        foreach (var block in new[] { Station.WestBlock, Station.EastBlock })
            Box(st, new Vector3((block.MinX + block.MaxX) / 2, 0.02f, (block.MinZ + block.MaxZ) / 2),
                new Vector3(block.Width + 2 * CityLayout.SidewalkWidth, 0.03f, block.Depth + 2 * CityLayout.SidewalkWidth), Sidewalk.Lightened(0.08f));
        var flat = st.Commit();
        flat.SurfaceSetMaterial(0, paint);
        node.AddChild(new MeshInstance3D { Name = "Floors", Mesh = flat });

        BuildHall(node, paint);
        BuildPlatformCanopy(node, paint);
        BuildStationYard(node);
        BuildWorkerHouses(node, paint);

        // Faroles con luz de noche y la fuente de la plaza.
        foreach (var l in Station.Lamps)
        {
            Visual(node, "LampPost", new Vector3(0.16f, 4.4f, 0.16f), new Vector3(l.X, 2.2f, l.Y), Iron);
            node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f }, MaterialOverride = Glow(new Color(1f, 0.88f, 0.62f), 0.3f), Position = new Vector3(l.X, 4.5f, l.Y) });
            node.AddChild(NightLamp(new Vector3(l.X, 4.2f, l.Y), new Color(1f, 0.85f, 0.6f), 1.6f, 14f));
        }
        var f = Station.Fountain;
        SolidCylinder(node, "Fountain", 3.2f, 0.7f, new Vector3(f.X, 0.35f, f.Y), Stone);
        node.AddChild(new MeshInstance3D { Name = "FountainWater", Mesh = new CylinderMesh { TopRadius = 2.9f, BottomRadius = 2.9f, Height = 0.05f }, MaterialOverride = Material(new Color(0.2f, 0.32f, 0.4f)), Position = new Vector3(f.X, 0.71f, f.Y) });
        SolidCylinder(node, "FountainColumn", 0.4f, 2.4f, new Vector3(f.X, 1.2f, f.Y), Stone.Darkened(0.1f));

        // Carteles: en la plaza, para el que baja del tren, y donde la avenida llega al Centro.
        node.AddChild(new Label3D
        {
            // Cartel en un poste al borde de la plaza, de cara a los que salen de la estación (miran hacia el sur, +Z).
            Name = "ToMainStreet", Text = "AVENIDA DE LA ESTACIÓN\n↑ Al Centro y a la Calle Principal", FontSize = 40, OutlineSize = 8, PixelSize = 0.008f,
            Modulate = new Color(0.95f, 0.88f, 0.68f), Position = new Vector3(Station.AxisX + 10.5f, 3.1f, Station.PlazaMaxZ - 0.05f), Rotation = new Vector3(0, Mathf.Pi, 0),
        });
        Visual(node, "SignPost", new Vector3(0.14f, 3.6f, 0.14f), new Vector3(Station.AxisX + 10.5f, 1.8f, Station.PlazaMaxZ + 0.1f), Iron);
        Visual(node, "SignBoard", new Vector3(5.2f, 1.1f, 0.08f), new Vector3(Station.AxisX + 10.5f, 3.1f, Station.PlazaMaxZ + 0.02f), new Color(0.12f, 0.16f, 0.13f));
        node.AddChild(new Label3D
        {
            Name = "ToStation", Text = "↑ ESTACIÓN BELLMONT", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 52, OutlineSize = 10, PixelSize = 0.01f, Modulate = new Color(0.95f, 0.85f, 0.6f), Position = new Vector3(Station.AxisX - 8, 4.5f, a.MaxZ + 4),
        });
    }

    /// <summary>El hall: paredes de ladrillo con zócalo de piedra, ventanales, techo, torre con reloj y lo de adentro.</summary>
    private static void BuildHall(Node3D parent, StandardMaterial3D paint)
    {
        const float height = 8f;
        var hall = Station.Hall;
        var node = new Node3D { Name = "Hall" };
        parent.AddChild(node);
        var body = new StaticBody3D { Name = "HallWalls" };
        node.AddChild(body);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var glass = new Color(0.15f, 0.18f, 0.2f);
        foreach (var w in Station.HallWalls)
        {
            var center = new Vector3(w.X, height / 2, w.Z);
            var size = new Vector3(w.Width, height, w.Depth);
            Box(st, center, size, Brick);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
            Box(st, new Vector3(w.X, 0.5f, w.Z), new Vector3(w.Width + 0.1f, 1f, w.Depth + 0.1f), Stone); // zócalo
            // Ventanales altos (arriba de las puertas) en los tramos largos.
            if (w.Width > 4)
                Box(st, new Vector3(w.X, 5.2f, w.Z), new Vector3(w.Width - 2, 2.6f, w.Depth + 0.06f), glass);
            if (w.Depth > 4)
                Box(st, new Vector3(w.X, 5.2f, w.Z), new Vector3(w.Width + 0.06f, 2.6f, w.Depth - 3), glass);
        }
        // Dinteles sobre las puertas (no chocan: están arriba de la cabeza).
        foreach (var x in Station.PlatformDoors)
            Box(st, new Vector3(x, 6.8f, Station.HallBack + 0.25f), new Vector3(Station.PlatformDoorWidth + 0.4f, 2.4f, 0.5f), Brick.Darkened(0.1f));
        Box(st, new Vector3(Station.AxisX, 7f, Station.HallFront - 0.25f), new Vector3(Station.FrontDoorWidth + 0.4f, 2f, 0.5f), Brick.Darkened(0.1f));
        // Techo a dos aguas (aproximado con dos losas) y cornisa.
        Box(st, new Vector3(hall.X, height + 0.15f, hall.Z), new Vector3(hall.Width + 0.8f, 0.3f, hall.Depth + 0.8f), Stone.Darkened(0.25f));
        Box(st, new Vector3(hall.X, height + 1.2f, hall.Z), new Vector3(hall.Width - 2, 1.8f, hall.Depth - 6), new Color(0.28f, 0.3f, 0.32f));
        // Frente: frontón de piedra sobre la puerta grande, y la torre del reloj.
        Box(st, new Vector3(Station.AxisX, height + 1.2f, Station.HallFront - 0.3f), new Vector3(14, 2.4f, 0.6f), Stone);
        Box(st, new Vector3(Station.AxisX, height + 5f, Station.HallFront - 2.5f), new Vector3(4.4f, 8f, 4.4f), Brick.Darkened(0.05f));
        Box(st, new Vector3(Station.AxisX, height + 9.3f, Station.HallFront - 2.5f), new Vector3(5f, 0.6f, 5f), Stone);
        Box(st, new Vector3(Station.AxisX, height + 10.4f, Station.HallFront - 2.5f), new Vector3(3f, 1.6f, 3f), new Color(0.22f, 0.3f, 0.26f));
        // Adentro: boletería, bancos.
        var furniture = new StaticBody3D { Name = "HallFurniture" };
        node.AddChild(furniture);
        for (var i = 0; i < Station.HallFurniture.Count; i++)
        {
            var b = Station.HallFurniture[i];
            var h = i == 0 ? 2.6f : 0.9f;
            var center = new Vector3(b.X, h / 2, b.Z);
            Box(st, center, new Vector3(b.Width, h, b.Depth), i == 0 ? DarkWood : DarkWood.Lightened(0.15f));
            furniture.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(b.Width, h, b.Depth) }, Position = center });
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, paint);
        body.AddChild(new MeshInstance3D { Mesh = mesh });

        // Carteles y reloj.
        node.AddChild(new Label3D
        {
            Name = "Name", Text = "ESTACIÓN BELLMONT", FontSize = 120, OutlineSize = 16, PixelSize = 0.01f, Modulate = new Color(0.95f, 0.9f, 0.78f),
            OutlineModulate = new Color(0.15f, 0.1f, 0.08f), Position = new Vector3(Station.AxisX, height + 1.2f, Station.HallFront + 0.05f),
        });
        node.AddChild(new Label3D
        {
            Name = "Ticket", Text = "BOLETERÍA", FontSize = 60, OutlineSize = 8, PixelSize = 0.008f, Modulate = new Color(0.95f, 0.85f, 0.55f),
            Position = new Vector3(96.3f, 3.1f, -396), Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        });
        node.AddChild(new Label3D
        {
            Name = "Board", Text = "LLEGADAS\nChicago · Detroit · Cleveland · Nueva York\nUn tren cada pocos minutos", FontSize = 44, OutlineSize = 8,
            PixelSize = 0.008f, Modulate = new Color(0.95f, 0.9f, 0.7f), Position = new Vector3(Station.AxisX, 6.6f, Station.HallBack + 0.56f),
        });
        var clock = new StationClock { Name = "Clock", Position = new Vector3(Station.AxisX, height + 5.6f, Station.HallFront - 0.2f) };
        node.AddChild(clock);
        // Luz adentro (de noche se ve por los ventanales).
        node.AddChild(NightLamp(new Vector3(hall.X - 10, 6.5f, hall.Z), new Color(1f, 0.86f, 0.62f), 2.2f, 18f));
        node.AddChild(NightLamp(new Vector3(hall.X + 10, 6.5f, hall.Z), new Color(1f, 0.86f, 0.62f), 2.2f, 18f));
    }

    /// <summary>Techo del andén sobre postes de hierro.</summary>
    private static void BuildPlatformCanopy(Node3D parent, StandardMaterial3D paint)
    {
        var body = new StaticBody3D { Name = "Canopy" };
        parent.AddChild(body);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var p in Station.CanopyPosts)
        {
            Box(st, new Vector3(p.X, 2.1f, p.Z), new Vector3(0.22f, 4.2f, 0.22f), Iron);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(p.Width, 4.2f, p.Depth) }, Position = new Vector3(p.X, 2.1f, p.Z) });
        }
        var length = Station.PlatformMaxX - Station.PlatformMinX;
        var mid = (Station.PlatformMinX + Station.PlatformMaxX) / 2;
        Box(st, new Vector3(mid, 4.3f, -412.2f), new Vector3(length, 0.2f, 7.6f), new Color(0.3f, 0.22f, 0.16f));
        Box(st, new Vector3(mid, 4.05f, -416f), new Vector3(length, 0.35f, 0.12f), new Color(0.75f, 0.68f, 0.5f)); // borde con «puntilla»
        Box(st, new Vector3(mid, 3.9f, -410), new Vector3(length, 0.2f, 0.2f), Iron);
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, paint);
        body.AddChild(new MeshInstance3D { Mesh = mesh });
    }

    /// <summary>Depósito del ferrocarril, tanque de agua y galpón.</summary>
    private static void BuildStationYard(Node3D parent)
    {
        var d = Station.FreightDepot;
        Solid(parent, "FreightDepot", new Vector3(d.Width, 6.5f, d.Depth), new Vector3(d.X, 3.25f, d.Z), new Color(0.42f, 0.3f, 0.24f));
        Visual(parent, "FreightDoor", new Vector3(5, 4, 0.1f), new Vector3(d.X, 2, d.Z + d.Depth / 2 + 0.06f), DarkWood);
        Visual(parent, "FreightRoof", new Vector3(d.Width + 0.8f, 0.4f, d.Depth + 0.8f), new Vector3(d.X, 6.7f, d.Z), new Color(0.3f, 0.3f, 0.32f));
        parent.AddChild(new Label3D
        {
            Name = "FreightSign", Text = "DEPÓSITO · FERROCARRIL CENTRAL", FontSize = 56, OutlineSize = 8, PixelSize = 0.01f,
            Modulate = new Color(0.92f, 0.86f, 0.7f), Position = new Vector3(d.X, 5, d.Z + d.Depth / 2 + 0.08f),
        });
        var t = Station.WaterTower;
        Solid(parent, "WaterTowerBase", new Vector3(t.Width, 1, t.Depth), new Vector3(t.X, 0.5f, t.Z), Stone.Darkened(0.2f));
        foreach (var (lx, lz) in new[] { (-1.8f, -1.8f), (1.8f, -1.8f), (-1.8f, 1.8f), (1.8f, 1.8f) })
            Visual(parent, "WaterTowerLeg", new Vector3(0.3f, 8, 0.3f), new Vector3(t.X + lx, 5, t.Z + lz), Iron);
        parent.AddChild(new MeshInstance3D
        {
            Name = "WaterTank", Mesh = new CylinderMesh { TopRadius = 3, BottomRadius = 3, Height = 4 }, MaterialOverride = Material(new Color(0.4f, 0.3f, 0.22f)),
            Position = new Vector3(t.X, 11, t.Z),
        });
        parent.AddChild(new MeshInstance3D
        {
            Name = "WaterTankRoof", Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 3.3f, Height = 1.4f }, MaterialOverride = Material(Iron.Lightened(0.1f)),
            Position = new Vector3(t.X, 13.7f, t.Z),
        });
        var s = Station.Shed;
        Solid(parent, "Shed", new Vector3(s.Width, 4.5f, s.Depth), new Vector3(s.X, 2.25f, s.Z), new Color(0.35f, 0.33f, 0.3f));
    }

    /// <summary>Casas obreras de ladrillo: un cuerpo y una malla por manzana, con ventanas que se prenden de noche.</summary>
    private static void BuildWorkerHouses(Node3D parent, StandardMaterial3D paint)
    {
        var body = new StaticBody3D { Name = "WorkerHouses" };
        parent.AddChild(body);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var lit = new SurfaceTool();
        lit.Begin(Mesh.PrimitiveType.Triangles);
        var glass = new Color(0.12f, 0.14f, 0.17f);
        foreach (var h in Station.Houses)
        {
            var p = h.Plan;
            var tone = Station.Palette[h.Tone];
            var color = new Color(tone.R, tone.G, tone.B);
            var center = new Vector3(p.X, h.Height / 2, p.Z);
            var size = new Vector3(p.Width, h.Height, p.Depth);
            Box(st, center, size, color);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
            Box(st, new Vector3(p.X, h.Height + 0.15f, p.Z), new Vector3(p.Width + 0.3f, 0.3f, p.Depth + 0.3f), color.Darkened(0.4f));
            // Chimenea.
            Box(st, new Vector3(p.X - p.Width / 4, h.Height + 0.9f, p.Z + p.Depth / 4), new Vector3(0.7f, 1.6f, 0.7f), color.Darkened(0.2f));
            var fx = p.X + h.FrontX * (p.Width / 2 + 0.04f);
            // Puerta y ventanas del frente (el frente mira en x).
            Box(st, new Vector3(fx, 1.1f, p.Z - p.Depth / 4), new Vector3(0.1f, 2.2f, 1.1f), DarkWood);
            for (var floor = 0; floor < h.Stories; floor++)
            {
                var on = (uint)(((int)(p.X * 10) * 73856093) ^ ((int)(p.Z * 10) * 19349663) ^ (floor * 83492791)) % 100 < 45;
                var y = floor * 3.3f + 1.8f;
                Box(on && floor > 0 ? lit : st, new Vector3(fx, y, p.Z + (floor == 0 ? p.Depth / 5 : 0)), new Vector3(0.1f, 1.3f, floor == 0 ? p.Depth / 3 : p.Depth - 2), glass);
            }
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, paint);
        lit.Commit(mesh);
        mesh.SurfaceSetMaterial(1, DayNightCycle.LitWindows);
        var houses = new MeshInstance3D { Mesh = mesh };
        houses.AddToGroup(GraphicsQuality.FarGroup);
        body.AddChild(houses);
    }
}

/// <summary>El reloj de la torre: marca la hora de Bellmont (la misma del cielo y de la pantalla).</summary>
public partial class StationClock : Node3D
{
    private Node3D _hour = null!, _minute = null!;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 1.3f, Height = 0.1f }, Rotation = new Vector3(Mathf.Pi / 2, 0, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.92f, 0.82f), EmissionEnabled = true, Emission = new Color(0.4f, 0.37f, 0.3f) } });
        _hour = Hand(0.7f, 0.12f);
        _minute = Hand(1.1f, 0.07f);
    }

    private Node3D Hand(float length, float width)
    {
        var pivot = new Node3D { Position = new Vector3(0, 0, 0.08f) };
        pivot.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(width, length, 0.04f) }, Position = new Vector3(0, length / 2, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.07f, 0.06f) } });
        AddChild(pivot);
        return pivot;
    }

    public override void _Process(double delta)
    {
        var t = Distrito.Shared.Life.BellmontClock.TimeOfDay(DateTimeOffset.UtcNow);
        _minute.Rotation = new Vector3(0, 0, -(float)(t.TotalMinutes % 60 / 60 * Math.Tau));
        _hour.Rotation = new Vector3(0, 0, -(float)(t.TotalHours % 12 / 12 * Math.Tau));
    }
}
