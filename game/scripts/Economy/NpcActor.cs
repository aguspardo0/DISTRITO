using Distrito.Server.Core.Npcs;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>Representación visual y física de un NPC fijo. Existe igual en servidor y clientes (no se replica).</summary>
public partial class NpcActor : StaticBody3D
{
    public NpcDefinition Definition { get; private set; } = null!;

    public static NpcActor Create(NpcDefinition definition, Color color, Vector3? at = null, string suffix = "")
    {
        var actor = new NpcActor { Name = definition.Code.Replace(':', '_') + suffix, Definition = definition };
        actor.Position = at ?? new Vector3(definition.Position.X, definition.Position.Y, definition.Position.Z);

        var shape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        };
        // Una persona de época (antes era una cápsula): saco del color del NPC, camisa clara y sombrero. Si hay un
        // .glb propio del NPC (game/modelos/personas/<código sin «npc:»>.glb) se usa ese; si no, el de «comerciante».
        var own = definition.Code.Replace("npc:", "");
        var code = Distrito.Game.World.ModelLibrary.Has("personas", own) ? own : "comerciante";
        var seed = definition.Code.Sum(c => c);
        string[] skins = ["#e0b899", "#c99a78", "#a8775a", "#7d5440"];
        var look = new Distrito.Shared.Api.CharacterLook(code, definition.Name, skins[seed % skins.Length], seed % 3 == 0 ? "#3b2a1d" : "#1c1612",
            "#e6dfcf", "#2b2622", "#" + color.ToHtml(false), "#6b1f1f", "#1a1410", seed % 4 == 0 ? null : "#2a241f",
            Vest: seed % 3 == 1, Jacket: seed % 3 != 1, Mustache: seed % 2 == 0);
        var body = Distrito.Game.Player.CharacterModel.Build(look);
        // Mira hacia donde llega la gente: adentro de un local, hacia la puerta; en la calle, hacia la calzada.
        var p = actor.Position;
        var face = Distrito.Server.Core.World.InteriorLayout.At(p.X, p.Z) is { } room
            ? new Vector2(room.Door.X - p.X, room.Door.Y - p.Z)
            : new Vector2(0, -p.Z);
        if (face.LengthSquared() > 0.01f)
            body.Rotation = new Vector3(0, Mathf.Atan2(-face.X, -face.Y), 0);
        var label = new Label3D
        {
            Text = $"{definition.Name}\n[E] hablar",
            Position = new Vector3(0, 2.4f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 44,
            OutlineSize = 8,
            Modulate = new Color(1f, 0.9f, 0.6f),
        };

        actor.AddChild(shape);
        actor.AddChild(body);
        actor.AddChild(label);
        return actor;
    }
}
