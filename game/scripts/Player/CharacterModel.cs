using Distrito.Shared.Api;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Modelo provisional del personaje armado con piezas simples, según las láminas de diseño del equipo
/// (Ejecutivo, Operativo, Trabajador, Asociado): silueta, ropa y colores de cada paleta.
/// Mira hacia −Z (como el resto del juego). Tiene piernas y brazos con pivote para la caminata.
/// Se reemplaza por modelos 3D definitivos (.glb con esqueleto) cuando estén.
/// </summary>
public partial class CharacterModel : Node3D
{
    private Node3D _legL = null!, _legR = null!, _armL = null!, _armR = null!;
    private float _phase;

    public string LookCode { get; private set; } = "";

    public static CharacterModel Build(CharacterLook look)
    {
        var m = new CharacterModel { Name = "Model", LookCode = look.Code };
        var skin = Mat(look.Skin);
        var hair = Mat(look.Hair);
        var shirt = Mat(look.Shirt);
        var pants = Mat(look.Pants);
        var top = Mat(look.Top);
        var accent = Mat(look.Accent);
        var shoes = Mat(look.Shoes, 0.4f);
        var sleeves = look.Jacket || look.LongCoat ? top : shirt;

        // Piernas (pivote en la cadera) con zapatos.
        m._legL = m.Pivot(new Vector3(-0.1f, 0.92f, 0));
        m._legR = m.Pivot(new Vector3(0.1f, 0.92f, 0));
        foreach (var leg in new[] { m._legL, m._legR })
        {
            Add(leg, Capsule(0.085f, 0.86f), pants, new Vector3(0, -0.45f, 0));
            Add(leg, Box(0.13f, 0.09f, 0.27f), shoes, new Vector3(0, -0.88f, -0.04f));
        }

        // Torso y cadera.
        Add(m, Box(0.4f, 0.22f, 0.24f), pants, new Vector3(0, 0.93f, 0));
        Add(m, Box(0.42f, 0.56f, 0.24f), shirt, new Vector3(0, 1.3f, 0));
        Add(m, Box(0.16f, 0.06f, 0.2f), shirt, new Vector3(0, 1.58f, -0.01f)); // cuello de camisa

        if (look.Vest)
            Add(m, Box(0.44f, 0.46f, 0.26f), top, new Vector3(0, 1.24f, 0));
        if (look.Jacket)
            Add(m, Box(0.48f, 0.64f, 0.29f), top, new Vector3(0, 1.22f, 0));
        if (look.LongCoat)
        {
            Add(m, Box(0.5f, 0.62f, 0.3f), top, new Vector3(0, 1.26f, 0));
            Add(m, Box(0.54f, 0.72f, 0.32f), top, new Vector3(0, 0.6f, 0.01f)); // faldón hasta las rodillas
        }
        if (look.Vest || look.LongCoat)
            Add(m, Box(0.05f, 0.26f, 0.01f), accent, new Vector3(0, 1.4f, -0.135f)); // corbata
        if (look.LongCoat)
            Add(m, Box(0.07f, 0.04f, 0.01f), Mat("#e8dcc8"), new Vector3(0.15f, 1.44f, -0.16f)); // pañuelo
        if (look.Suspenders)
        {
            foreach (var x in new[] { -0.11f, 0.11f })
            {
                Add(m, Box(0.04f, 0.52f, 0.01f), accent, new Vector3(x, 1.3f, -0.125f));
                Add(m, Box(0.04f, 0.52f, 0.01f), accent, new Vector3(x, 1.3f, 0.125f));
            }
            Add(m, Box(0.42f, 0.05f, 0.25f), Mat("#3a2a1e"), new Vector3(0, 1.04f, 0)); // cinturón
        }
        if (look.Jacket)
            Add(m, Box(0.49f, 0.05f, 0.3f), Mat("#3a2a1e"), new Vector3(0, 1.0f, 0));

        // Brazos (pivote en el hombro) con manos.
        m._armL = m.Pivot(new Vector3(-0.27f, 1.52f, 0));
        m._armR = m.Pivot(new Vector3(0.27f, 1.52f, 0));
        foreach (var arm in new[] { m._armL, m._armR })
        {
            Add(arm, Capsule(0.065f, 0.62f), sleeves, new Vector3(0, -0.3f, 0));
            Add(arm, Sphere(0.055f), skin, new Vector3(0, -0.64f, 0));
        }

        // Cabeza, pelo, bigote y gorra.
        Add(m, Capsule(0.055f, 0.14f), skin, new Vector3(0, 1.62f, 0));
        var head = Add(m, Sphere(0.12f), skin, new Vector3(0, 1.74f, 0));
        head.Scale = new Vector3(0.92f, 1.08f, 1f);
        var hairCap = Add(m, Sphere(0.125f), hair, new Vector3(0, 1.79f, 0.015f));
        hairCap.Scale = new Vector3(0.95f, 0.7f, 1f);
        if (look.Mustache)
            Add(m, Box(0.09f, 0.018f, 0.02f), hair, new Vector3(0, 1.69f, -0.115f));
        if (look.Hat is { } hatColor)
        {
            var hat = Mat(hatColor);
            var crown = Add(m, Cylinder(0.14f, 0.07f), hat, new Vector3(0, 1.86f, 0.005f));
            crown.Scale = new Vector3(1.05f, 1f, 1.12f);
            Add(m, Box(0.2f, 0.02f, 0.1f), hat, new Vector3(0, 1.835f, -0.13f));
        }
        return m;
    }

    /// <summary>Caminata: balanceo de piernas y brazos según la velocidad horizontal (m/s).</summary>
    public void Animate(float speed, float delta)
    {
        var amount = Mathf.Clamp(speed / 4.5f, 0f, 1f);
        _phase += delta * (4f + 6f * amount);
        var swing = Mathf.Sin(_phase) * 0.6f * amount;
        _legL.Rotation = new Vector3(swing, 0, 0);
        _legR.Rotation = new Vector3(-swing, 0, 0);
        _armL.Rotation = new Vector3(-swing * 0.7f, 0, 0);
        _armR.Rotation = new Vector3(swing * 0.7f, 0, 0);
        if (amount < 0.05f)
            _phase = 0;
    }

    private Node3D Pivot(Vector3 at)
    {
        var p = new Node3D { Position = at };
        AddChild(p);
        return p;
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Vector3 at)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at };
        parent.AddChild(mi);
        return mi;
    }

    private static StandardMaterial3D Mat(string hex, float roughness = 0.85f) =>
        new() { AlbedoColor = new Color(hex), Roughness = roughness };

    private static BoxMesh Box(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };
    private static CapsuleMesh Capsule(float radius, float height) => new() { Radius = radius, Height = height, RadialSegments = 12, Rings = 4 };
    private static SphereMesh Sphere(float radius) => new() { Radius = radius, Height = radius * 2, RadialSegments = 16, Rings = 8 };
    private static CylinderMesh Cylinder(float radius, float height) => new() { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 16 };
}
