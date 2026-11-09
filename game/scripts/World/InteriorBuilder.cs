using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Arma los interiores (y sus puertas en la calle) desde <see cref="InteriorLayout"/>, igual en el servidor y en los
/// clientes. Aspecto provisorio (greybox): cada mueble se dibuja con formas simples, pero si existe
/// res://assets/interiores/&lt;tipo&gt;.glb se usa ese modelo en su lugar (la colisión sigue siendo la caja del tipo).
/// Así, cuando lleguen los modelos definitivos, se copian los archivos y no se toca código.
/// </summary>
public static class InteriorBuilder
{
    public const string AssetFolder = "res://assets/interiores";

    /// <summary>Tamaño (ancho, alto, profundidad) de cada tipo de mueble: lo usan la forma provisoria y la colisión.</summary>
    public static readonly Dictionary<string, Vector3> Sizes = new()
    {
        ["mostrador"] = new(3f, 1.05f, 0.8f), ["escritorio"] = new(1.8f, 0.78f, 0.9f), ["archivo"] = new(0.5f, 1.3f, 0.6f),
        ["estante-alto"] = new(5f, 3.6f, 1.1f), ["estante"] = new(2.6f, 2.2f, 0.5f), ["estante-frascos"] = new(2.4f, 2.2f, 0.45f),
        ["porton"] = new(4.5f, 4.5f, 0.2f), ["pallet"] = new(1.2f, 0.15f, 1f), ["carretilla"] = new(0.6f, 1.2f, 0.5f),
        ["elevador-auto"] = new(5f, 2.4f, 2.6f), ["banco-trabajo"] = new(2.4f, 0.9f, 0.8f), ["pared-herramientas"] = new(3f, 2f, 0.1f),
        ["puerta-trasera"] = new(1.2f, 2.2f, 0.1f), ["sillon"] = new(1f, 0.9f, 0.9f), ["planta"] = new(0.5f, 1.3f, 0.5f),
        ["caja-fuerte"] = new(0.8f, 1.1f, 0.7f), ["heladera"] = new(1f, 1.9f, 0.7f), ["vidriera"] = new(1.8f, 1f, 0.6f),
        ["mesa-corte"] = new(2.2f, 0.9f, 1.1f), ["maniqui"] = new(0.5f, 1.75f, 0.4f), ["perchero"] = new(1.6f, 1.6f, 0.5f),
        ["espejo"] = new(1f, 2f, 0.08f), ["probador"] = new(1.4f, 2.2f, 1.4f), ["barra"] = new(9f, 1.1f, 0.8f),
        ["taburete"] = new(0.4f, 0.75f, 0.4f), ["reservado"] = new(1.6f, 1.1f, 1.4f), ["rocola"] = new(0.9f, 1.6f, 0.6f),
        ["mesa"] = new(0.9f, 0.75f, 0.9f),
    };

    // Lo que va arriba del mostrador o colgado: sin colisión.
    private static readonly HashSet<string> NoCollision = ["caja-registradora", "balanza", "cartel", "cuadro", "lampara-colgante", "stock-estantes", "stock-pedidos"];

    private static readonly Dictionary<string, StandardMaterial3D> Cache = new();

    public static StandardMaterial3D Mat(Color color, float roughness = 0.8f, float metallic = 0f, float emission = 0f)
    {
        var key = $"{color.ToHtml()}|{roughness}|{metallic}|{emission}";
        if (Cache.TryGetValue(key, out var m))
            return m;
        m = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
        if (emission > 0) { m.EmissionEnabled = true; m.Emission = color; m.EmissionEnergyMultiplier = emission; }
        return Cache[key] = m;
    }

    private static readonly Color Wood = new(0.42f, 0.28f, 0.16f), DarkWood = new(0.26f, 0.16f, 0.09f), Metal = new(0.5f, 0.52f, 0.55f),
        Chrome = new(0.82f, 0.82f, 0.8f), Cream = new(0.88f, 0.84f, 0.72f), Red = new(0.62f, 0.12f, 0.12f), Green = new(0.16f, 0.32f, 0.22f);

    /// <summary>Todos los interiores y sus puertas. Devuelve los puntos marcados (por ejemplo, dónde van los estantes con stock).</summary>
    /// <summary>Puntos marcados de los interiores (los estantes con stock real del galpón, por ejemplo).</summary>
    public static Dictionary<string, Vector3> Anchors { get; private set; } = new();

    public static Dictionary<string, Vector3> Build(Node3D world)
    {
        var anchors = Anchors = new Dictionary<string, Vector3>();
        Leaves.Clear();
        var root = new Node3D { Name = "Interiores" };
        world.AddChild(root);
        foreach (var plan in InteriorLayout.All)
        {
            BuildStreetDoor(root, plan);
            BuildRoom(root, plan, anchors);
        }
        return anchors;
    }

