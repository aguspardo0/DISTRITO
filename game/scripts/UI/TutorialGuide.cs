using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Tutorial de inicio (Etapa F de la hoja de ruta; primera hora con el café, 2026-11-08): guía al jugador nuevo por lo básico, marcando a dónde
/// ir (luz en el mundo y aro en el mapa, como los encargos). No da premios: solo enseña. El avance se guarda por personaje
/// en user://tutorial.cfg; los personajes con experiencia lo saltean solos y se puede saltar o repetir desde Esc.
/// </summary>
public partial class TutorialGuide : PanelContainer
{
    private const string FilePath = "user://tutorial.cfg";

    private sealed record Step(string Title, string Text, Func<bool> Done, string[] Targets);

    private Func<EconomySnapshot?> _snapshot = () => null;
    private Func<Guid?> _character = () => null;
    private Func<bool> _mapOpen = () => false;
    private Func<bool> _notebookOpen = () => false;
    private List<Step> _steps = [];
    private Label _title = null!;
    private Label _text = null!;
    private Label _cheer = null!;
    private int _index = -1;
    private double _tick, _cheerUntil, _clock, _lastStepAt;
    private bool _mapSeen, _notebookSeen, _previewing;
    private Guid? _loadedFor;

    /// <summary>A dónde ir en el paso actual (para los marcadores del mundo y del mapa).</summary>
    public IReadOnlyList<string> Targets => _index >= 0 && _index < _steps.Count ? _steps[_index].Targets : [];

    public event Action? TargetsChanged;

    public bool Active => _index >= 0 && _index < _steps.Count;

    /// <summary>¿Terminó un turno de empleado en la Esquina del café? (lo pone Main).</summary>
    public Func<bool> ShiftDone { get; set; } = () => false;

    private bool HasStand() => _snapshot()?.BusinessOf(EconomyCatalog.CoffeeStand.Code) is not null;

    public void Initialize(Func<EconomySnapshot?> snapshot, Func<Guid?> character, Func<bool> mapOpen, Func<bool> notebookOpen)
    {
        _snapshot = snapshot;
        _character = character;
        _mapOpen = mapOpen;
        _notebookOpen = notebookOpen;
        var streetPeople = FamilyPeople.Contacts.Where(c => c.Role == "calle").Select(c => c.Code).ToArray();
        _steps =
        [
            new("Bienvenido a Bellmont",
                "Llegaste en tren con veinte pesos en el bolsillo. Estás en la Calle Principal, el corazón neutral de la ciudad: acá están los "
                + "proveedores y los comercios, cada familia con su local en la boca de su avenida, y alrededor, El Centro. Abrí el mapa con M para ubicarte.",
                () => _mapSeen, []),
            new("Tu primer trabajo",
                "Andá a la Esquina del café (la columna de luz te marca el camino) y hablá con Don Aldo con E. Trabajá un turno de diez minutos: "
                + "aprendés a servir el café de la urna como le gusta a cada cliente y cobrás $3 más propinas.",
                () => ShiftDone() || HasStand(), [EconomyCatalog.CoffeeCornerNpc]),
            new("Tu propio puesto",
                "Ahora que sabés cómo es, podés tener tu propio puesto de café. La licencia la da la Oficina Valenti: hablá con ellos y sacala.",
                HasStand, [EconomyCatalog.ValentiOfficeNpc]),
            new("Mercadería para el puesto",
                "Un puesto sin café no vende. Andá al Almacén Moretti y comprá bolsas de café; después cargalas en tu puesto desde la Esquina del café.",
                () => _snapshot()?.QuantityOf(EconomyCatalog.CoffeeBagItem) > 0 || HasWork(), [EconomyCatalog.DepositNpc]),
            new("Las familias",
                "Bellmont tiene tres familias rivales, cada una en su barrio: los Moretti al oeste, los Valenti al norte y los Caruso al sur. "
                + "Salí de la Calle Principal por la avenida de una familia y hablá con su gente: Tano Ricci, Lupo o Beppe. Tomá un encargo.",
                HasWork, streetPeople),
            new("Tu primer encargo",
                "Cumplí el encargo: lo que tenés que hacer aparece arriba a la derecha, con la distancia, y el destino brilla en el mundo y en el mapa.",
                () => _snapshot()?.Reputation?.Any(r => r.Value > 0) == true, []),
            new("La libreta",
                "Abrí la libreta con L: ahí ves tus encargos, el encargo del día y todo el trabajo que ofrece cada familia.",
                () => _notebookSeen, []),
            new("Lo que viene",
                "Con 50 de reputación, el capo de una familia te recibe y te cuenta su historia. Formá una cuadrilla con C para negocios, "
                + "misiones y la operación del día. Hasta el nivel 5 nadie te puede atacar: aprovechá para crecer. ¡Suerte!",
                () => _clock - _lastStepAt > 25, []),
        ];
    }

