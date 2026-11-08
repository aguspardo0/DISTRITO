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
        BuildDowntown(city);
        BuildEdges(city);
    }

    // ---------- El Centro (fase 1 del mapa) ----------

    private static readonly Color Asphalt = new(0.15f, 0.15f, 0.16f);
    private static readonly Color Curb = new(0.42f, 0.41f, 0.39f);

    /// <summary>
    /// El Centro: suelo de calles, veredas, edificios de época (pegados, con cornisa y filas de ventanas), faroles y los
    /// locales vacíos con su cartel. Cada manzana es una sola malla y un solo cuerpo de colisión (pocas llamadas de
    /// dibujo aunque sean cientos de edificios). Los muros de los barrios se ven: ladrillo del color de la familia.
    /// </summary>
    private static void BuildDowntown(Node3D city)
    {
        var node = new Node3D { Name = "Downtown" };
        city.AddChild(node);
        var d = CityLayout.Downtown;
        // Suelo: un cuerpo grande (arriba en y = 0) y la calzada apenas más abajo para no pelear con los suelos de antes.
        var ground = new StaticBody3D { Name = "Ground", Position = new Vector3(0, -0.5f, 0) };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(d.Width, 1, d.Depth) } });
        ground.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(d.Width, 1, d.Depth) }, MaterialOverride = Material(Asphalt), Position = new Vector3(0, -0.03f, 0) });
        node.AddChild(ground);

        var paint = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.92f };
        var glow = Glow(new Color(0.95f, 0.92f, 0.8f), 0.15f); // de día casi apagados
        var byBlock = CityLayout.DowntownBuildings.GroupBy(b => CityLayout.DowntownBlocks.First(k => k.Contains(b.Plan.X, b.Plan.Z, 0.01f))).ToDictionary(g => g.Key, g => g.ToList());
        var index = 0;
        foreach (var block in CityLayout.DowntownBlocks)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            var lamps = new SurfaceTool();
            lamps.Begin(Mesh.PrimitiveType.Triangles);
            var lit = new SurfaceTool(); // ventanas que se encienden de noche
            lit.Begin(Mesh.PrimitiveType.Triangles);
            var anyLit = false;
            var body = new StaticBody3D { Name = $"Block{index++}" };

            // Vereda alrededor de la manzana (y el piso de la manzana, para baldíos y pasajes).
            Box(st, new Vector3((block.MinX + block.MaxX) / 2, 0.005f, (block.MinZ + block.MaxZ) / 2),
                new Vector3(block.Width + 2 * CityLayout.SidewalkWidth, 0.03f, block.Depth + 2 * CityLayout.SidewalkWidth), Sidewalk);
            Box(st, new Vector3((block.MinX + block.MaxX) / 2, 0.012f, (block.MinZ + block.MaxZ) / 2),
                new Vector3(block.Width + 0.3f, 0.03f, block.Depth + 0.3f), Curb.Darkened(0.2f));

            foreach (var b in byBlock.GetValueOrDefault(block) ?? [])
            {
                var p = b.Plan;
                var tone = CityLayout.DowntownPalette[b.Tone];
                var color = new Color(tone.R, tone.G, tone.B);
                var center = new Vector3(p.X, b.Height / 2, p.Z);
                var size = new Vector3(p.Width, b.Height, p.Depth);
                Box(st, center, size, color);
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
                Box(st, new Vector3(p.X, b.Height + 0.2f, p.Z), new Vector3(p.Width + 0.35f, 0.4f, p.Depth + 0.35f), color.Darkened(0.35f)); // cornisa
                Box(st, new Vector3(p.X, 3.9f, p.Z), new Vector3(p.Width + 0.12f, 0.25f, p.Depth + 0.12f), color.Darkened(0.25f)); // línea de planta baja
                // Ventanas: una franja oscura por piso en cada fachada (de vidrio y marco, sin texturas todavía).
                var glass = new Color(0.12f, 0.14f, 0.17f);
                for (var floor = 1; floor < b.Stories; floor++)
                {
                    var y = floor * 3.6f + 1.9f;
                    // Algunos pisos tienen gente: de noche se ven las ventanas encendidas (siempre los mismos).
                    var on = (uint)(((int)(p.X * 10) * 73856093) ^ ((int)(p.Z * 10) * 19349663) ^ (floor * 83492791)) % 100 < 40;
                    var target = on ? lit : st;
                    anyLit |= on;
                    Box(target, new Vector3(p.X, y, p.Z), new Vector3(p.Width + 0.08f, 1.5f, p.Depth - 1.4f), glass);
                    Box(target, new Vector3(p.X, y, p.Z), new Vector3(p.Width - 1.4f, 1.5f, p.Depth + 0.08f), glass);
                }
                // Planta baja: vidriera y puerta del lado del frente.
                var front = new Vector3(p.X + b.FrontX * (p.Width / 2 + 0.04f), 1.6f, p.Z + b.FrontZ * (p.Depth / 2 + 0.04f));
                var wide = b.FrontX != 0 ? p.Depth : p.Width;
                var shopSize = b.FrontX != 0 ? new Vector3(0.1f, 2.4f, wide - 2.4f) : new Vector3(wide - 2.4f, 2.4f, 0.1f);
                Box(st, front, shopSize, glass.Lightened(0.08f));
            }

            // Faroles cada 25 m por el borde de la vereda (solo se ven; la luz de verdad queda para el día y la noche).
            foreach (var (lx, lz) in LampSpots(block))
            {
                Box(st, new Vector3(lx, 2.25f, lz), new Vector3(0.14f, 4.5f, 0.14f), new Color(0.2f, 0.22f, 0.22f));
                Box(lamps, new Vector3(lx, 4.55f, lz), new Vector3(0.4f, 0.18f, 0.4f), Colors.White);
            }

            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, paint);
            lamps.Commit(mesh);
            mesh.SurfaceSetMaterial(1, glow);
            if (anyLit)
            {
                lit.Commit(mesh);
                mesh.SurfaceSetMaterial(2, DayNightCycle.LitWindows);
            }
            body.AddChild(new MeshInstance3D { Mesh = mesh });
            node.AddChild(body);
        }

        // Locales vacíos: toldo y cartel «SE ALQUILA» (todavía no se pueden alquilar: falta definir las reglas).
        foreach (var shop in CityLayout.EmptyShops)
        {
            var facing = Mathf.Atan2(shop.NX, shop.NZ); // el cartel (Label3D) se lee desde +Z
            var sign = new Node3D { Name = $"Shop_{shop.Code}", Position = new Vector3(shop.X, 0, shop.Z), Rotation = new Vector3(0, facing, 0) };
            node.AddChild(sign);
            Visual(sign, "Awning", new Vector3(shop.Width, 0.12f, 1.4f), new Vector3(0, 3.1f, 0.1f), new Color(0.45f, 0.18f, 0.15f));
            sign.AddChild(new Label3D
            {
                Name = "ForRent", Text = "SE ALQUILA\nLocal disponible", FontSize = 40, OutlineSize = 8, PixelSize = 0.008f,
                Modulate = new Color(0.95f, 0.88f, 0.7f), Position = new Vector3(0, 2.3f, -0.45f), VisibilityRangeEnd = 45,
            });
        }

        // El Café La Perla: la competencia de los puestos de café (no es de ningún jugador).
        {
            var shop = CityLayout.RivalCafe;
            var facing = Mathf.Atan2(shop.NX, shop.NZ);
            var cafe = new Node3D { Name = "CafeLaPerla", Position = new Vector3(shop.X, 0, shop.Z), Rotation = new Vector3(0, facing, 0) };
            node.AddChild(cafe);
            Visual(cafe, "Awning", new Vector3(shop.Width, 0.12f, 1.6f), new Vector3(0, 3.1f, 0.15f), new Color(0.15f, 0.35f, 0.25f));
            var neon = new Color(1f, 0.85f, 0.55f);
            Visual(cafe, "SignBoard", new Vector3(shop.Width * 0.8f, 0.9f, 0.1f), new Vector3(0, 4.3f, -0.5f), new Color(0.1f, 0.1f, 0.11f));
            cafe.AddChild(new Label3D
            {
                Name = "Neon", Text = "CAFÉ LA PERLA", FontSize = 90, OutlineSize = 14, PixelSize = 0.01f, Shaded = false,
                Modulate = neon, OutlineModulate = neon.Darkened(0.6f), Position = new Vector3(0, 4.3f, -0.42f),
            });
            cafe.AddChild(NightLamp(new Vector3(0, 2.8f, 1.2f), new Color(1f, 0.85f, 0.6f), 1.4f, 7f));
        }

        // Muros de los barrios: ladrillo de 3,2 m con el color de cada familia.
        foreach (var barrio in CityLayout.Barrios)
        {
            var tone = new Color(barrio.Palette[0].R, barrio.Palette[0].G, barrio.Palette[0].B).Darkened(0.25f);
            var i = 0;
            foreach (var w in CityLayout.WallsOf(barrio))
            {
                Solid(node, $"Wall_{barrio.Area.Code}_{i}", new Vector3(w.Width, 3.2f, w.Depth), new Vector3(w.X, 1.6f, w.Z), tone);
                Visual(node, $"WallCap_{barrio.Area.Code}_{i++}", new Vector3(w.Width + 0.2f, 0.2f, w.Depth + 0.2f), new Vector3(w.X, 3.3f, w.Z), tone.Darkened(0.3f));
            }
        }

        // Cartel en cada esquina del Mercado hacia el Centro.
        node.AddChild(new Label3D
        {
            Name = "DowntownSign", Text = "EL CENTRO\nBellmont · 1952", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64,
            OutlineSize = 12, PixelSize = 0.01f, Modulate = new Color(0.95f, 0.85f, 0.6f), Position = new Vector3(-36, 6, -36),
        });
    }

    /// <summary>Faroles en el borde de la vereda de una manzana, cada unos 25 m.</summary>
    private static IEnumerable<(float X, float Z)> LampSpots(DowntownBlock b)
    {
        var o = CityLayout.SidewalkWidth - 0.6f;
        for (var x = b.MinX + 6; x < b.MaxX - 3; x += 25)
        {
            yield return (x, b.MinZ - o);
            yield return (x, b.MaxZ + o);
        }
        for (var z = b.MinZ + 6; z < b.MaxZ - 3; z += 25)
        {
            yield return (b.MinX - o, z);
            yield return (b.MaxX + o, z);
        }
    }

    /// <summary>Agrega una caja (con normales y color por vértice) a una malla que se está armando.</summary>
    private static void Box(SurfaceTool st, Vector3 center, Vector3 size, Color color)
    {
        var h = size / 2;
        var faces = new (Vector3 N, Vector3 U, Vector3 V)[]
        {
            // u × v = n en todas (así el orden de los vértices es el mismo para cada cara).
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
            // a → b → cc es antihorario visto desde afuera; Godot toma el horario como frente, así que va a → cc → b.
            st.AddVertex(a); st.AddVertex(cc); st.AddVertex(b);
            st.AddVertex(a); st.AddVertex(e); st.AddVertex(cc);
        }
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
            else
                DecorateRoof(node, b, building, i);
        }

        BuildLandmark(node, b, accent);

        // Alumbrado eléctrico de los 50: poste alto, brazo hacia la calle y luminaria de luz blanca; luz real cada dos.
        var lamp = 0;
        // Lado a lo ancho del barrio (en floats: CityLayout usa System.Numerics y esto es Godot).
        float sideX = -b.Inward.Y, sideZ = b.Inward.X;
        foreach (var p in CityLayout.LampsOf(b))
        {
            var spot = new Vector2(p.X, p.Y);
            var v = (p.X - b.Entrance.X) * sideX + (p.Y - b.Entrance.Y) * sideZ;
            var arm = new Vector2(sideX, sideZ) * -Mathf.Sign(v); // hacia el eje del barrio (bulevar y plaza)
            var head = spot + arm * 1.3f;
            node.AddChild(new MeshInstance3D
            {
                Name = $"LampPost{lamp}", Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.09f, Height = 4.6f },
                MaterialOverride = Material(new Color(0.22f, 0.24f, 0.24f)), Position = new Vector3(spot.X, 2.3f, spot.Y),
            });
            node.AddChild(new MeshInstance3D
            {
                Name = $"LampArm{lamp}", Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 1.4f) },
                MaterialOverride = Material(new Color(0.22f, 0.24f, 0.24f)),
                Position = new Vector3((spot.X + head.X) / 2, 4.55f, (spot.Y + head.Y) / 2), Rotation = new Vector3(0, Mathf.Atan2(arm.X, arm.Y), 0),
            });
            node.AddChild(new MeshInstance3D
            {
                Name = $"Lamp{lamp}", Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.12f, 0.55f) },
                MaterialOverride = Glow(new Color(0.92f, 0.95f, 1f), 0.2f),
                Position = new Vector3(head.X, 4.5f, head.Y), Rotation = new Vector3(0, Mathf.Atan2(arm.X, arm.Y), 0),
            });
            if (lamp % 2 == 0)
                node.AddChild(NightLamp(new Vector3(head.X, 4.2f, head.Y), new Color(0.9f, 0.93f, 1f), 1.5f, 12f));
            lamp++;
        }

        // Cartel de bienvenida en la entrada (se lee al llegar por la avenida).
        var gate = b.ToWorld(2, 0);
        node.AddChild(new Label3D
        {
            Name = "WelcomeSign", Text = $"{area.Name.ToUpperInvariant()}\n{b.Motto}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, OutlineSize = 12, PixelSize = 0.01f,
            Modulate = accent.Lightened(0.2f), Position = new Vector3(gate.X, 6.8f, gate.Y),
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
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, OutlineSize = 10, PixelSize = 0.009f,
            Modulate = accent.Lightened(0.3f), Position = new Vector3(sign.X, 3.6f, sign.Y),
        });
    }

    private static readonly string[][] NeonWords =
    [
        ["BAR", "PIZZERIA", "HOTEL", "BILLARES", "CAFÉ"],   // Moretti
        ["BANCO", "CLUB", "HOTEL", "CINE", "JOYERÍA"],      // Valenti
        ["DINER", "GARAGE", "TALLER", "BAR", "BOWLING"],    // Caruso
    ];

    private static readonly Color[] NeonColors =
        [new(1f, 0.25f, 0.45f), new(0.3f, 0.9f, 1f), new(1f, 0.35f, 0.2f), new(0.45f, 1f, 0.5f), new(1f, 0.8f, 0.3f)];

    /// <summary>
    /// Techos de 1952: antenas de televisión en uno de cada tres edificios y un cartel de neón en uno de cada cuatro,
    /// mirando hacia la entrada del barrio. Solo decorado (sin colisión), determinista por edificio.
    /// </summary>
    private static void DecorateRoof(Node3D node, BarrioPlan b, CityBuilding building, int index)
    {
        var plan = building.Plan;
        var top = building.Height + 0.35f;
        if (index % 3 == 0)
        {
            var at = new Vector3(plan.X + plan.Width * 0.25f, top, plan.Z - plan.Depth * 0.2f);
            var metal = Material(new Color(0.55f, 0.56f, 0.58f));
            node.AddChild(new MeshInstance3D { Name = $"Antenna{index}", Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 2.2f }, MaterialOverride = metal, Position = at + new Vector3(0, 1.1f, 0) });
            for (var k = 0; k < 3; k++)
                node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f - k * 0.35f, 0.03f, 0.03f) }, MaterialOverride = metal, Position = at + new Vector3(0, 1.4f + k * 0.35f, 0), Rotation = new Vector3(0, 0.6f, 0) });
        }
        if (index % 4 != 1)
            return;
        var family = b.Family switch { Distrito.Shared.Economy.EconomyCatalog.Moretti => 0, Distrito.Shared.Economy.EconomyCatalog.Valenti => 1, _ => 2 };
        var words = NeonWords[family];
        var word = words[index / 4 % words.Length];
        var color = NeonColors[(index / 4 + family) % NeonColors.Length];
        var facing = Mathf.Atan2(-b.Inward.X, -b.Inward.Y);
        var front = b.Inward.X != 0 ? plan.Width / 2 : plan.Depth / 2;
        var posX = plan.X - b.Inward.X * (front - 0.6f);
        var posZ = plan.Z - b.Inward.Y * (front - 0.6f);
        var sign = new Node3D { Name = $"Neon{index}", Position = new Vector3(posX, top, posZ), Rotation = new Vector3(0, facing, 0) };
        node.AddChild(sign);
        var width = 0.75f * word.Length + 0.8f;
        Visual(sign, "Frame", new Vector3(width, 1.5f, 0.12f), new Vector3(0, 1.2f, -0.1f), new Color(0.1f, 0.1f, 0.11f));
        foreach (var x in new[] { -width * 0.35f, width * 0.35f })
            Visual(sign, $"Leg{x}", new Vector3(0.08f, 0.6f, 0.08f), new Vector3(x, 0.2f, -0.1f), new Color(0.15f, 0.15f, 0.16f));
        sign.AddChild(new Label3D
        {
            Text = word, FontSize = 120, OutlineSize = 18, PixelSize = 0.01f, Modulate = color, OutlineModulate = color.Darkened(0.6f),
            Shaded = false, Position = new Vector3(0, 1.2f, 0),
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
            Text = b.Title, FontSize = 110, OutlineSize = 14, Modulate = accent.Lightened(0.35f), PixelSize = 0.014f,
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
        node.AddChild(NightLamp(at + new Vector3(0, 4f, 0), new Color(1f, 0.85f, 0.6f), 1.5f, 12f));
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

    /// <summary>Material que brilla; de noche brilla más (lo maneja <see cref="DayNightCycle"/>).</summary>
    private static StandardMaterial3D Glow(Color color, float energy)
    {
        var material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = energy };
        DayNightCycle.Glows.Add((material, energy));
        return material;
    }

    /// <summary>Farol con luz de verdad: solo de noche.</summary>
    private static OmniLight3D NightLamp(Vector3 position, Color color, float energy, float range)
    {
        var lamp = new OmniLight3D { Position = position, LightColor = color, LightEnergy = energy, OmniRange = range, Visible = false };
        DayNightCycle.Lamps.Add(lamp);
        return lamp;
    }

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
