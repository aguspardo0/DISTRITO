using System.Text.Json;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Modelos 3D sin tocar código (pedido del equipo, 2026-11-09: los hacen con Meshy). Se tira un .glb en
/// <c>game/modelos/&lt;tipo&gt;/&lt;código&gt;.glb</c> y el juego lo usa en lugar de la forma provisoria:
///
///   personas/  trabajador, ejecutivo, operativo, asociado, vecino, vecina, policia (o el código de un NPC fijo)
///   autos/     camioneta, camion (los códigos de los vehículos)
///   muebles/   mostrador, estante-alto, mesa… (los tipos de los muebles de los interiores)
///   objetos/   cajon, barril, tacho, caja, neumatico, bolsa (los objetos con física)
///   edificios/ hotel-roma, club-jazz, barberia… (las fachadas de la Calle Principal)
///
/// Variantes: <c>vecino.glb</c>, <c>vecino-2.glb</c>, <c>vecino-3.glb</c>… (se reparte una por persona, siempre la misma).
/// El juego lo escala solo (una persona mide 1,8 m; un auto, lo que mide su tipo; un mueble entra en su caja), lo apoya
/// en el piso y lo gira para que mire hacia adelante. La colisión sigue siendo la de siempre. Si algo queda mal, se
/// corrige sin código en <c>game/modelos/ajustes.json</c> (ver <see cref="Adjust"/>).
/// </summary>
public static class ModelLibrary
{
    public const string Root = "res://modelos";

    /// <summary>
    /// Ajuste de un modelo en <c>ajustes.json</c>, por clave «tipo/código» (o «tipo» para todos los de esa carpeta):
    /// <c>{ "personas/vecino": { "girar": 0, "altura": 1.75, "subir": 0.02, "escala": 1.0 } }</c>.
    /// «girar»: grados (por defecto 180, porque los .glb suelen mirar a +Z y el juego mira a −Z).
    /// </summary>
    public sealed record Adjust(float? Girar = null, float? Altura = null, float? Subir = null, float? Escala = null);

    private static Dictionary<string, List<string>>? _index;
    private static Dictionary<string, Adjust> _adjust = new();
    private static readonly Dictionary<string, PackedScene?> Loaded = new();

    /// <summary>Cuántos modelos hay (para el registro de arranque).</summary>
    public static int Count => Index.Sum(kv => kv.Value.Count);

    private static Dictionary<string, List<string>> Index => _index ??= Scan();

    private static Dictionary<string, List<string>> Scan()
    {
        var index = new Dictionary<string, List<string>>();
        foreach (var kind in new[] { "personas", "autos", "muebles", "objetos", "edificios" })
        {
            foreach (var file in List($"{Root}/{kind}"))
            {
                var name = file.EndsWith(".glb") ? file[..^4] : file.EndsWith(".gltf") ? file[..^5] : null;
                if (name is null)
                    continue;
                // «vecino-2» es una variante de «vecino».
                var dash = name.LastIndexOf('-');
                var code = dash > 0 && int.TryParse(name[(dash + 1)..], out _) ? name[..dash] : name;
                var key = $"{kind}/{code}";
                if (!index.TryGetValue(key, out var list))
                    index[key] = list = [];
                list.Add($"{Root}/{kind}/{file}");
            }
        }
        foreach (var list in index.Values)
            list.Sort(StringComparer.Ordinal);
        _adjust = ReadAdjustments();
        if (index.Count > 0)
            GD.Print($"[DISTRITO] MODELOS {index.Sum(kv => kv.Value.Count)} archivo(s): {string.Join(", ", index.Keys.OrderBy(k => k))}");
        return index;
    }

    private static IEnumerable<string> List(string folder)
    {
        // En el juego exportado los archivos originales no se listan: ResourceLoader sabe cuáles se pueden cargar.
        try
        {
            return ResourceLoader.ListDirectory(folder).Where(f => !f.EndsWith('/')).ToArray();
        }
        catch
        {
            using var dir = DirAccess.Open(folder);
            return dir?.GetFiles().Select(f => f.Replace(".import", "").Replace(".remap", "")).Distinct().ToArray() ?? [];
        }
    }

    private static Dictionary<string, Adjust> ReadAdjustments()
    {
        var path = $"{Root}/ajustes.json";
        if (!Godot.FileAccess.FileExists(path))
            return new();
        try
        {
            var json = Godot.FileAccess.GetFileAsString(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            var result = new Dictionary<string, Adjust>();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var entry in doc.RootElement.EnumerateObject())
                if (!entry.Name.StartsWith('_') && entry.Value.ValueKind == JsonValueKind.Object)
                    result[entry.Name] = entry.Value.Deserialize<Adjust>(options) ?? new Adjust();
            return result;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[DISTRITO] modelos/ajustes.json no se pudo leer: {e.Message}");
            return new();
        }
    }

