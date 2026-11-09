using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// El estilo de época de todas las ventanas: papel oscuro, bordes de bronce y la letra de los diarios de los años 50
/// (Libre Franklin; títulos en Oswald). Se aplica a toda la ventana del juego, así que cualquier panel nuevo lo toma
/// solo; los que tienen colores propios (avisos, encargos) los conservan.
/// </summary>
public static class PeriodTheme
{
    public static readonly Color Ink = new(0.93f, 0.89f, 0.8f);
    public static readonly Color Muted = new(0.68f, 0.64f, 0.56f);
    public static readonly Color Brass = new(0.78f, 0.62f, 0.33f);
    public static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    public static readonly Color Paper = new(0.09f, 0.08f, 0.07f, 0.96f);
    public static readonly Color Wood = new(0.17f, 0.135f, 0.1f);

    private static Theme? _theme;
    private static Font? _body, _title;

    /// <summary>Letra del texto común.</summary>
    public static Font Body => _body ??= Variable("res://fonts/LibreFranklin.ttf", 460) ?? ThemeDB.FallbackFont;

    /// <summary>Letra de los títulos (angosta, de cartel).</summary>
    public static Font Title => _title ??= Variable("res://fonts/Oswald.ttf", 500) ?? Body;

    /// <summary>Pone el estilo en toda la ventana del juego (y en lo que se dibuja a mano con la letra de base).</summary>
    public static void Apply(Window root)
    {
        // El tema de la ventana raíz no pasa a través de las CanvasLayer (donde viven casi todas las ventanas), así que
        // se mezcla con el tema por defecto del motor: así lo toma todo control, esté donde esté.
        ThemeDB.FallbackFont = Body;
        var basis = ThemeDB.GetDefaultTheme();
        basis.MergeWith(Build());
        basis.DefaultFont = Body;
        basis.DefaultFontSize = 15;
        root.Theme = Build();
    }

    public static Theme Build()
    {
        if (_theme is not null)
            return _theme;
        var t = new Theme { DefaultFont = Body, DefaultFontSize = 15 };

        var panel = Box(Paper, Brass with { A = 0.85f }, 2, 4, 12, 9);
        foreach (var type in new[] { "PanelContainer", "Panel", "PopupPanel", "PopupMenu" })
            t.SetStylebox("panel", type, panel);
        t.SetStylebox("panel", "TooltipPanel", Box(Paper, Brass, 1, 3, 8, 5));
        t.SetColor("font_color", "TooltipLabel", Ink);

        foreach (var type in new[] { "Label", "RichTextLabel" })
        {
            t.SetColor(type == "Label" ? "font_color" : "default_color", type, Ink);
            t.SetColor("font_shadow_color", type, new Color(0, 0, 0, 0.35f));
        }

        // Botones: madera oscura con filete de bronce; al pasar el mouse se enciende el borde dorado.
        t.SetStylebox("normal", "Button", Box(Wood, Brass with { A = 0.55f }, 1, 3, 12, 5));
        t.SetStylebox("hover", "Button", Box(Wood.Lightened(0.12f), Gold, 1, 3, 12, 5));
        t.SetStylebox("pressed", "Button", Box(Wood.Darkened(0.35f), Gold, 1, 3, 12, 5));
        t.SetStylebox("disabled", "Button", Box(Wood with { A = 0.5f }, Muted with { A = 0.3f }, 1, 3, 12, 5));
        t.SetStylebox("focus", "Button", Box(Colors.Transparent, Gold with { A = 0.7f }, 1, 3, 12, 5, drawCenter: false));
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Gold);
        t.SetColor("font_pressed_color", "Button", Gold);
        t.SetColor("font_focus_color", "Button", Ink);
        t.SetColor("font_hover_pressed_color", "Button", Gold);
        t.SetColor("font_disabled_color", "Button", Muted with { A = 0.6f });

        // Casillas y llaves: sin caja de botón, solo el texto.
        var empty = new StyleBoxEmpty();
        foreach (var type in new[] { "CheckBox", "CheckButton" })
            foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
                t.SetStylebox(state, type, empty);

