using Distrito.Game.UI;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Calidad gráfica (Opciones): baja para notebooks y placas integradas, media por defecto, alta para PCs con placa de
/// video. Cambia sombras, suavizado, resolución interna, distancia de dibujado de la ciudad lejana y de los interiores,
/// hasta dónde se ven las luces y cuánta lluvia cae. Se aplica a todo lo que ya está y a lo que aparece después.
/// </summary>
public static class GraphicsQuality
{
    /// <summary>Lo que cambia con cada calidad (baja, media, alta).</summary>
    public sealed record Level(string Name, bool Shadows, float ShadowDistance, Viewport.Msaa Msaa, bool Fxaa, float Scale,
        float FarCity, float Interior, float LightFade, int Rain, float CameraFar);

    public static readonly Level[] Levels =
    [
        new("Baja", false, 0, Viewport.Msaa.Disabled, true, 0.75f, 200, 30, 25, 700, 320),
        new("Media", true, 60, Viewport.Msaa.Disabled, true, 1f, 360, 50, 45, 1500, 520),
        new("Alta", true, 130, Viewport.Msaa.Msaa4X, false, 1f, 0, 90, 80, 2500, 900),
    ];

    public static Level Current => Levels[GameSettings.Quality];

    /// <summary>Grupos: «lejos» (manzanas del Centro y barrios: se dejan de dibujar a lo lejos) e «interior» (muebles de los locales).</summary>
    public const string FarGroup = "lejos", InteriorGroup = "interior";

    private static SceneTree? _tree;

    public static void Attach(SceneTree tree)
    {
        if (_tree is not null || DisplayServer.GetName() == "headless")
            return;
        _tree = tree;
        tree.NodeAdded += OnNodeAdded;
        GameSettings.QualityChanged += () => Apply(tree.Root);
        Apply(tree.Root);
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is OmniLight3D light)
            Fade(light);
        else if (node is Camera3D camera)
            camera.Far = Current.CameraFar;
    }

    private static void Fade(OmniLight3D light)
    {
        light.DistanceFadeEnabled = true;
        light.DistanceFadeBegin = Current.LightFade;
        light.DistanceFadeLength = 10;
        light.ShadowEnabled = false;
    }

    public static void Apply(Node root)
    {
        var q = Current;
        var viewport = root.GetViewport();
        viewport.Msaa3D = q.Msaa;
        viewport.ScreenSpaceAA = q.Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.Scaling3DScale = q.Scale;
        foreach (var node in root.FindChildren("*", "", true, false))
        {
            switch (node)
            {
                case DirectionalLight3D sun:
                    sun.ShadowEnabled = q.Shadows;
                    sun.DirectionalShadowMaxDistance = q.ShadowDistance > 0 ? q.ShadowDistance : 100;
                    sun.DirectionalShadowMode = q.ShadowDistance > 100 ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
                    break;
                case OmniLight3D light:
                    Fade(light);
                    break;
                case Camera3D camera:
                    camera.Far = q.CameraFar;
                    break;
            }
        }
        foreach (var node in root.GetTree().GetNodesInGroup(FarGroup))
            Range(node, q.FarCity);
        foreach (var node in root.GetTree().GetNodesInGroup(InteriorGroup))
            Range(node, q.Interior);
    }

    /// <summary>Hasta qué distancia se dibuja todo lo que cuelga de <paramref name="node"/> (0 = siempre).</summary>
    private static void Range(Node node, float end)
    {
        if (node is GeometryInstance3D g)
        {
            g.VisibilityRangeEnd = end;
            g.VisibilityRangeEndMargin = end > 0 ? 10 : 0; // corte seco (sin transparencias, que cuestan) con margen para que no titile
        }
        foreach (var child in node.GetChildren())
            Range(child, end);
    }
}
