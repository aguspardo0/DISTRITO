using Distrito.Server.Core.Npcs;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>Representación visual y física de un NPC fijo. Existe igual en servidor y clientes (no se replica).</summary>
public partial class NpcActor : StaticBody3D
{
    public NpcDefinition Definition { get; private set; } = null!;

    public static NpcActor Create(NpcDefinition definition, Color color)
    {
        var actor = new NpcActor { Name = definition.Code.Replace(':', '_'), Definition = definition };
        actor.Position = new Vector3(definition.Position.X, definition.Position.Y, definition.Position.Z);

        var shape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        };
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
        };
        var hat = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.18f },
            Position = new Vector3(0, 1.85f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.1f, 0.08f) },
        };
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
        actor.AddChild(hat);
        actor.AddChild(label);
        return actor;
    }
}
