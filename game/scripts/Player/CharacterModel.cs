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

    // Modelo .glb (game/modelos/personas/<código>.glb): si existe, reemplaza a las piezas simples.
    private AnimationPlayer? _anims;
    private string? _idle, _walk, _run, _punchAnim, _downAnim, _current;
    private Node3D? _hand; // donde va el arma (el hueso de la mano derecha, si el esqueleto lo tiene)
    private bool Glb => _hand is not null;
    private float _phase;
    private float _punch;
    private bool _downed;

    private string _weapon = Distrito.Shared.Economy.Weapons.Fists;
    private MeshInstance3D? _gun;

    /// <summary>Muestra el arma en la mano derecha (y el brazo apuntando adelante).</summary>
    public void SetWeapon(string code)
    {
        if (code == _weapon)
            return;
        _weapon = code;
        _gun?.QueueFree();
        _gun = null;
        if (code == Distrito.Shared.Economy.Weapons.Fists)
            return;
        var glbGun = Glb;
        var length = code == "pistola" ? 0.2f : code == "escopeta" ? 0.75f : 0.6f;
        _gun = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.09f, length) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(code == "pistola" ? "#1a1a1a" : "#3a2616"), Roughness = 0.5f },
            Position = new Vector3(0, -0.64f, -length / 2 + 0.05f),
            RotationDegrees = new Vector3(-90, 0, 0),
        };
        if (glbGun)
        {
            _gun.Position = new Vector3(0, 0, -length / 2);
            _gun.RotationDegrees = Vector3.Zero;
            _hand!.AddChild(_gun);
        }
        else
            _armR.AddChild(_gun);
    }

    private float _aimPitch, _recoil, _reload;

    public void SetAimPitch(float pitch) => _aimPitch = Mathf.Clamp(pitch, -1.2f, 1.2f);
    /// <summary>Retroceso: el brazo salta hacia arriba y vuelve.</summary>
    public void Recoil() => _recoil = 0.12f;
    /// <summary>Recarga: el brazo baja un rato.</summary>
    public void Reload(float seconds) => _reload = seconds;

    /// <summary>Piña: el brazo derecho sale hacia adelante un instante.</summary>
    public void Punch() => _punch = 0.3f;

    /// <summary>Herido: el modelo queda tirado en el piso.</summary>
    public void SetDowned(bool downed)
    {
        if (downed == _downed)
            return;
        _downed = downed;
        if (Glb && _downAnim is not null)
        {
            Play(downed ? _downAnim : _idle);
            return;
        }
        Rotation = downed ? new Vector3(Mathf.DegToRad(-85f), 0, 0) : Vector3.Zero;
        Position = downed ? new Vector3(0, 0.2f, 0.9f) : Vector3.Zero;
    }

    public string LookCode { get; private set; } = "";

    public static CharacterModel Build(CharacterLook look)
    {
        var m = new CharacterModel { Name = "Model", LookCode = look.Code };
        // Variante estable para la misma persona (la ropa ya la distingue): sale de sus colores.
        var seed = $"{look.Skin}{look.Shirt}{look.Pants}{look.Top}".Sum(c => c * 7);
        if (Distrito.Game.World.ModelLibrary.Make("personas", look.Code, new Vector3(0.6f, 1.8f, 0.4f), Distrito.Game.World.ModelLibrary.Fit.Height, seed) is { } glb)
        {
            m.AddChild(glb);
            m.UseGlb(glb);
            return m;
        }
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
            Add(m, Box(0.07f, 0.04f, 0.01f), Mat("#cfc4b0"), new Vector3(0.15f, 1.44f, -0.16f)); // pañuelo
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

    /// <summary>Prepara un modelo .glb: sus animaciones (quieto, caminar, correr, piña, herido) y la mano para el arma.</summary>
    private void UseGlb(Node3D glb)
    {
        _anims = Distrito.Game.World.ModelLibrary.AnimationsOf(glb);
        if (_anims is not null)
        {
            _idle = Distrito.Game.World.ModelLibrary.FindAnimation(_anims, "idle", "quieto", "parado", "stand");
            _walk = Distrito.Game.World.ModelLibrary.FindAnimation(_anims, "walk", "camin");
            _run = Distrito.Game.World.ModelLibrary.FindAnimation(_anims, "run", "corr", "jog") ?? _walk;
            _punchAnim = Distrito.Game.World.ModelLibrary.FindAnimation(_anims, "punch", "pina", "piña", "hit", "attack", "golpe");
            _downAnim = Distrito.Game.World.ModelLibrary.FindAnimation(_anims, "death", "die", "dying", "fall", "herido", "muerte");
            foreach (var name in new[] { _idle, _walk, _run })
                if (name is not null && _anims.GetAnimation(name) is { } anim)
                    anim.LoopMode = Animation.LoopModeEnum.Linear;
            Play(_idle);
        }
        if (Distrito.Game.World.ModelLibrary.FindBone(glb, "righthand", "hand_r", "hand.r", "r_hand", "mano_d", "manoderecha") is { } bone)
        {
            var attach = new BoneAttachment3D { BoneName = bone.Skeleton.GetBoneName(bone.Bone) };
            bone.Skeleton.AddChild(attach);
            _hand = attach;
        }
        else
            _hand = new Node3D { Name = "Mano", Position = new Vector3(0.25f, 1.05f, -0.25f) }; // sin esqueleto: el arma va adelante
        if (_hand.GetParent() is null)
            AddChild(_hand);
    }

    private void Play(string? animation, float blend = 0.2f, float speed = 1f)
    {
        if (_anims is null || animation is null)
            return;
        _anims.SpeedScale = speed;
        if (_current == animation && _anims.IsPlaying())
            return;
        _current = animation;
        _anims.Play(animation, blend);
    }

    /// <summary>Caminata: balanceo de piernas y brazos según la velocidad horizontal (m/s).</summary>
    public void Animate(float speed, float delta)
    {
        if (Glb)
        {
            if (_downed)
                return;
            if (_punch > 0 && _punchAnim is not null)
            {
                _punch -= delta;
                Play(_punchAnim, 0.05f);
                return;
            }
            if (speed > 3.4f && _run is not null) Play(_run, 0.2f, Mathf.Clamp(speed / 5f, 0.7f, 1.5f));
            else if (speed > 0.25f && _walk is not null) Play(_walk, 0.2f, Mathf.Clamp(speed / 1.6f, 0.6f, 1.6f));
            else Play(_idle, 0.25f);
            return;
        }
        var amount = Mathf.Clamp(speed / 4.5f, 0f, 1f);
        _phase += delta * (4f + 6f * amount);
        var swing = Mathf.Sin(_phase) * 0.6f * amount;
        _legL.Rotation = new Vector3(swing, 0, 0);
        _legR.Rotation = new Vector3(-swing, 0, 0);
        _armL.Rotation = new Vector3(-swing * 0.7f, 0, 0);
        _armR.Rotation = new Vector3(swing * 0.7f, 0, 0);
        if (_weapon != Distrito.Shared.Economy.Weapons.Fists && !_downed)
        {
            // Apuntando: el brazo sigue el ángulo vertical de la mira; el izquierdo acompaña; retroceso y recarga.
            var angle = Mathf.DegToRad(90f) + _aimPitch;
            if (_recoil > 0) { _recoil -= delta; angle += Mathf.DegToRad(14f) * (_recoil / 0.12f); }
            if (_reload > 0) { _reload -= delta; angle = Mathf.DegToRad(35f); }
            _armR.Rotation = new Vector3(angle, 0, 0);
            if (_weapon != "pistola")
                _armL.Rotation = new Vector3(angle - 0.15f, 0, 0.35f);
        }
        if (_punch > 0)
        {
            _punch -= delta;
            _armR.Rotation = new Vector3(Mathf.DegToRad(95f) * Mathf.Sin(Mathf.Clamp(_punch / 0.3f, 0, 1) * Mathf.Pi), 0, 0);
        }
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
