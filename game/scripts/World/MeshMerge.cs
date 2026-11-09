using Godot;
using Array = Godot.Collections.Array;

namespace Distrito.Game.World;

/// <summary>
/// Junta piezas simples en una sola malla con colores por vértice (rendimiento: una persona pasa de ~20 llamadas de
/// dibujo a 5 y un auto de ~40 a 3 o 4). Las piezas que brillan o son transparentes (faros, vidrios, balizas) quedan
/// aparte, igual que las que tienen nombre propio (las que se animan o se prenden y apagan).
/// </summary>
public static class MeshMerge
{
    /// <summary>Para comparar en la medición de rendimiento: con DISTRITO_SIN_UNIR=1 no junta nada.</summary>
    private static readonly bool Disabled = OS.GetEnvironment("DISTRITO_SIN_UNIR") == "1";

    private static readonly StandardMaterial3D Painted = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.75f };

    /// <summary>Junta los hijos directos de <paramref name="parent"/> que son mallas sueltas (sin hijos propios).</summary>
    public static void Children(Node3D parent)
    {
        var parts = parent.GetChildren().OfType<MeshInstance3D>().Where(m => m.GetChildCount() == 0 && Mergeable(m)).ToList();
        Merge(parent, parts.Select(p => (p, p.Transform)).ToList());
    }

    /// <summary>Junta todas las mallas que cuelgan de <paramref name="root"/> (un modelo quieto: un auto, un puesto).</summary>
    public static void All(Node3D root)
    {
        var parts = new List<(MeshInstance3D, Transform3D)>();
        void Walk(Node node, Transform3D t)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D n)
                    continue;
                var here = t * n.Transform;
                if (n is MeshInstance3D m && m.GetChildCount() == 0 && Mergeable(m) && !m.Name.ToString().StartsWith("Baliza"))
                    parts.Add((m, here));
                else
                    Walk(n, here);
            }
        }
        Walk(root, Transform3D.Identity);
        Merge(root, parts);
    }

    private static bool Mergeable(MeshInstance3D m)
    {
        if (m.Mesh is null || !m.Visible)
            return false;
        var material = m.MaterialOverride as StandardMaterial3D ?? (m.Mesh.GetSurfaceCount() > 0 ? m.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D : null);
        if (material is null)
            return m.MaterialOverride is null;
        return !material.EmissionEnabled && material.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && material.AlbedoTexture is null;
    }

    private static void Merge(Node3D root, List<(MeshInstance3D Mesh, Transform3D At)> parts)
    {
        if (parts.Count < 2 || Disabled)
            return;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        foreach (var (mi, at) in parts)
        {
            var color = (mi.MaterialOverride as StandardMaterial3D)?.AlbedoColor ?? Colors.White;
            var normalBasis = at.Basis.Inverse().Transposed();
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(s);
                if (arrays.Count <= (int)Mesh.ArrayType.Index)
                    continue;
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var norms = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var start = vertices.Count;
                for (var i = 0; i < verts.Length; i++)
                {
                    vertices.Add(at * verts[i]);
                    normals.Add(i < norms.Length ? (normalBasis * norms[i]).Normalized() : Vector3.Up);
                    colors.Add(color);
                }
                if (idx.Length > 0)
                    foreach (var k in idx)
                        indices.Add(start + k);
                else
                    for (var i = 0; i < verts.Length; i++)
                        indices.Add(start + i);
            }
        }
        if (vertices.Count == 0)
            return;
        var data = new Array();
        data.Resize((int)Mesh.ArrayType.Max);
        data[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        data[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        data[(int)Mesh.ArrayType.Color] = colors.ToArray();
        data[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data);
        mesh.SurfaceSetMaterial(0, Painted);
        foreach (var (mi, _) in parts)
        {
            mi.GetParent()?.RemoveChild(mi);
            mi.Free();
        }
        root.AddChild(new MeshInstance3D { Name = "Piezas", Mesh = mesh });
    }
}