    // ---------------- La puerta en la calle ----------------

    /// <summary>Las hojas de las puertas (con física): las abre y las cierra <see cref="DoorNetwork"/>.</summary>
    public static Dictionary<string, AnimatableBody3D> Leaves { get; } = new();

    /// <summary>Marco, cartel, toldo y luz de la puerta, del lado de la calle (el hueco y la hoja los pone el salón).</summary>
    private static void BuildStreetDoor(Node3D root, InteriorPlan plan)
    {
        var yaw = Mathf.Atan2(plan.Outward.X, plan.Outward.Y); // el frente de la puerta mira hacia afuera
        var door = new Node3D { Name = $"Puerta_{plan.Code}", Position = new Vector3(plan.Door.X, 0, plan.Door.Y), Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(door);
        var frame = Mat(plan.Kind == InteriorLayout.Wholesale ? Metal : DarkWood);
        var width = plan.DoorWidth;
        // Marco: dos jambas y el dintel (el hueco queda libre).
        Box(door, new Vector3(0.15f, 2.6f, 0.14f), new Vector3(-width / 2 - 0.075f, 1.3f, 0.04f), frame);
        Box(door, new Vector3(0.15f, 2.6f, 0.14f), new Vector3(width / 2 + 0.075f, 1.3f, 0.04f), frame);
        Box(door, new Vector3(width + 0.3f, 0.15f, 0.14f), new Vector3(0, 2.55f, 0.04f), frame);
        // Toldo a rayas en los minoristas y en el diner.
        if (plan.Kind is InteriorLayout.Retail or InteriorLayout.Bar)
            for (var i = 0; i < 6; i++)
                Box(door, new Vector3((width + 1.4f) / 6, 0.06f, 1.2f), new Vector3(-(width + 1.4f) / 2 + (i + 0.5f) * (width + 1.4f) / 6, 2.95f, 0.65f),
                    Mat(i % 2 == 0 ? Cream : plan.Kind == InteriorLayout.Bar ? Red : Green), rotation: new Vector3(Mathf.DegToRad(-12), 0, 0));
        door.AddChild(new Label3D
        {
            Text = plan.Name.ToUpperInvariant(), FontSize = 64, OutlineSize = 10, PixelSize = 0.008f, Position = new Vector3(0, 3.45f, 0.15f),
            Modulate = new Color(0.98f, 0.86f, 0.55f), OutlineModulate = new Color(0.1f, 0.08f, 0.06f), Width = 900, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        door.AddChild(new OmniLight3D { Position = new Vector3(0, 2.9f, 0.8f), LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.2f, OmniRange = 5f, DistanceFadeEnabled = true, DistanceFadeBegin = 40, DistanceFadeLength = 10 });
    }

    // ---------------- El salón ----------------

    /// <summary>
    /// El salón, en su lugar adentro del edificio: piso, techo, paredes (afuera del color de la fachada, adentro con su
    /// revestimiento), la pared del frente con el hueco de la puerta y vidrieras, la hoja de la puerta y los muebles.
    /// </summary>
    private static void BuildRoom(Node3D root, InteriorPlan plan, Dictionary<string, Vector3> anchors)
    {
        var room = new Node3D { Name = $"Interior_{plan.Code}", Position = new Vector3(plan.Origin.X, 0.05f, plan.Origin.Y), Rotation = new Vector3(0, plan.Yaw, 0) };
        root.AddChild(room);
        float w = plan.Width, d = plan.Depth, h = plan.Height;
        var furniture = new Node3D { Name = "Muebles" };
        furniture.AddToGroup(GraphicsQuality.InteriorGroup); // los muebles se dibujan solo de cerca (según la calidad gráfica)
        room.AddChild(furniture);
        const float t = InteriorLayout.WallThickness;
        var lining = WallMaterial(plan.Wall);
        var outside = FacadeMaterial(plan.Code);

        // Piso apenas por encima de la vereda (para que no se pisen) y techo.
        Solid(room, "Piso", new Vector3(w + 2 * t, 0.4f, d + 2 * t), new Vector3(0, -0.2f, 0), FloorMaterial(plan.Floor)); // arriba en y = 0,05: apenas sobre la vereda
        Box(room, new Vector3(w + 2 * t, 0.2f, d + 2 * t), new Vector3(0, h + 0.1f, 0), Mat(new Color(0.18f, 0.17f, 0.16f)));
        // Paredes del fondo y de los costados: afuera, la fachada; adentro, el revestimiento del local.
        Solid(room, "ParedFondo", new Vector3(w + 2 * t, h, t), new Vector3(0, h / 2, -d / 2 - t / 2), outside);
        Solid(room, "ParedIzq", new Vector3(t, h, d + 2 * t), new Vector3(-w / 2 - t / 2, h / 2, 0), outside);
        Solid(room, "ParedDer", new Vector3(t, h, d + 2 * t), new Vector3(w / 2 + t / 2, h / 2, 0), outside);
        Box(room, new Vector3(w, h, 0.02f), new Vector3(0, h / 2, -d / 2 + 0.01f), lining);
        Box(room, new Vector3(0.02f, h, d), new Vector3(-w / 2 + 0.01f, h / 2, 0), lining);
        Box(room, new Vector3(0.02f, h, d), new Vector3(w / 2 - 0.01f, h / 2, 0), lining);

        // Pared del frente: el hueco de la puerta y, en los comercios, vidrieras a los dos lados (se ve la calle).
        var dw = plan.DoorWidth;
        const float doorTop = 2.5f;
        var front = d / 2 + t / 2;
        var glassy = plan.Kind is InteriorLayout.Retail or InteriorLayout.Bar or InteriorLayout.Office;
        var segments = new[] { (-w / 2 - t, plan.DoorX - dw / 2), (plan.DoorX + dw / 2, w / 2 + t) };
        var glass = Mat(new Color(0.55f, 0.68f, 0.75f, 0.28f), 0.05f, 0.1f);
        glass.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var k = 0;
        foreach (var (x1, x2) in segments)
        {
            var len = x2 - x1;
            if (len <= 0.05f)
                continue;
            var cx = (x1 + x2) / 2;
            if (glassy && len > 2.2f)
            {
                // Zócalo, vidriera (con colisión: no se pasa) y la pared de arriba.
                Solid(room, $"Zocalo{k}", new Vector3(len, 0.8f, t), new Vector3(cx, 0.4f, front), outside);
                Solid(room, $"Vidriera{k}", new Vector3(len - 0.6f, 1.8f, 0.06f), new Vector3(cx, 1.7f, front), glass);
                Solid(room, $"Marco{k}a", new Vector3(0.3f, 1.8f, t), new Vector3(x1 + 0.15f, 1.7f, front), outside);
                Solid(room, $"Marco{k}b", new Vector3(0.3f, 1.8f, t), new Vector3(x2 - 0.15f, 1.7f, front), outside);
                Solid(room, $"Arriba{k}", new Vector3(len, h - 2.6f, t), new Vector3(cx, 2.6f + (h - 2.6f) / 2, front), outside);
            }
            else
                Solid(room, $"Frente{k}", new Vector3(len, h, t), new Vector3(cx, h / 2, front), outside);
            k++;
        }
        Solid(room, "Dintel", new Vector3(dw, h - doorTop, t), new Vector3(plan.DoorX, doorTop + (h - doorTop) / 2, front), outside);

        // La hoja de la puerta: gira sobre la bisagra (lado izquierdo, mirando desde afuera) hacia adentro del salón.
        var leaf = new AnimatableBody3D { Name = "Hoja", Position = new Vector3(plan.DoorX - dw / 2, 0, front), SyncToPhysics = false };
        var leafColor = plan.Kind == InteriorLayout.Wholesale ? new Color(0.35f, 0.38f, 0.4f) : new Color(0.32f, 0.2f, 0.11f);
        leaf.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(dw - 0.04f, doorTop - 0.05f, 0.07f) }, MaterialOverride = Mat(leafColor), Position = new Vector3(dw / 2, (doorTop - 0.05f) / 2, 0) });
        if (plan.Kind != InteriorLayout.Wholesale)
            leaf.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(dw * 0.55f, 0.9f, 0.08f) }, MaterialOverride = glass, Position = new Vector3(dw / 2, 1.6f, 0) });
        foreach (var side in new[] { -1f, 1f })
            leaf.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.05f, 0.05f) }, MaterialOverride = Mat(Chrome, 0.2f, 0.9f), Position = new Vector3(dw - 0.2f, 1.05f, side * 0.07f) });
        leaf.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(dw - 0.04f, doorTop - 0.05f, 0.08f) }, Position = new Vector3(dw / 2, (doorTop - 0.05f) / 2, 0) });
        room.AddChild(leaf);
        Leaves[plan.Code] = leaf;

        // Zócalo oscuro: separa piso y pared.
        foreach (var (size, at) in new[] { (new Vector3(w, 0.2f, 0.05f), new Vector3(0, 0.1f, -d / 2 + 0.03f)), (new Vector3(0.05f, 0.2f, d), new Vector3(-w / 2 + 0.03f, 0.1f, 0)), (new Vector3(0.05f, 0.2f, d), new Vector3(w / 2 - 0.03f, 0.1f, 0)) })
            Box(room, size, at, Mat(new Color(0.15f, 0.12f, 0.1f)));
        // Luz de relleno para que nada quede negro (se apaga de lejos).
        room.AddChild(new OmniLight3D { Position = new Vector3(0, h - 0.6f, 0), LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.8f, OmniRange = Mathf.Max(w, d), DistanceFadeEnabled = true, DistanceFadeBegin = 45, DistanceFadeLength = 10 });

        foreach (var item in plan.Items)
        {
            if (item.Type.StartsWith("stock-"))
            {
                var p = plan.ToWorld(item.X, item.Z);
                anchors[$"{plan.Code}:{item.Type}"] = new Vector3(p.X, 0, p.Y);
                continue;
            }
            Furniture(furniture, item, plan);
        }
    }

    /// <summary>El color de afuera del local: el del tono de su lote en la Calle Principal.</summary>
    private static StandardMaterial3D FacadeMaterial(string code)
    {
        var lot = MainStreet.Lots.FirstOrDefault(l => l.Code == code);
        var tone = CityLayout.DowntownPalette[(lot?.Tone ?? 0) % CityLayout.DowntownPalette.Length];
        return Mat(new Color(tone.R, tone.G, tone.B), 0.9f);
    }

    private static StandardMaterial3D FloorMaterial(string kind) => kind switch
    {
        "parquet" => Mat(new Color(0.45f, 0.3f, 0.18f), 0.6f),
        "baldosa" => Mat(new Color(0.62f, 0.55f, 0.46f), 0.7f),
        "damero" => Checker(new Color(0.92f, 0.9f, 0.85f), new Color(0.12f, 0.12f, 0.13f)),
        _ => Mat(new Color(0.45f, 0.45f, 0.44f), 0.95f),
    };

    private static StandardMaterial3D WallMaterial(string kind) => kind switch
    {
        "ladrillo" => Mat(new Color(0.5f, 0.27f, 0.2f)),
        "chapa" => Mat(new Color(0.42f, 0.46f, 0.48f), 0.5f, 0.3f),
        "empapelado" => Mat(new Color(0.36f, 0.42f, 0.33f)),
        "madera" => Mat(new Color(0.48f, 0.34f, 0.2f)),
        "azulejo" => Mat(new Color(0.86f, 0.88f, 0.85f), 0.3f),
        "cromado" => Mat(new Color(0.9f, 0.86f, 0.76f), 0.4f),
        _ => Mat(Cream),
    };

    private static StandardMaterial3D Checker(Color a, Color b)
    {
        var key = $"checker|{a.ToHtml()}|{b.ToHtml()}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        // 64×64 con mipmaps: de lejos no hace moiré. Cada textura tiene 2×2 baldosas de medio metro.
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                image.SetPixel(x, y, (x < 32) == (y < 32) ? a : b);
        image.GenerateMipmaps();
        var m = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Scale = new Vector3(16, 10, 1), Roughness = 0.4f,
        };
        return Cache[key] = m;
    }

    // ---------------- Muebles ----------------

    private static void Furniture(Node3D room, Distrito.Server.Core.World.InteriorItem item, InteriorPlan plan)
    {
        var node = new Node3D { Name = $"{item.Type}", Position = new Vector3(item.X, 0, item.Z), Rotation = new Vector3(0, Mathf.DegToRad(item.Rotation), 0) };
        room.AddChild(node);
        var path = $"{AssetFolder}/{item.Type}.glb";
        var box = Sizes.GetValueOrDefault(item.Type, new Vector3(0.6f, 0.6f, 0.6f));
        if (ModelLibrary.Make("muebles", item.Type, box, ModelLibrary.Fit.Inside) is { } glb)
            node.AddChild(glb); // game/modelos/muebles/<tipo>.glb, escalado para que entre en su caja
        else if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path) is { } model)
            node.AddChild(model.Instantiate<Node3D>());
        else
        {
            Greybox(node, item, plan);
            MeshMerge.All(node); // rendimiento: cada mueble provisorio en una sola malla
        }
        if (!NoCollision.Contains(item.Type) && Sizes.TryGetValue(item.Type, out var size))
        {
            var body = new StaticBody3D { Name = "Colision" };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2, 0) });
            node.AddChild(body);
        }
    }

    private static void Greybox(Node3D n, Distrito.Server.Core.World.InteriorItem item, InteriorPlan plan)
    {
        var s = Sizes.GetValueOrDefault(item.Type, new Vector3(0.5f, 0.5f, 0.5f));
        switch (item.Type)
        {
            case "mostrador":
                Box(n, new Vector3(s.X, s.Y - 0.06f, s.Z), new Vector3(0, (s.Y - 0.06f) / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(s.X + 0.1f, 0.06f, s.Z + 0.1f), new Vector3(0, s.Y - 0.03f, 0), Mat(Wood, 0.4f));
                break;
            case "caja-registradora":
                Box(n, new Vector3(0.45f, 0.25f, 0.4f), new Vector3(0, 1.17f, 0), Mat(Chrome, 0.3f, 0.7f));
                Box(n, new Vector3(0.35f, 0.12f, 0.06f), new Vector3(0, 1.36f, -0.12f), Mat(Chrome, 0.3f, 0.7f));
                break;
            case "balanza":
                Box(n, new Vector3(0.35f, 0.08f, 0.3f), new Vector3(0, 1.09f, 0), Mat(Red, 0.5f));
                Cylinder(n, 0.12f, 0.03f, new Vector3(0, 1.2f, 0), Mat(Chrome, 0.3f, 0.8f));
                break;
            case "escritorio":
                Box(n, new Vector3(s.X, 0.06f, s.Z), new Vector3(0, s.Y - 0.03f, 0), Mat(Wood, 0.4f));
                Box(n, new Vector3(0.5f, s.Y - 0.06f, s.Z - 0.1f), new Vector3(-s.X / 2 + 0.28f, (s.Y - 0.06f) / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(0.5f, s.Y - 0.06f, s.Z - 0.1f), new Vector3(s.X / 2 - 0.28f, (s.Y - 0.06f) / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(0.35f, 0.3f, 0.25f), new Vector3(0.4f, s.Y + 0.15f, 0), Mat(new Color(0.12f, 0.12f, 0.12f))); // teléfono / máquina de escribir
                break;
            case "archivo":
            case "caja-fuerte":
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(item.Type == "archivo" ? new Color(0.38f, 0.42f, 0.36f) : new Color(0.2f, 0.22f, 0.24f), 0.5f, 0.4f));
                for (var i = 0; i < 3; i++)
                    Box(n, new Vector3(0.15f, 0.04f, 0.04f), new Vector3(0, s.Y * (0.25f + 0.3f * i), s.Z / 2 + 0.01f), Mat(Chrome, 0.3f, 0.8f));
                break;
            case "estante-alto":
                foreach (var x in new[] { -s.X / 2, 0f, s.X / 2 })
                    foreach (var z in new[] { -s.Z / 2, s.Z / 2 })
                        Box(n, new Vector3(0.08f, s.Y, 0.08f), new Vector3(x, s.Y / 2, z), Mat(new Color(0.75f, 0.42f, 0.12f)));
                for (var level = 0; level < 4; level++)
                {
                    var y = 0.15f + level * 1.1f;
                    Box(n, new Vector3(s.X, 0.05f, s.Z), new Vector3(0, y, 0), Mat(new Color(0.55f, 0.4f, 0.25f)));
                    for (var k = 0; k < 6; k++)
                        Box(n, new Vector3(0.6f, 0.45f, 0.6f), new Vector3(-s.X / 2 + 0.5f + k * 0.8f, y + 0.25f, ((k + level) % 2 - 0.5f) * 0.15f),
                            Mat((k + level) % 3 == 0 ? new Color(0.6f, 0.47f, 0.3f) : new Color(0.5f, 0.38f, 0.24f)));
                }
                break;
            case "estante":
            case "estante-frascos":
                Box(n, new Vector3(s.X, s.Y, 0.05f), new Vector3(0, s.Y / 2, -s.Z / 2 + 0.03f), Mat(DarkWood));
                for (var level = 0; level < 4; level++)
                {
                    var y = 0.3f + level * 0.5f;
                    Box(n, new Vector3(s.X, 0.04f, s.Z), new Vector3(0, y, 0), Mat(Wood));
                    for (var k = 0; k < 8; k++)
                    {
                        var x = -s.X / 2 + 0.17f + k * (s.X - 0.34f) / 7;
                        var color = Color.FromHsv((k * 0.13f + level * 0.27f) % 1f, 0.45f, 0.7f);
                        if (item.Type == "estante-frascos")
                            Cylinder(n, 0.07f, 0.22f, new Vector3(x, y + 0.13f, 0), Mat(color, 0.15f, 0f, 0.05f));
                        else
                            Box(n, new Vector3(0.18f, 0.28f, 0.2f), new Vector3(x, y + 0.16f, 0), Mat(color, 0.7f));
                    }
                }
                break;
            case "porton":
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(new Color(0.4f, 0.43f, 0.45f), 0.5f, 0.4f));
                for (var i = 1; i < 12; i++)
                    Box(n, new Vector3(s.X, 0.03f, 0.04f), new Vector3(0, i * s.Y / 12, 0.11f), Mat(new Color(0.3f, 0.32f, 0.34f)));
                n.AddChild(new Label3D { Text = "CARGA Y DESCARGA", FontSize = 48, PixelSize = 0.008f, OutlineSize = 8, Position = new Vector3(0, s.Y + 0.35f, 0.15f), Modulate = new Color(0.95f, 0.8f, 0.3f) });
                break;
            case "pallet":
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(new Color(0.62f, 0.5f, 0.32f)));
                break;
            case "carretilla":
                Box(n, new Vector3(0.5f, 0.05f, 0.4f), new Vector3(0, 0.08f, 0.1f), Mat(Red));
                Box(n, new Vector3(0.05f, 1.2f, 0.05f), new Vector3(-0.22f, 0.6f, -0.1f), Mat(Red));
                Box(n, new Vector3(0.05f, 1.2f, 0.05f), new Vector3(0.22f, 0.6f, -0.1f), Mat(Red));
                break;
            case "elevador-auto":
                foreach (var x in new[] { -s.X / 2 + 0.2f, s.X / 2 - 0.2f })
                    Box(n, new Vector3(0.3f, s.Y + 1f, 0.3f), new Vector3(x, (s.Y + 1f) / 2, -s.Z / 2 - 0.2f), Mat(new Color(0.85f, 0.65f, 0.1f)));
                Box(n, new Vector3(s.X - 0.6f, 0.12f, s.Z - 0.6f), new Vector3(0, 1.3f, 0), Mat(new Color(0.3f, 0.3f, 0.32f)));
                // Un sedán de 1950 en reparación (sin ruedas de un lado).
                Box(n, new Vector3(4.2f, 0.6f, 1.8f), new Vector3(0, 1.75f, 0), Mat(new Color(0.35f, 0.15f, 0.12f), 0.35f, 0.15f));
                Box(n, new Vector3(2f, 0.55f, 1.6f), new Vector3(-0.2f, 2.3f, 0), Mat(Cream, 0.35f));
                Box(n, new Vector3(4.3f, 0.1f, 1.7f), new Vector3(0, 1.43f, 0), Mat(Chrome, 0.2f, 0.9f));
                break;
            case "banco-trabajo":
                Box(n, new Vector3(s.X, 0.08f, s.Z), new Vector3(0, s.Y - 0.04f, 0), Mat(Wood));
                Box(n, new Vector3(s.X - 0.1f, 0.05f, s.Z - 0.1f), new Vector3(0, 0.25f, 0), Mat(DarkWood));
                foreach (var x in new[] { -s.X / 2 + 0.06f, s.X / 2 - 0.06f })
                    Box(n, new Vector3(0.08f, s.Y, s.Z - 0.05f), new Vector3(x, s.Y / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(0.3f, 0.2f, 0.2f), new Vector3(-0.6f, s.Y + 0.1f, 0), Mat(new Color(0.2f, 0.35f, 0.5f), 0.5f, 0.5f)); // morsa
                break;
            case "pared-herramientas":
                Box(n, s, new Vector3(0, 1.6f, 0), Mat(new Color(0.55f, 0.45f, 0.3f)));
                for (var i = 0; i < 9; i++)
                    Box(n, new Vector3(0.06f, 0.4f + (i % 3) * 0.1f, 0.04f), new Vector3(-1.2f + i * 0.3f, 1.6f + (i % 2) * 0.4f, 0.07f), Mat(Metal, 0.4f, 0.7f));
                break;
            case "puerta-trasera":
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(DarkWood));
                Label(n, item.Label ?? "", new Vector3(0, s.Y + 0.25f, 0.08f), 32, new Color(0.9f, 0.85f, 0.7f));
                break;
            case "cartel":
                Label(n, item.Label ?? plan.Name, new Vector3(0, plan.Height - 0.8f, 0), 56, new Color(0.98f, 0.86f, 0.55f));
                break;
            case "cuadro":
                Box(n, new Vector3(1.2f, 0.9f, 0.05f), new Vector3(0, 2.1f, 0), Mat(new Color(0.55f, 0.42f, 0.18f), 0.4f, 0.3f));
                Box(n, new Vector3(1f, 0.7f, 0.02f), new Vector3(0, 2.1f, 0.03f), Mat(new Color(0.3f, 0.26f, 0.22f)));
                Label(n, item.Label ?? "", new Vector3(0, 1.5f, 0.06f), 24, new Color(0.9f, 0.85f, 0.7f));
                break;
            case "lampara-colgante":
                var h = plan.Height;
                Box(n, new Vector3(0.02f, 1f, 0.02f), new Vector3(0, h - 0.5f, 0), Mat(new Color(0.1f, 0.1f, 0.1f)));
                Cylinder(n, 0.3f, 0.2f, new Vector3(0, h - 1.05f, 0), Mat(new Color(0.2f, 0.32f, 0.24f), 0.5f, 0.3f), top: 0.08f);
                Cylinder(n, 0.08f, 0.1f, new Vector3(0, h - 1.15f, 0), Mat(new Color(1f, 0.9f, 0.7f), 1f, 0f, 3f));
                n.AddChild(new OmniLight3D { Position = new Vector3(0, h - 1.3f, 0), LightColor = new Color(1f, 0.85f, 0.62f), LightEnergy = 2.2f, OmniRange = 8f, ShadowEnabled = true });
                break;
            case "sillon":
                Box(n, new Vector3(s.X, 0.45f, s.Z), new Vector3(0, 0.22f, 0), Mat(new Color(0.42f, 0.14f, 0.14f), 0.9f));
                Box(n, new Vector3(s.X, 0.5f, 0.2f), new Vector3(0, 0.65f, -s.Z / 2 + 0.1f), Mat(new Color(0.42f, 0.14f, 0.14f), 0.9f));
                break;
            case "planta":
                Cylinder(n, 0.2f, 0.35f, new Vector3(0, 0.18f, 0), Mat(new Color(0.55f, 0.3f, 0.2f)));
                Box(n, new Vector3(0.5f, 0.9f, 0.5f), new Vector3(0, 0.8f, 0), Mat(new Color(0.2f, 0.4f, 0.2f)));
                break;
            case "heladera":
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(new Color(0.92f, 0.92f, 0.88f), 0.25f));
                Box(n, new Vector3(0.05f, 0.4f, 0.05f), new Vector3(s.X / 2 - 0.15f, 1.2f, s.Z / 2 + 0.03f), Mat(Chrome, 0.2f, 0.9f));
                break;
            case "vidriera":
                Box(n, new Vector3(s.X, 0.5f, s.Z), new Vector3(0, 0.25f, 0), Mat(DarkWood));
                Box(n, new Vector3(s.X, 0.5f, s.Z), new Vector3(0, 0.75f, 0), Mat(new Color(0.7f, 0.85f, 0.9f), 0.05f, 0.1f, 0.15f));
                break;
            case "mesa-corte":
                Box(n, new Vector3(s.X, 0.08f, s.Z), new Vector3(0, s.Y - 0.04f, 0), Mat(Wood));
                Box(n, new Vector3(1.2f, 0.02f, 0.6f), new Vector3(0.2f, s.Y + 0.01f, 0), Mat(new Color(0.2f, 0.22f, 0.35f))); // tela
                Box(n, new Vector3(s.X - 0.2f, s.Y - 0.08f, s.Z - 0.2f), new Vector3(0, (s.Y - 0.08f) / 2, 0), Mat(DarkWood));
                break;
            case "maniqui":
                Cylinder(n, 0.18f, 0.04f, new Vector3(0, 0.02f, 0), Mat(DarkWood));
                Box(n, new Vector3(0.03f, 0.9f, 0.03f), new Vector3(0, 0.45f, 0), Mat(DarkWood));
                Box(n, new Vector3(0.42f, 0.6f, 0.25f), new Vector3(0, 1.2f, 0), Mat(new Color(0.18f, 0.2f, 0.26f))); // saco
                Box(n, new Vector3(0.08f, 0.25f, 0.02f), new Vector3(0, 1.32f, 0.13f), Mat(Red)); // corbata
                Cylinder(n, 0.14f, 0.06f, new Vector3(0, 1.62f, 0), Mat(new Color(0.15f, 0.13f, 0.12f))); // sombrero
                break;
            case "perchero":
                Box(n, new Vector3(s.X, 0.04f, 0.04f), new Vector3(0, s.Y, 0), Mat(Chrome, 0.2f, 0.9f));
                foreach (var x in new[] { -s.X / 2, s.X / 2 })
                    Box(n, new Vector3(0.04f, s.Y, 0.04f), new Vector3(x, s.Y / 2, 0), Mat(Chrome, 0.2f, 0.9f));
                for (var i = 0; i < 6; i++)
                    Box(n, new Vector3(0.12f, 0.9f, 0.45f), new Vector3(-s.X / 2 + 0.2f + i * 0.24f, s.Y - 0.5f, 0), Mat(Color.FromHsv(0.05f + i * 0.11f, 0.35f, 0.4f)));
                break;
            case "espejo":
                Box(n, s, new Vector3(0, 1.1f, 0), Mat(new Color(0.55f, 0.42f, 0.18f), 0.4f, 0.3f));
                Box(n, new Vector3(s.X - 0.1f, s.Y - 0.1f, 0.02f), new Vector3(0, 1.1f, 0.04f), Mat(new Color(0.8f, 0.85f, 0.88f), 0.02f, 1f));
                break;
            case "probador":
                Box(n, new Vector3(0.05f, s.Y, s.Z), new Vector3(-s.X / 2, s.Y / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(0.05f, s.Y, s.Z), new Vector3(s.X / 2, s.Y / 2, 0), Mat(DarkWood));
                Box(n, new Vector3(s.X, 2f, 0.04f), new Vector3(0, 1.1f, s.Z / 2), Mat(new Color(0.5f, 0.12f, 0.15f), 1f)); // cortina
                break;
            case "barra":
                Box(n, new Vector3(s.X, s.Y - 0.08f, s.Z), new Vector3(0, (s.Y - 0.08f) / 2, 0), Mat(Red, 0.4f));
                Box(n, new Vector3(s.X + 0.1f, 0.08f, s.Z + 0.15f), new Vector3(0, s.Y - 0.04f, 0), Mat(Chrome, 0.2f, 0.9f));
                Box(n, new Vector3(s.X, 0.06f, 0.02f), new Vector3(0, 0.35f, s.Z / 2 + 0.01f), Mat(Chrome, 0.2f, 0.9f));
                Box(n, new Vector3(s.X, 1.6f, 0.4f), new Vector3(0, 1.6f, -1.3f), Mat(new Color(0.75f, 0.75f, 0.72f), 0.3f, 0.6f)); // la cocina atrás
                break;
            case "taburete":
                Cylinder(n, 0.05f, s.Y - 0.08f, new Vector3(0, (s.Y - 0.08f) / 2, 0), Mat(Chrome, 0.2f, 0.9f));
                Cylinder(n, 0.2f, 0.08f, new Vector3(0, s.Y - 0.04f, 0), Mat(Red, 0.5f));
                break;
            case "reservado":
                Box(n, new Vector3(s.X, 0.45f, 0.45f), new Vector3(0, 0.22f, -s.Z / 2 + 0.22f), Mat(Red, 0.5f));
                Box(n, new Vector3(s.X, 0.6f, 0.15f), new Vector3(0, 0.75f, -s.Z / 2 + 0.07f), Mat(Red, 0.5f));
                Box(n, new Vector3(s.X, 0.45f, 0.45f), new Vector3(0, 0.22f, s.Z / 2 - 0.22f), Mat(Red, 0.5f));
                Box(n, new Vector3(s.X, 0.6f, 0.15f), new Vector3(0, 0.75f, s.Z / 2 - 0.07f), Mat(Red, 0.5f));
                Box(n, new Vector3(s.X - 0.2f, 0.05f, 0.5f), new Vector3(0, 0.75f, 0), Mat(Cream, 0.3f));
                break;
            case "rocola":
                Box(n, new Vector3(s.X, s.Y - 0.3f, s.Z), new Vector3(0, (s.Y - 0.3f) / 2, 0), Mat(new Color(0.55f, 0.25f, 0.1f), 0.3f));
                Cylinder(n, s.X / 2, s.Z, new Vector3(0, s.Y - 0.3f, 0), Mat(new Color(1f, 0.55f, 0.2f), 0.3f, 0f, 1.5f), rotation: new Vector3(Mathf.Pi / 2, 0, 0));
                Box(n, new Vector3(s.X - 0.2f, 0.5f, 0.02f), new Vector3(0, 0.9f, s.Z / 2 + 0.01f), Mat(new Color(0.3f, 0.8f, 1f), 0.2f, 0f, 1.2f));
                n.AddChild(new OmniLight3D { Position = new Vector3(0, 1.2f, 0.6f), LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1f, OmniRange = 3f });
                break;
            case "mesa":
                Cylinder(n, 0.45f, 0.04f, new Vector3(0, s.Y - 0.02f, 0), Mat(Cream, 0.3f));
                Cylinder(n, 0.05f, s.Y - 0.04f, new Vector3(0, (s.Y - 0.04f) / 2, 0), Mat(Chrome, 0.2f, 0.9f));
                break;
            default:
                Box(n, s, new Vector3(0, s.Y / 2, 0), Mat(new Color(0.5f, 0.5f, 0.5f)));
                break;
        }
    }

    // ---------------- Ayudas ----------------

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 at, Material mat, Vector3? rotation = null)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = at, Rotation = rotation ?? Vector3.Zero };
        parent.AddChild(mesh);
        return mesh;
    }

    public static MeshInstance3D Cylinder(Node3D parent, float radius, float height, Vector3 at, Material mat, float? top = null, Vector3? rotation = null)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top ?? radius, BottomRadius = radius, Height = height, RadialSegments = 16 },
            MaterialOverride = mat, Position = at, Rotation = rotation ?? Vector3.Zero,
        };
        parent.AddChild(mesh);
        return mesh;
    }

    private static void Label(Node3D parent, string text, Vector3 at, int size, Color color) =>
        parent.AddChild(new Label3D { Text = text, FontSize = size, PixelSize = 0.008f, OutlineSize = 8, Position = at, Modulate = color, OutlineModulate = new Color(0.08f, 0.06f, 0.05f) });

    private static void Solid(Node3D parent, string name, Vector3 size, Vector3 at, Material mat)
    {
        var body = new StaticBody3D { Name = name, Position = at };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }
}
