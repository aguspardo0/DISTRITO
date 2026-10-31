using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Bot de prueba que juega un contrato de entrega completo como lo haría una persona:
/// camina al origen, acepta el contrato, compra la mercadería, camina al destino y la entrega.
/// Usa los mismos RPC que el jugador; el servidor valida todo igual.
/// </summary>
public partial class ContractBot : Node
{
    private enum Step { WaitingForSpawn, GoingToDeposit, TalkingToDeposit, GoingToWorkshop, TalkingToWorkshop, Done }

    private const float ArriveDistance = 2.2f;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private Step _step = Step.WaitingForSpawn;
    private int _startCompleted;
    private ContractTemplate _template = EconomyCatalog.SparePartsDelivery;

    public ContractTemplate Template => _template;
    /// <summary>Lo que debería ganar: el pago menos lo que cuesta la mercadería.</summary>
    public long ExpectedGain => _template.RewardCents - _template.Quantity * PriceOf(_template.ItemCode);

    public long? StartBalance { get; private set; }
    public long? FinalBalance { get; private set; }
    public bool Completed => _step == Step.Done;
    public int ReputationCaruso { get; private set; }
    public string StepName => _step.ToString();

    private static long PriceOf(string item) =>
        item == EconomyCatalog.CoffeeBagItem ? EconomyCatalog.CoffeeBagPriceCents : EconomyCatalog.SparePartsPriceCents;

    public void Initialize(GameNetwork network, EconomyNetwork economy, string? contractCode = null)
    {
        if (contractCode is not null)
            _template = EconomyCatalog.Templates[contractCode];
        _network = network;
        _economy = economy;
        _economy.DialogReceived += OnDialog;
        _economy.EconomyChanged += OnEconomy;
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
                _startCompleted = s.CompletedContracts;
                Log($"inicio: saldo {EconomyCatalog.FormatMoney(s.BalanceCents)}");
                _step = Step.GoingToDeposit;
                break;
            case Step.GoingToDeposit:
                if (WalkTo(player, controller, NpcInteractionRules.Find(_template.SourceNpc)!))
                {
                    _step = Step.TalkingToDeposit;
                    _economy.Interact(_template.SourceNpc);
                }
                break;
            case Step.GoingToWorkshop:
                if (WalkTo(player, controller, NpcInteractionRules.Find(_template.DestinationNpc)!))
                {
                    _step = Step.TalkingToWorkshop;
                    _economy.Interact(_template.DestinationNpc);
                }
                break;
            default:
                controller.ScriptedDirection = Vector2.Zero;
                break;
        }
    }

    /// <summary>Camina en línea recta hacia el NPC. Devuelve true al llegar.</summary>
    private static bool WalkTo(PlayerCharacter player, LocalPlayerController controller, NpcDefinition npc)
    {
        var target = new Vector2(npc.Position.X, npc.Position.Z);
        var here = new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
        var toTarget = target - here;
        if (toTarget.Length() <= ArriveDistance)
        {
            controller.ScriptedDirection = Vector2.Zero;
            return true;
        }
        controller.ScriptedDirection = toTarget.Normalized();
        return false;
    }

    private void OnDialog(NpcDialog dialog)
    {
        Log($"{dialog.Title}: {dialog.Text.Replace('\n', ' ')} [{string.Join(" | ", dialog.Options.Select(o => o.Id))}]");
        var snapshot = _economy.LastSnapshot;

        if (_step == Step.TalkingToDeposit)
        {
            var carrying = snapshot?.QuantityOf(_template.ItemCode) ?? 0;
            var next = dialog.Options.FirstOrDefault(o => o.Id == $"accept:{_template.Code}")
                       ?? (carrying < _template.Quantity ? dialog.Options.FirstOrDefault(o => o.Id == $"buy:{_template.ItemCode}") : null);
            if (next is not null)
                _economy.Choose(dialog.NpcCode, next.Id);
            else if (carrying >= _template.Quantity)
                _step = Step.GoingToWorkshop;
        }
        else if (_step == Step.TalkingToWorkshop)
        {
            var contract = _economy.LastSnapshot?.ActiveContract(_template.Code);
            var deliver = contract is null ? null : dialog.Options.FirstOrDefault(o => o.Id == $"deliver:{contract.Id}");
            if (deliver is not null)
                _economy.Choose(dialog.NpcCode, deliver.Id);
        }
    }

    private void OnEconomy(EconomySnapshot snapshot)
    {
        if (_step is Step.WaitingForSpawn or Step.Done || snapshot.CompletedContracts <= _startCompleted)
            return;
        FinalBalance = snapshot.BalanceCents;
        _step = Step.Done;
        Log($"contrato {_template.Code} cumplido: saldo {EconomyCatalog.FormatMoney(snapshot.BalanceCents)}, reputación Caruso {snapshot.ReputationWith(EconomyCatalog.Caruso)} / Moretti {snapshot.ReputationWith(EconomyCatalog.Moretti)}");
        ReputationCaruso = snapshot.ReputationWith(EconomyCatalog.Caruso);
    }

    private static void Log(string message) => GD.Print($"[DISTRITO] [bot-contrato] {message}");
}