        // Campos de texto: tinta sobre papel más oscuro.
        foreach (var type in new[] { "LineEdit", "TextEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Box(new Color(0.05f, 0.045f, 0.04f), Brass with { A = 0.45f }, 1, 3, 8, 5));
            t.SetStylebox("focus", type, Box(Colors.Transparent, Gold, 1, 3, 8, 5, drawCenter: false));
            t.SetStylebox("read_only", type, Box(new Color(0.07f, 0.065f, 0.06f), Muted with { A = 0.3f }, 1, 3, 8, 5));
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_placeholder_color", type, Muted with { A = 0.7f });
            t.SetColor("caret_color", type, Gold);
            t.SetColor("selection_color", type, Brass with { A = 0.4f });
        }

        // Listas y pestañas.
        t.SetStylebox("panel", "ItemList", Box(new Color(0.06f, 0.055f, 0.05f), Brass with { A = 0.35f }, 1, 3, 6, 6));
        t.SetStylebox("selected", "ItemList", Box(Brass with { A = 0.35f }, Colors.Transparent, 0, 2, 4, 2));
        t.SetStylebox("selected_focus", "ItemList", Box(Brass with { A = 0.45f }, Colors.Transparent, 0, 2, 4, 2));
        t.SetColor("font_color", "ItemList", Ink);
        t.SetColor("font_selected_color", "ItemList", Gold);
        t.SetStylebox("panel", "TabContainer", Box(Paper, Brass with { A = 0.6f }, 1, 3, 10, 8));
        t.SetStylebox("tab_selected", "TabContainer", Box(Wood.Lightened(0.08f), Gold, 1, 3, 12, 5));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Wood.Darkened(0.2f), Brass with { A = 0.35f }, 1, 3, 12, 5));
        t.SetStylebox("tab_hovered", "TabContainer", Box(Wood, Brass, 1, 3, 12, 5));
        t.SetColor("font_selected_color", "TabContainer", Gold);
        t.SetColor("font_unselected_color", "TabContainer", Muted);
        t.SetColor("font_hovered_color", "TabContainer", Ink);

        // Barras y deslizadores.
        t.SetStylebox("background", "ProgressBar", Box(new Color(0.05f, 0.045f, 0.04f), Brass with { A = 0.35f }, 1, 2, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Brass, Colors.Transparent, 0, 2, 0, 0));
        t.SetColor("font_color", "ProgressBar", Ink);
        var track = Box(new Color(0.05f, 0.045f, 0.04f), Brass with { A = 0.4f }, 1, 2, 0, 2);
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", Box(Brass, Colors.Transparent, 0, 2, 0, 2));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Gold, Colors.Transparent, 0, 2, 0, 2));
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", type, Box(new Color(0, 0, 0, 0.25f), Colors.Transparent, 0, 3, 3, 3));
            t.SetStylebox("grabber", type, Box(Brass with { A = 0.55f }, Colors.Transparent, 0, 3, 3, 3));
            t.SetStylebox("grabber_highlight", type, Box(Brass, Colors.Transparent, 0, 3, 3, 3));
            t.SetStylebox("grabber_pressed", type, Box(Gold, Colors.Transparent, 0, 3, 3, 3));
        }
        t.SetStylebox("separator", "HSeparator", Line());
        t.SetConstant("separation", "HSeparator", 10);

        // Títulos: Label con «Titulo» como variación de tipo.
        t.SetTypeVariation("Titulo", "Label");
        t.SetFont("font", "Titulo", Title);
        t.SetFontSize("font_size", "Titulo", 22);
        t.SetColor("font_color", "Titulo", Gold);

        return _theme = t;
    }

    private static StyleBoxFlat Box(Color bg, Color border, int borderWidth, int radius, int padX, int padY, bool drawCenter = true) => new()
    {
        BgColor = bg,
        DrawCenter = drawCenter,
        BorderColor = border,
        BorderWidthLeft = borderWidth, BorderWidthRight = borderWidth, BorderWidthTop = borderWidth, BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
    };

    private static StyleBoxLine Line() => new() { Color = Brass with { A = 0.45f }, Thickness = 1 };

    private static Font? Variable(string path, int weight)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load<FontFile>(path) is not { } file)
            return null;
        var font = new FontVariation { BaseFont = file };
        font.VariationOpentype = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), weight } };
        return font;
    }
}
