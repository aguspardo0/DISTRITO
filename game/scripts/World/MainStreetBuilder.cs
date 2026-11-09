using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Construye la Calle Principal (ver <see cref="MainStreet"/>): calzada con línea al medio, veredas con cordón, los
/// edificios de cada lote (con cornisa, ventanas, vidrieras, toldo y cartel), la plazoleta de La Esquina y los faroles.
/// Igual en el servidor y en los clientes. Las puertas de los locales con interior las pone <see cref="InteriorBuilder"/>
/// y el casino, <see cref="DocksBuilder.BuildCasino"/>.
/// </summary>
public static class MainStreetBuilder
{
    private static readonly Color Asphalt = new(0.16f, 0.16f, 0.17f);
    private static readonly Color Sidewalk = new(0.4f, 0.39f, 0.36f);
    private static readonly Color Curb = new(0.5f, 0.49f, 0.46f);
    private static readonly Color Paint = new(0.85f, 0.78f, 0.45f);
    private static readonly Color Glass = new(0.13f, 0.15f, 0.18f);
    private static readonly Color Brass = new(0.95f, 0.85f, 0.55f);

    public static void Build(Node3D world)
    {
        var node = new Node3D { Name = "MainStreet" };
        world.AddChild(node);
        var m = CityLayout.Market;

        // Piso: vereda clara en todo el lote y la calzada encima, un poco más oscura, con la línea del medio.
        Visual(node, "Sidewalks", new Vector3(m.Width, 0.04f, m.Depth), new Vector3(m.CenterX, 0.02f, 0), Sidewalk);
        Visual(node, "Road", new Vector3(m.Width, 0.05f, MainStreet.RoadHalf * 2), new Vector3(m.CenterX, 0.03f, 0), Asphalt);
        foreach (var side in new[] { -1, 1 })
            Visual(node, $"Curb{side}", new Vector3(m.Width, 0.12f, 0.25f), new Vector3(m.CenterX, 0.06f, side * MainStreet.RoadHalf), Curb);
        for (var x = m.MinX + 2; x < m.MaxX - 2; x += 6)
            Visual(node, "Line", new Vector3(3f, 0.06f, 0.18f), new Vector3(x + 1.5f, 0.035f, 0), Paint);
        // Las avenidas de las familias cruzan la calle: ahí la vereda se corta (sendas peatonales a rayas).
        foreach (var (minX, maxX, north) in new[] { (CityLayout.NorthAvenue.MinX, CityLayout.NorthAvenue.MaxX, true), (CityLayout.SouthAvenue.MinX, CityLayout.SouthAvenue.MaxX, false) })
            for (var x = minX + 0.5f; x < maxX - 0.5f; x += 1.2f)
                Visual(node, "Crossing", new Vector3(0.6f, 0.06f, 3.5f), new Vector3(x, 0.035f, (north ? -1 : 1) * (MainStreet.RoadHalf - 2f)), Colors.WhiteSmoke.Darkened(0.15f));

        var paint = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        var body = new StaticBody3D { Name = "Buildings" };
        node.AddChild(body);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var lit = new SurfaceTool();
        lit.Begin(Mesh.PrimitiveType.Triangles);
        var anyLit = false;

        foreach (var lot in MainStreet.Buildings.Where(l => l.Kind != "casino"))
        {
            var p = lot.Plan;
            var tone = CityLayout.DowntownPalette[lot.Tone % CityLayout.DowntownPalette.Length];
            var color = new Color(tone.R, tone.G, tone.B);
            var center = new Vector3(p.X, lot.Height / 2, p.Z);
            var size = new Vector3(p.Width, lot.Height, p.Depth);
            Box(st, center, size, color);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
            Box(st, new Vector3(p.X, lot.Height + 0.2f, p.Z), new Vector3(p.Width + 0.4f, 0.4f, p.Depth + 0.4f), color.Darkened(0.35f)); // cornisa
            Box(st, new Vector3(p.X, 3.9f, p.Z), new Vector3(p.Width + 0.14f, 0.25f, p.Depth + 0.14f), color.Darkened(0.25f)); // línea de planta baja

            // Pisos de arriba: filas de ventanas en el frente (algunas se encienden de noche).
            var face = lot.FrontCenter;
            var facing = lot.Facing;
            var stories = (int)(lot.Height / 3.6f);
            for (var floor = 1; floor < stories; floor++)
            {
                var y = floor * 3.6f + 1.9f;
                var count = Math.Max(2, (int)(p.Width / 3.2f));
                for (var i = 0; i < count; i++)
                {
                    var wx = lot.MinX + (i + 0.5f) * p.Width / count;
                    var on = Math.Abs(i * 7 + floor * 3 + (int)lot.MinX) % 5 < 2;
                    anyLit |= on;
                    Box(on ? lit : st, new Vector3(wx, y, face.Y + facing.Y * 0.04f), new Vector3(1.3f, 1.6f, 0.1f), Glass);
                }
            }

            // Planta baja: dos vidrieras a los lados (la puerta va al medio si el local tiene interior).
            var hasDoorInFront = lot.Kind == "interior" && InteriorLayout.Find(lot.Code) is { } plan && plan.Outward.Y != 0;
            var pane = hasDoorInFront ? (p.Width - 4f) / 2 - 0.6f : p.Width - 2.4f;
            if (hasDoorInFront)
                foreach (var side in new[] { -1, 1 })
                    Box(st, new Vector3(p.X + side * (1.6f + pane / 2 + 0.3f), 1.7f, face.Y + facing.Y * 0.05f), new Vector3(pane, 2.3f, 0.1f), Glass.Lightened(0.1f));
            else
                Box(st, new Vector3(p.X, 1.7f, face.Y + facing.Y * 0.05f), new Vector3(pane, 2.3f, 0.1f), Glass.Lightened(0.1f));

            // Cartel en el frente: los locales con puerta al frente ya lo tienen sobre la puerta.
            if (!hasDoorInFront)
                Sign(node, lot, lot.Kind == "club" ? new Color(1f, 0.45f, 0.75f) : Brass, neon: lot.Kind == "club");
            if (lot.Kind is "club")
                Marquee(node, lot);
            else if (lot.Kind == "fachada" || (lot.Kind == "interior" && !hasDoorInFront))
                Awning(node, lot, false); // los locales con puerta al frente ya tienen su toldo sobre la puerta
        }

        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, paint);
        if (anyLit)
        {
            lit.Commit(mesh);
            mesh.SurfaceSetMaterial(1, DayNightCycle.LitWindows);
        }
        body.AddChild(new MeshInstance3D { Mesh = mesh });

