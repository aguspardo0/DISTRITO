using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Bot de prueba del negocio: compra la licencia en la Oficina Valenti, una bolsa de café en el Almacén Moretti,
/// repone su puesto en la esquina, espera a que el puesto venda solo (ciclo económico del backend) y retira la ganancia.
/// </summary>
public partial class BusinessBot : Node
{
    private enum Step { WaitingForSpawn, ToValenti, AtValenti, ToDeposit, AtDeposit, ToCorner, AtCorner, WaitingForSales, Done }

    private const float ArriveDistance = 2.2f;
    private const double PollSeconds = 2.0;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private Step _step = Step.WaitingForSpawn;
    private double _untilPoll;

    public long? StartBalance { get; private set; }
    public long? FinalBalance { get; private set; }
    public bool Completed => _step == Step.Done;
    public string StepName => _step.ToString();

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
        _economy.DialogReceived += OnDialog;
    }

    public override void _Process(double delta)
    {
        var player = _network.LocalPlayer;
        var controller = player?.GetNodeOrNull<LocalPlayerController>("LocalController");
        if (player is null || controller is null)
            return;

        switch (_step)
        {
            case Step.WaitingForSpawn when _economy.LastSnapshot is { } s:
                StartBalance = s.BalanceCents;
                Log($"inicio: saldo {EconomyCatalog.FormatMoney(s.BalanceCents)}");
                _step = Step.ToValenti;
                break;
            case Step.ToValenti:
                Arrive(player, controller, NpcInteractionRules.ValentiOffice, Step.AtValenti);
                break;
            case Step.ToDeposit:
                Arrive(player, controller, NpcInteractionRules.Deposit, Step.AtDeposit);
                break;
            case Step.ToCorner:
                Arrive(player, controller, NpcInteractionRules.CoffeeCorner, Step.AtCorner);
                break;
            case Step.WaitingForSales:
                controller.ScriptedDirection = Vector2.Zero;
                _untilPoll -= delta;
                if (_untilPoll <= 0)
                {
                    _untilPoll = PollSeconds;
                    _economy.Interact(NpcInteractionRules.CoffeeCorner.Code); // pregunta cómo va el puesto
                }
                break;
            default:
                controller.ScriptedDirection = Vector2.Zero;
                break;
        }
    }

    private void Arrive(PlayerCharacter player, LocalPlayerController controller, NpcDefinition npc, Step next)
    {
        var toTarget = new Vector2(npc.Position.X, npc.Position.Z) - new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
        if (toTarget.Length() > ArriveDistance)
        {
            controller.ScriptedDirection = toTarget.Normalized();
            return;
        }
        controller.ScriptedDirection = Vector2.Zero;
        _step = next;
        _economy.Interact(npc.Code);
    }

    private void OnDialog(NpcDialog dialog)
    {
        Log($"{dialog.Title}: {dialog.Text.Replace('\n', ' ')} [{string.Join(" | ", dialog.Options.Select(o => o.Id))}]");
        var snapshot = _economy.LastSnapshot;
        switch (_step)
        {
            case Step.AtValenti:
                if (Pick(dialog, "license:")) return;
                if (snapshot?.Business is not null) _step = Step.ToDeposit;
                break;
            case Step.AtDeposit:
                if (snapshot is not null && snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem) > 0) { _step = Step.ToCorner; return; }
                Pick(dialog, $"buy:{EconomyCatalog.CoffeeBagItem}");
                break;
            case Step.AtCorner:
                if (Pick(dialog, "restock:")) return;
                if (snapshot?.Business is { Stock: > 0 }) { _step = Step.WaitingForSales; _untilPoll = PollSeconds; }
                break;
            case Step.WaitingForSales:
                if (Pick(dialog, "withdraw:")) return;
                if (snapshot is { Business: { CashCents: 0, UnitsSold: > 0 } })
                {
                    FinalBalance = snapshot.BalanceCents;
                    _step = Step.Done;
                    Log($"listo: vendió {snapshot.Business.UnitsSold}, saldo {EconomyCatalog.FormatMoney(snapshot.BalanceCents)}");
                }
                break;
        }
    }

    private bool Pick(NpcDialog dialog, string prefix)
    {
        var option = dialog.Options.FirstOrDefault(o => o.Id.StartsWith(prefix, StringComparison.Ordinal));
        if (option is null)
            return false;
        _economy.Choose(dialog.NpcCode, option.Id);
        return true;
    }

    private static void Log(string message) => GD.Print($"[DISTRITO] [bot-negocio] {message}");
}
