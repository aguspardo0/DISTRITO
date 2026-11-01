using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Bultos agrupados para mostrar existencias (nunca uno por unidad): cajones de madera, bolsas, cajones de bebidas,
/// cajas de munición. La cantidad de bultos sale de <see cref="StockVisualRules"/>.
/// </summary>
public static class StockProps
{
    private static readonly Dictionary<string, StandardMaterial3D> Materials = new();

    private static StandardMaterial3D Mat(string key, Color color)
    {
        if (!Materials.TryGetValue(key, out var m))
            Materials[key] = m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f };
        return m;
    }

    /// <summary>Tamaño de un bulto según el objeto (para acomodarlos).</summary>
    public static Vector3 SizeOf(string itemCode, string kind) => itemCode switch
    {
        EconomyCatalog.CoffeeBagItem => new Vector3(0.5f, 0.32f, 0.38f),
        EconomyCatalog.DrinksCrateItem => new Vector3(0.5f, 0.34f, 0.4f),
        _ when kind == ItemKinds.Ammo => new Vector3(0.36f, 0.2f, 0.26f),
        _ when kind == ItemKinds.Weapon => new Vector3(0.9f, 0.14f, 0.3f),
        _ => new Vector3(0.55f, 0.42f, 0.45f),
    };

    public static Node3D Build(string itemCode, string kind)
    {
        var size = SizeOf(itemCode, kind);
        var node = new Node3D();
        switch (itemCode)
        {
            case EconomyCatalog.CoffeeBagItem:
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { Radius = size.Y / 2, Height = size.X },
                    MaterialOverride = Mat("bolsa", new Color(0.55f, 0.45f, 0.3f)),
                    Position = new Vector3(0, size.Y / 2, 0), Rotation = new Vector3(0, 0, Mathf.Pi / 2),
                });
                break;
            case EconomyCatalog.DrinksCrateItem:
                node.AddChild(Box(size with { Y = size.Y * 0.7f }, new Vector3(0, size.Y * 0.35f, 0), Mat("cajon", new Color(0.36f, 0.24f, 0.14f))));
                for (var i = 0; i < 4; i++)
                    node.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.04f, Height = 0.2f },
                        MaterialOverride = Mat("botella", new Color(0.15f, 0.3f, 0.18f)),
                        Position = new Vector3(-0.15f + 0.1f * i, size.Y * 0.8f, 0),
                    });
                break;
            default:
                var color = kind switch
                {
                    ItemKinds.Ammo => new Color(0.24f, 0.28f, 0.2f),
                    ItemKinds.Weapon => new Color(0.18f, 0.16f, 0.14f),
                    _ => new Color(0.5f, 0.37f, 0.22f),
                };
                node.AddChild(Box(size, new Vector3(0, size.Y / 2, 0), Mat($"caja-{kind}", color)));
                if (kind is not (ItemKinds.Ammo or ItemKinds.Weapon))
                    node.AddChild(Box(new Vector3(size.X + 0.01f, 0.05f, 0.06f), new Vector3(0, size.Y * 0.75f, 0), Mat("listón", new Color(0.4f, 0.29f, 0.17f))));
                break;
        }
        return node;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 at, Material mat) =>
        new() { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = at };

    /// <summary>
    /// Acomoda las pilas en un área (centro en el origen del contenedor): filas a lo ancho, avanzando a lo largo y apilando.
    /// </summary>
    public static void Layout(Node3D parent, IEnumerable<StockPile> piles, float width, float length, int levels)
    {
        float x = -width / 2, z = -length / 2, rowDepth = 0;
        var level = 0;
        foreach (var pile in piles)
        {
            var size = SizeOf(pile.ItemCode, pile.Kind);
            for (var i = 0; i < StockVisualRules.Props(pile); i++)
            {
                if (x + size.X > width / 2 + 0.01f)
                {
                    x = -width / 2;
                    z += rowDepth + 0.05f;
                    rowDepth = 0;
                }
                if (z + size.Z > length / 2 + 0.01f)
                {
                    level++;
                    x = -width / 2;
                    z = -length / 2;
                    rowDepth = 0;
                    if (level >= levels)
                        return; // no entra más a la vista (los datos siguen siendo los reales)
                }
                var prop = Build(pile.ItemCode, pile.Kind);
                prop.Position = new Vector3(x + size.X / 2, level * 0.45f, z + size.Z / 2);
                parent.AddChild(prop);
                x += size.X + 0.05f;
                rowDepth = Mathf.Max(rowDepth, size.Z);
            }
        }
    }
}
