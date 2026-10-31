using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Ubica un control en la pantalla con un contenedor que ocupa toda la ventana y se recalcula solo
/// cuando cambia el tamaño (ventana, pantalla completa, escalado). Antes los paneles se ubicaban con
/// Position, que en Godot es relativa a la esquina superior izquierda: quedaban fuera de la pantalla.
/// </summary>
public static class UiAnchor
{
    public static MarginContainer Wrap(Control child, Control.SizeFlags horizontal, Control.SizeFlags vertical,
        int left = 12, int top = 10, int right = 12, int bottom = 12, string? name = null)
    {
        var holder = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (name is not null)
            holder.Name = name;
        holder.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        holder.AddThemeConstantOverride("margin_left", left);
        holder.AddThemeConstantOverride("margin_top", top);
        holder.AddThemeConstantOverride("margin_right", right);
        holder.AddThemeConstantOverride("margin_bottom", bottom);
        child.SizeFlagsHorizontal = horizontal;
        child.SizeFlagsVertical = vertical;
        holder.AddChild(child);
        return holder;
    }

    public const Control.SizeFlags Start = Control.SizeFlags.ShrinkBegin;
    public const Control.SizeFlags Center = Control.SizeFlags.ShrinkCenter;
    public const Control.SizeFlags End = Control.SizeFlags.ShrinkEnd;
}