        BuildCorner(node, MainStreet.Lot("la-esquina"));
        BuildLamps(node);

        // Carteles de calle en las dos puntas.
        Label(node, "CALLE PRINCIPAL", new Vector3(m.MinX + 3, 5.2f, -MainStreet.RoadHalf - 1.2f), 72);
        Label(node, "CALLE PRINCIPAL\n→ Avenida del Puerto · Los Muelles", new Vector3(m.MaxX - 3, 5.2f, MainStreet.RoadHalf + 1.2f), 56);
    }

    /// <summary>La Esquina: plazoleta con baldosas, dos bancos, un árbol y un reloj de calle (el puesto de café y el kiosco los ponen sus NPC).</summary>
    private static void BuildCorner(Node3D parent, StreetLot lot)
    {
        var node = new Node3D { Name = "LaEsquina" };
        parent.AddChild(node);
        var z0 = MainStreet.Front;
        var depth = MainStreet.LotDepth;
        Visual(node, "Tiles", new Vector3(lot.Width, 0.05f, depth), new Vector3(lot.CenterX, 0.045f, z0 + depth / 2), new Color(0.52f, 0.44f, 0.36f));
        foreach (var bx in new[] { lot.MinX + 4, lot.MaxX - 4 })
        {
            Solid(node, "Bench", new Vector3(2.2f, 0.45f, 0.6f), new Vector3(bx, 0.25f, z0 + depth - 3.5f), new Color(0.35f, 0.24f, 0.14f));
            Visual(node, "BenchBack", new Vector3(2.2f, 0.5f, 0.1f), new Vector3(bx, 0.7f, z0 + depth - 3.2f), new Color(0.35f, 0.24f, 0.14f));
        }
        // Árbol: tronco y copa.
        var tree = new Vector3(lot.CenterX, 0, z0 + depth - 6f);
        SolidCylinder(node, "Trunk", 0.25f, 3f, tree + new Vector3(0, 1.5f, 0), new Color(0.3f, 0.2f, 0.12f));
        node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 2.2f, Height = 3.6f }, MaterialOverride = Material(new Color(0.2f, 0.33f, 0.18f)), Position = tree + new Vector3(0, 4.2f, 0) });
        // Reloj de calle de hierro, en la esquina de la avenida.
        var clock = new Vector3(lot.MinX + 1.2f, 0, z0 + 1.2f);
        SolidCylinder(node, "ClockPost", 0.12f, 3.6f, clock + new Vector3(0, 1.8f, 0), new Color(0.12f, 0.14f, 0.13f));
        Visual(node, "ClockFace", new Vector3(0.9f, 0.9f, 0.25f), clock + new Vector3(0, 3.9f, 0), new Color(0.92f, 0.9f, 0.8f));
        Label(node, "LA ESQUINA", new Vector3(lot.CenterX, 4.6f, z0 + depth - 1f), 60);
    }

    private static void BuildLamps(Node3D parent)
    {
        var node = new Node3D { Name = "Lamps" };
        parent.AddChild(node);
        var post = Material(new Color(0.16f, 0.18f, 0.18f));
        var head = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.92f, 0.75f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.6f), EmissionEnergyMultiplier = 0.3f };
        DayNightCycle.Glows.Add((head, 0.3f));
        var i = 0;
        foreach (var at in MainStreet.Lamps())
        {
            // En los cruces de las avenidas no va farol (queda libre el paso).
            if (at.X > CityLayout.NorthAvenue.MinX - 1 && at.X < CityLayout.NorthAvenue.MaxX + 1 && at.Y < 0) continue;
            if (at.X > CityLayout.SouthAvenue.MinX - 1 && at.X < CityLayout.SouthAvenue.MaxX + 1 && at.Y > 0) continue;
            node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.1f, Height = 4.4f }, MaterialOverride = post, Position = new Vector3(at.X, 2.2f, at.Y) });
            node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.28f, Height = 0.56f }, MaterialOverride = head, Position = new Vector3(at.X, 4.55f, at.Y) });
            // Luz de verdad solo en uno de cada tres (de noche), para no cargar la placa de video.
            if (i++ % 3 == 0)
            {
                var lamp = new OmniLight3D { Position = new Vector3(at.X, 4.2f, at.Y), LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.6f, OmniRange = 14f, Visible = false };
                DayNightCycle.Lamps.Add(lamp);
                node.AddChild(lamp);
            }
        }
    }

    private static void Sign(Node3D parent, StreetLot lot, Color color, bool neon)
    {
        var f = lot.FrontCenter;
        var yaw = Mathf.Atan2(lot.Facing.X, lot.Facing.Y);
        var sign = new Node3D { Name = $"Sign_{lot.Code}", Position = new Vector3(f.X, 0, f.Y), Rotation = new Vector3(0, yaw, 0) };
        parent.AddChild(sign);
        Visual(sign, "Board", new Vector3(Math.Min(lot.Width - 1.5f, 9f), 0.9f, 0.12f), new Vector3(0, 4.6f, 0.1f), new Color(0.1f, 0.09f, 0.08f));
        sign.AddChild(new Label3D
        {
            Text = lot.Sign, FontSize = 72, OutlineSize = 10, PixelSize = 0.009f, Position = new Vector3(0, 4.6f, 0.2f), Shaded = !neon,
            Modulate = color, OutlineModulate = color.Darkened(0.75f), Width = 1000, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
    }

    private static void Awning(Node3D parent, StreetLot lot, bool overDoor)
    {
        var f = lot.FrontCenter;
        var yaw = Mathf.Atan2(lot.Facing.X, lot.Facing.Y);
        var node = new Node3D { Name = $"Awning_{lot.Code}", Position = new Vector3(f.X, 0, f.Y), Rotation = new Vector3(0, yaw, 0) };
        parent.AddChild(node);
        var colors = new[] { new Color(0.55f, 0.15f, 0.12f), new Color(0.15f, 0.35f, 0.25f), new Color(0.2f, 0.25f, 0.45f), new Color(0.5f, 0.4f, 0.15f) };
        var stripe = colors[lot.Code.Sum(c => c) % colors.Length]; // igual en todos los equipos
        var width = Math.Min(lot.Width - 1.2f, 12f);
        var parts = Math.Max(4, (int)(width / 0.9f));
        for (var i = 0; i < parts; i++)
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(width / parts, 0.06f, 1.5f) },
                MaterialOverride = Material(i % 2 == 0 ? new Color(0.92f, 0.88f, 0.78f) : stripe),
                Position = new Vector3(-width / 2 + (i + 0.5f) * width / parts, overDoor ? 3.25f : 3.1f, 0.75f),
                Rotation = new Vector3(Mathf.DegToRad(-14), 0, 0),
            });
    }

    /// <summary>Marquesina del club de jazz con lamparitas (como la del casino, en violeta).</summary>
    private static void Marquee(Node3D parent, StreetLot lot)
    {
        var f = lot.FrontCenter;
        var yaw = Mathf.Atan2(lot.Facing.X, lot.Facing.Y);
        var node = new Node3D { Name = $"Marquee_{lot.Code}", Position = new Vector3(f.X, 0, f.Y), Rotation = new Vector3(0, yaw, 0) };
        parent.AddChild(node);
        var width = lot.Width * 0.7f;
        Visual(node, "Canopy", new Vector3(width, 0.35f, 2.2f), new Vector3(0, 3.3f, 1.1f), new Color(0.15f, 0.08f, 0.18f));
        Visual(node, "Door", new Vector3(2.2f, 2.8f, 0.1f), new Vector3(0, 1.4f, 0.06f), new Color(0.3f, 0.12f, 0.2f));
        var bulb = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.9f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.85f), EmissionEnergyMultiplier = 1.5f };
        DayNightCycle.Glows.Add((bulb, 1.5f));
        for (var i = 0; i < 14; i++)
            node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f }, MaterialOverride = bulb, Position = new Vector3(-width / 2 + i * width / 13, 3.1f, 2.2f) });
        var light = new OmniLight3D { Position = new Vector3(0, 2.8f, 2.5f), LightColor = new Color(1f, 0.6f, 0.85f), LightEnergy = 1.4f, OmniRange = 8f, Visible = false };
        DayNightCycle.Lamps.Add(light);
        node.AddChild(light);
    }

    private static void Label(Node3D parent, string text, Vector3 at, int size) =>
        parent.AddChild(new Label3D
        {
            Text = text, FontSize = size, OutlineSize = 10, PixelSize = 0.01f, Position = at, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(0.95f, 0.85f, 0.6f), OutlineModulate = new Color(0.1f, 0.08f, 0.06f), VisibilityRangeEnd = 90,
        });

    /// <summary>Caja con normales y color por vértice (igual que en CityBuilder: u × v = n en todas las caras).</summary>
    private static void Box(SurfaceTool st, Vector3 center, Vector3 size, Color color)
    {
        var h = size / 2;
        var faces = new (Vector3 N, Vector3 U, Vector3 V)[]
        {
            (Vector3.Right, Vector3.Forward, Vector3.Up), (Vector3.Left, Vector3.Back, Vector3.Up),
            (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Right, Vector3.Back),
            (Vector3.Back, Vector3.Right, Vector3.Up), (Vector3.Forward, Vector3.Left, Vector3.Up),
        };
        foreach (var (n, u, v) in faces)
        {
            var c = center + n * h;
            var du = u * h;
            var dv = v * h;
            var a = c - du - dv;
            var b = c + du - dv;
            var cc = c + du + dv;
            var e = c - du + dv;
            st.SetColor(color);
            st.SetNormal(n);
            st.AddVertex(a); st.AddVertex(cc); st.AddVertex(b);
            st.AddVertex(a); st.AddVertex(e); st.AddVertex(cc);
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

    private static void SolidCylinder(Node3D parent, string name, float radius, float height, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height }, MaterialOverride = Material(color) });
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = height } });
        parent.AddChild(body);
    }
}