    public static bool Has(string kind, string code) => Index.ContainsKey($"{kind}/{code}");

    /// <summary>
    /// Arma el modelo (o null si no hay) y lo ajusta: escala para que mida <paramref name="size"/> (en el eje que pida
    /// <paramref name="fit"/>), apoyado en el piso, centrado y mirando a −Z. <paramref name="seed"/> elige la variante.
    /// </summary>
    public static Node3D? Make(string kind, string code, Vector3 size, Fit fit, int seed = 0)
    {
        if (!Index.TryGetValue($"{kind}/{code}", out var files) || files.Count == 0)
            return null;
        var path = files[(int)((uint)seed % (uint)files.Count)];
        if (!Loaded.TryGetValue(path, out var scene))
        {
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            Loaded[path] = scene;
            if (scene is null)
                GD.PushWarning($"[DISTRITO] no se pudo cargar {path}");
        }
        if (scene?.Instantiate() is not Node3D model)
            return null;
        var adjust = _adjust.GetValueOrDefault($"{kind}/{code}") ?? _adjust.GetValueOrDefault(kind) ?? new Adjust();
        var holder = new Node3D { Name = "Modelo" };
        var turn = new Node3D { Name = "Giro", RotationDegrees = new Vector3(0, adjust.Girar ?? 180f, 0) };
        holder.AddChild(turn);
        turn.AddChild(model);

        // Medidas del modelo tal como viene (todas sus mallas, ya giradas).
        var box = Bounds(model, turn.Transform);
        if (box.Size.LengthSquared() < 1e-6f)
            return holder;
        var target = adjust.Altura is { } h ? new Vector3(size.X, h, size.Z) : size;
        var scale = fit switch
        {
            Fit.Height => target.Y / box.Size.Y,
            Fit.Length => target.Z / Mathf.Max(box.Size.Z, 0.01f),
            _ => Mathf.Min(target.X / Mathf.Max(box.Size.X, 0.01f), Mathf.Min(target.Y / Mathf.Max(box.Size.Y, 0.01f), target.Z / Mathf.Max(box.Size.Z, 0.01f))),
        } * (adjust.Escala ?? 1f);
        turn.Scale = Vector3.One * scale;
        var center = box.GetCenter() * scale;
        turn.Position = new Vector3(-center.X, -box.Position.Y * scale + (adjust.Subir ?? 0f), -center.Z);
        return holder;
    }

    public enum Fit { Height, Length, Inside }

    /// <summary>Caja que contiene todas las mallas del modelo, en el espacio de <paramref name="parent"/>.</summary>
    private static Aabb Bounds(Node3D root, Transform3D parent)
    {
        var any = false;
        var box = new Aabb();
        void Walk(Node node, Transform3D t)
        {
            var here = node is Node3D n ? t * n.Transform : t;
            if (node is VisualInstance3D v && v.GetAabb() is var a && a.Size.LengthSquared() > 0)
            {
                var world = here * a;
                box = any ? box.Merge(world) : world;
                any = true;
            }
            foreach (var child in node.GetChildren())
                Walk(child, here);
        }
        Walk(root, parent);
        return box;
    }

    /// <summary>Busca el AnimationPlayer del modelo (los .glb con esqueleto y animaciones lo traen).</summary>
    public static AnimationPlayer? AnimationsOf(Node node) =>
        node as AnimationPlayer ?? node.GetChildren().Select(AnimationsOf).FirstOrDefault(p => p is not null);

    /// <summary>La primera animación cuyo nombre tenga alguna de las palabras (sin mayúsculas).</summary>
    public static string? FindAnimation(AnimationPlayer player, params string[] words) =>
        player.GetAnimationList().FirstOrDefault(a => words.Any(w => a.ToString().Contains(w, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Busca un hueso del esqueleto por nombre (para colgar el arma en la mano derecha).</summary>
    public static (Skeleton3D Skeleton, int Bone)? FindBone(Node node, params string[] names)
    {
        if (node is Skeleton3D skeleton)
            for (var i = 0; i < skeleton.GetBoneCount(); i++)
            {
                var bone = skeleton.GetBoneName(i);
                if (names.Any(n => bone.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    return (skeleton, i);
            }
        foreach (var child in node.GetChildren())
            if (FindBone(child, names) is { } found)
                return found;
        return null;
    }
}