    private bool HasWork() => _snapshot() is { } s && (s.ActiveEncargos.Any() || s.Reputation?.Any(r => r.Value > 0) == true);

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(380, 0);
        AddThemeStyleboxOverride("panel", Distrito.Game.Economy.EncargoLook.Panel(0.82f, Distrito.Game.Economy.EncargoLook.Gold));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        _title = Distrito.Game.Economy.EncargoLook.Text("", Distrito.Game.Economy.EncargoLook.Gold, 360, 15);
        box.AddChild(_title);
        _text = Distrito.Game.Economy.EncargoLook.Text("", null, 360, 13);
        box.AddChild(_text);
        _cheer = Distrito.Game.Economy.EncargoLook.Text("", Distrito.Game.Economy.EncargoLook.Good, 360, 13);
        box.AddChild(_cheer);
        box.AddChild(Distrito.Game.Economy.EncargoLook.Text("Esc → Saltar el tutorial", Distrito.Game.Economy.EncargoLook.Muted, 360, 11));
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _mapSeen |= _mapOpen();
        _notebookSeen |= _notebookOpen();
        _tick += delta;
        if (_tick < 0.4)
            return;
        _tick = 0;
        var character = _character();
        if (!_previewing && character != _loadedFor && _snapshot() is { } snapshot)
            Load(character, snapshot);
        if (!Active)
        {
            Visible = false;
            return;
        }
        if (_steps[_index].Done())
            Advance();
        Render();
    }

    private void Load(Guid? character, EconomySnapshot snapshot)
    {
        _loadedFor = character;
        var cfg = new ConfigFile();
        cfg.Load(FilePath);
        var key = character?.ToString() ?? "invitado";
        if (cfg.HasSectionKey(key, "paso2"))
            _index = (int)cfg.GetValue(key, "paso2");
        else if (cfg.HasSectionKey(key, "paso"))
        {
            // Tutorial de antes (sin los pasos del café): se corre al mismo paso de ahora.
            var old = (int)cfg.GetValue(key, "paso");
            _index = old == 0 ? 0 : old + 2; // antes: 0 bienvenida, 1 compra…; ahora hay dos pasos más (trabajo y puesto) después de la bienvenida
        }
        else
            // Quien ya tiene experiencia no necesita el tutorial.
            _index = snapshot.ProgressOrEmpty.Level >= 3 || snapshot.Reputation?.Any(r => r.Value >= 10) == true ? _steps.Count : 0;
        _lastStepAt = _clock;
        Save();
        TargetsChanged?.Invoke();
    }

    private void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(FilePath);
        cfg.SetValue(_loadedFor?.ToString() ?? "invitado", "paso2", _index);
        cfg.Save(FilePath);
    }

    private void Advance()
    {
        _cheer.Text = $"✔ {_steps[_index].Title}";
        _cheerUntil = _clock + 4;
        _index++;
        _lastStepAt = _clock;
        Save();
        TargetsChanged?.Invoke();
    }

    private void Render()
    {
        if (!Active)
            return;
        Visible = true;
        var step = _steps[_index];
        _title.Text = $"PRIMEROS PASOS ({_index + 1}/{_steps.Count}) · {step.Title}";
        _text.Text = step.Text;
        if (_clock > _cheerUntil)
            _cheer.Text = "";
    }

    /// <summary>Saltar el tutorial (desde Esc).</summary>
    public void Skip()
    {
        _index = _steps.Count;
        Save();
        Visible = false;
        TargetsChanged?.Invoke();
    }

    /// <summary>Volver a empezar el tutorial (desde Esc).</summary>
    public void Restart()
    {
        _index = 0;
        _mapSeen = _notebookSeen = false;
        _lastStepAt = _clock;
        Save();
        TargetsChanged?.Invoke();
    }

    /// <summary>Para las capturas de prueba: mostrar un paso sin guardar nada.</summary>
    public void Preview(int step)
    {
        _loadedFor = Guid.Empty;
        _previewing = true;
        _index = Math.Clamp(step, 0, _steps.Count - 1);
        Render();
        TargetsChanged?.Invoke();
    }
}
