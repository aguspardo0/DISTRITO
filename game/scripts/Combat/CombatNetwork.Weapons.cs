using Distrito.Game.Player;
using Distrito.Server.Core.Combat;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Combat;

/// <summary>
/// Armas de fuego (propuesta aprobada por el equipo, 2026-10-31; las armas se pierden al quedar herido).
/// El cliente manda a dónde apunta; el servidor calcula el tiro desde los ojos del que dispara, ve si alguien está en la
/// línea (y no hay una pared antes), aplica las mismas reglas de PvP y el daño según la distancia.
/// La munición vive en el inventario; el servidor la descuenta en tandas.
/// </summary>
public partial class CombatNetwork
{
    private const double AmmoFlushSeconds = 3;
    private const float EyeHeight = 1.5f;

    private readonly Dictionary<long, string> _armed = new();
    private readonly Dictionary<(long, string), int> _loaded = new();
    private readonly Dictionary<long, double> _reloadUntil = new();
    private readonly Dictionary<long, double> _lastShot = new();
    private readonly Dictionary<Guid, EconomySnapshot> _snapshots = new();
    private readonly Dictionary<(Guid, string), int> _ammo = new();     // balas que tiene (estimado del servidor)
    private readonly Dictionary<(Guid, string), int> _pending = new();  // gastadas sin descontar todavía
    private readonly HashSet<Guid> _flushing = new();
    private double _nextFlush;

    // Cliente
    private string _myWeapon = Weapons.Fists;
    private double _nextLocalShot;
    public string AmmoText { get; private set; } = "";
    public event Action? AmmoChanged;

    private void InitializeWeapons()
    {
        LocalPlayerController.WeaponKey += OnWeaponKey;
        _economy.SnapshotSent += OnSnapshotSent;
    }

    private void OnWeaponKey(int key)
    {
        var code = key switch { 2 => Weapons.Pistol.Code, 3 => Weapons.Shotgun.Code, 4 => Weapons.Thompson.Code, _ => Weapons.Fists };
        RequestWeapon(code);
    }

    public void RequestWeapon(string code)
    {
        if (_network.LocalPlayer is null)
            return;
        if (Multiplayer.IsServer())
            HandleWeapon(Multiplayer.GetUniqueId(), code);
        else
            RpcId(1, MethodName.ServerWeapon, code);
    }

    /// <summary>Cliente: disparar hacia un punto del mundo (lo calcula la cámara o un bot).</summary>
    public void RequestShot(Vector3 aimPoint)
    {
        if (_network.LocalPlayer is null)
            return;
        if (Multiplayer.IsServer())
            HandleShot(Multiplayer.GetUniqueId(), aimPoint);
        else
            RpcId(1, MethodName.ServerShoot, aimPoint);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerAim(float yaw, float pitch)
    {
        if (Multiplayer.IsServer())
            HandleAim(Multiplayer.GetRemoteSenderId(), yaw, pitch);
    }

    private void HandleAim(long peer, float yaw, float pitch)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || _network.GetPlayer(peer) is not { Downed: false } player)
            return;
        player.AimYaw = yaw;
        player.AimPitch = Mathf.Clamp(pitch, -1.2f, 1.2f);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerWeapon(string code)
    {
        if (Multiplayer.IsServer())
            HandleWeapon(Multiplayer.GetRemoteSenderId(), code);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerShoot(Vector3 aimPoint)
    {
        if (Multiplayer.IsServer())
            HandleShot(Multiplayer.GetRemoteSenderId(), aimPoint);
    }

    /// <summary>Cliente: clic con un arma en la mano dispara (las automáticas, mientras se mantiene apretado).</summary>
    private double _nextAimSend;

    private void ProcessLocalShooting(double delta)
    {
        if (_myWeapon == Weapons.Fists || Weapons.Find(_myWeapon) is not { } weapon || _network.LocalPlayer is not { Downed: false })
            return;
        // La cámara orienta la puntería: se le avisa al servidor 10 veces por segundo para girar el cuerpo y el brazo.
        if (_clock >= _nextAimSend && GetViewport().GetCamera3D() is { } aimCamera)
        {
            _nextAimSend = _clock + 0.1;
            var f = -aimCamera.GlobalBasis.Z;
            var yaw = Mathf.Atan2(-f.X, -f.Z);
            var pitch = Mathf.Asin(Mathf.Clamp(f.Y, -1, 1)) + 0.12f; // la cámara va un poco arriba: se compensa
            if (Multiplayer.IsServer()) HandleAim(Multiplayer.GetUniqueId(), yaw, pitch);
            else RpcId(1, MethodName.ServerAim, yaw, pitch);
        }
        if (Input.MouseMode != Input.MouseModeEnum.Captured || !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (!weapon.Automatic)
                _triggerReleased = true;
            return;
        }
        if (!weapon.Automatic && !_triggerReleased)
            return;
        if (_clock < _nextLocalShot)
            return;
        _triggerReleased = false;
        _nextLocalShot = _clock + weapon.CooldownSeconds;
        if (GetViewport().GetCamera3D() is not { } camera)
            return;
        var center = GetViewport().GetVisibleRect().Size / 2;
        var from = camera.ProjectRayOrigin(center);
        var to = from + camera.ProjectRayNormal(center) * 60f;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = [_network.LocalPlayer.GetRid()];
        if (Distrito.Game.Transport.VehicleNetwork.Find(_network.LocalPlayer.Vehicle) is { } mine)
            query.Exclude.Add(mine.GetRid()); // manejando: se apunta por la ventanilla, no al propio vehículo
        var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        RequestShot(hit.Count > 0 ? (Vector3)hit["position"] : to);
    }

    private bool _triggerReleased = true;

    // ================= Servidor =================

    /// <summary>Efecto de una habilidad del árbol para un jugador (según su última economía conocida).</summary>
    public double Skill(long peer, string code) =>
        _network.TryGetSession(peer, out var s) && _snapshots.TryGetValue(s.CharacterId, out var snap) ? snap.Skill(code) : 0;

    /// <summary>Vida máxima: 100 + Piel dura.</summary>
    public int MaxHealthOf(long peer) => CombatRules.MaxHealth + (int)Skill(peer, Distrito.Shared.Progression.SkillTree.ToughSkin);

    private double ReloadSecondsOf(long peer) => Weapons.ReloadSeconds * (1 - Skill(peer, Distrito.Shared.Progression.SkillTree.QuickReload) / 100);

    private void OnSnapshotSent(long peer, EconomySnapshot snapshot)
    {
        if (!_network.TryGetSession(peer, out var session))
            return;
        var wasMax = MaxHealthOf(peer);
        _snapshots[session.CharacterId] = snapshot;
        // Piel dura: si estaba con la vida llena, queda llena con el nuevo máximo.
        if (_network.GetPlayer(peer) is { Downed: false } me && me.Health >= wasMax)
            me.Health = MaxHealthOf(peer);
        _levels[session.CharacterId] = snapshot.ProgressOrEmpty.Level;
        foreach (var w in Weapons.All)
        {
            var key = (session.CharacterId, w.AmmoItem);
            if (_pending.GetValueOrDefault(key) == 0 && !_flushing.Contains(session.CharacterId))
                _ammo[key] = snapshot.QuantityOf(w.AmmoItem);
        }
        // Si perdió el arma que tenía en la mano (la vendió o se la sacaron), vuelve a las piñas.
        if (_armed.TryGetValue(peer, out var code) && code != Weapons.Fists && snapshot.QuantityOf(code) == 0)
            SetArmed(peer, Weapons.Fists);
    }

    private bool Owns(long peer, string code, out Guid character)
    {
        character = Guid.Empty;
        if (_network.Backend is null)
            return true; // partida local: armas libres para probar
        if (!_network.TryGetSession(peer, out var session))
            return false;
        character = session.CharacterId;
        return _snapshots.TryGetValue(session.CharacterId, out var s) && s.QuantityOf(code) > 0;
    }

    private void HandleWeapon(long peer, string code)
    {
        if (_network.GetPlayer(peer) is not { Downed: false })
            return;
        if (code != Weapons.Fists && (Weapons.Find(code) is null || !Owns(peer, code, out _)))
        {
            SendMessage(peer, $"No tenés {code}. Las armas se compran en el Taller Caruso.");
            return;
        }
        SetArmed(peer, code);
    }

    private void SetArmed(long peer, string code)
    {
        _armed[peer] = code;
        if (_network.GetPlayer(peer) is { } player)
            player.Weapon = code;
        SendAmmo(peer);
    }

    private int Available(long peer, WeaponType w) =>
        _network.Backend is null ? 999 : _network.TryGetSession(peer, out var s) ? _ammo.GetValueOrDefault((s.CharacterId, w.AmmoItem)) : 0;

    private void HandleShot(long peer, Vector3 aimPoint)
    {
        if (_network.GetPlayer(peer) is not { Downed: false } shooter)
            return; // manejando también se dispara: es la ventaja del que va en vehículo
        var code = _armed.GetValueOrDefault(peer, Weapons.Fists);
        if (Weapons.Find(code) is not { } weapon)
            return;
        if (_lastShot.TryGetValue(peer, out var last) && _clock - last < weapon.CooldownSeconds * 0.9)
            return;
        if (_reloadUntil.TryGetValue(peer, out var until) && _clock < until)
            return;
        if (!aimPoint.IsFinite())
            return;

        var key = (peer, code);
        var loaded = _loaded.GetValueOrDefault(key);
        if (loaded <= 0)
        {
            var available = Available(peer, weapon);
            if (available <= 0)
            {
                SendMessage(peer, $"Sin {weapon.AmmoName}. Comprá en el Taller Caruso.");
                return;
            }
            _reloadUntil[peer] = _clock + ReloadSecondsOf(peer);
            _loaded[key] = Math.Min(weapon.Magazine, available);
            SendMessage(peer, "Recargando…");
            foreach (var p in Multiplayer.GetPeers())
                RpcId(p, MethodName.ReloadFx, peer);
            ReloadFx(peer);
            GetTree().CreateTimer(ReloadSecondsOf(peer)).Timeout += () => SendAmmo(peer);
            return;
        }

        _lastShot[peer] = _clock;
        _loaded[key] = loaded - 1;
        if (_network.Backend is not null && _network.TryGetSession(peer, out var session))
        {
            var ammoKey = (session.CharacterId, weapon.AmmoItem);
            _ammo[ammoKey] = Math.Max(0, _ammo.GetValueOrDefault(ammoKey) - 1);
            _pending[ammoKey] = _pending.GetValueOrDefault(ammoKey) + 1;
        }

        // El cuerpo gira hacia la mira y el tiro sale del cañón del arma (no de la cámara).
        var eye = shooter.GlobalPosition + Vector3.Up * EyeHeight;
        var flat = new Vector3(aimPoint.X - eye.X, 0, aimPoint.Z - eye.Z);
        if (flat.LengthSquared() > 0.01f)
        {
            shooter.AimYaw = Mathf.Atan2(-flat.X, -flat.Z);
            shooter.Rotation = new Vector3(0, shooter.AimYaw, 0);
        }
        var muzzle = MuzzleOf(shooter, weapon);
        var direction = (aimPoint - muzzle).Normalized();
        if (direction.LengthSquared() < 0.5f)
            return;
        // Línea de vista: entre los ojos y el cañón no puede haber pared (sacar el arma a través de un muro).
        if (WallDistance(eye, (muzzle - eye).Normalized(), eye.DistanceTo(muzzle)) < eye.DistanceTo(muzzle))
        {
            SendMessage(peer, "Estás pegado a la pared: no hay línea de tiro.");
            return;
        }
        shooter.AimPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(direction.Y, -1, 1)), -1.2f, 1.2f);
        var (target, distance) = FirstPlayerOnRay(shooter, muzzle, direction, weapon.Range);
        var wallDistance = WallDistance(muzzle, direction, weapon.Range);
        // NPC de los asaltos (chofer, cobrador, guardias): si el tiro les llega antes que a una pared o a un jugador.
        if (NpcShot?.Invoke(muzzle, direction, weapon.Range) is { } npc && npc.Distance < wallDistance && (target is null || npc.Distance < distance))
        {
            ShotFxEverywhere(peer, muzzle, muzzle + direction * npc.Distance);
            SendAmmo(peer);
            SendHit(peer);
            NpcHit?.Invoke(npc.Id, shooter, weapon.DamageAt(npc.Distance));
            return;
        }
        var end = muzzle + direction * Math.Min(weapon.Range, Math.Min(wallDistance, target is null ? float.MaxValue : distance));
        ShotFxEverywhere(peer, muzzle, end);
        SendAmmo(peer);

        if (target is null || wallDistance < distance)
            return;
        if (CombatRules.WhyNot(N(shooter.GlobalPosition), N(target.GlobalPosition), LevelOf(peer), LevelOf(target.PeerId), _state,
                AtWar(peer, target.PeerId), AttackKind.Weapon) is { } reason)
        {
            if (!_lastRefusal.TryGetValue(peer, out var said) || _clock - said > 3)
            {
                _lastRefusal[peer] = _clock;
                SendMessage(peer, reason);
            }
            return;
        }
        ApplyDamage(shooter, target, (int)Math.Round(weapon.DamageAt(distance) * (1 + Skill(peer, Distrito.Shared.Progression.SkillTree.SteadyHand) / 100))); // Pulso firme
    }

    /// <summary>Posición del cañón: mano derecha, a la altura del pecho, hacia adelante según el largo del arma.</summary>
    private static Vector3 MuzzleOf(PlayerCharacter shooter, WeaponType weapon)
    {
        var yaw = shooter.AimYaw;
        var forward = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var reach = weapon.Code == "pistola" ? 0.75f : 0.95f;
        return shooter.GlobalPosition + Vector3.Up * 1.4f + right * 0.27f + forward * reach;
    }

    /// <summary>El jugador más cercano cuya cápsula (radio 0,45 m, de 0,2 a 1,7 m de alto) cruza el rayo.</summary>
    private (PlayerCharacter? Target, float Distance) FirstPlayerOnRay(PlayerCharacter shooter, Vector3 origin, Vector3 dir, float range)
    {
        PlayerCharacter? best = null;
        var bestT = float.MaxValue;
        foreach (var p in _network.AllPlayers)
        {
            if (p == shooter || p.Dead)
                continue; // los que manejan también pueden recibir tiros (van sentados en la cabina)
            // Un herido está tirado: su cápsula es baja (se lo puede rematar).
            var bottom = p.GlobalPosition + Vector3.Up * (p.Downed ? 0.05f : 0.2f);
            var top = p.GlobalPosition + Vector3.Up * (p.Downed ? 0.45f : 1.7f);
            var (t, dist) = RaySegmentDistance(origin, dir, bottom, top);
            if (t >= 0 && t <= range && dist <= 0.45f && t < bestT)
                (best, bestT) = (p, t);
        }
        return (best, bestT);
    }

    /// <summary>Distancia mínima entre un rayo y un segmento (devuelve el avance sobre el rayo y la distancia).</summary>
    public static (float T, float Distance) RaySegmentDistance(Vector3 o, Vector3 d, Vector3 a, Vector3 b)
    {
        var u = b - a;
        var w = o - a;
        float aa = d.Dot(d), bb = d.Dot(u), cc = u.Dot(u), dd = d.Dot(w), ee = u.Dot(w);
        var denom = aa * cc - bb * bb;
        float t, s;
        if (denom < 1e-6f) { t = 0; s = Mathf.Clamp(ee / cc, 0, 1); }
        else
        {
            s = Mathf.Clamp((aa * ee - bb * dd) / denom, 0, 1);
            t = (bb * s - dd) / aa;
        }
        var pRay = o + d * t;
        var pSeg = a + u * s;
        return (t, pRay.DistanceTo(pSeg));
    }

    private float WallDistance(Vector3 origin, Vector3 dir, float range)
    {
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * range);
        // Los vehículos en marcha no frenan las balas (se le dispara al que maneja y él dispara por la ventanilla);
        // los estacionados sí sirven de cobertura.
        query.Exclude = new Godot.Collections.Array<Rid>(_network.AllPlayers.Select(p => p.GetRid())
            .Concat(Distrito.Game.Transport.VehicleNetwork.DrivenBodies())
            .Concat(ShotTransparent?.Invoke() ?? []));
        var hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? origin.DistanceTo((Vector3)hit["position"]) : float.MaxValue;
    }

    private void ProcessAmmoFlush()
    {
        if (_network.Backend is not { } backend || _clock < _nextFlush)
            return;
        _nextFlush = _clock + AmmoFlushSeconds;
        foreach (var character in _pending.Where(kv => kv.Value > 0).Select(kv => kv.Key.Item1).Distinct().ToList())
            _ = FlushAmmoAsync(backend, character);
    }

    private async Task FlushAmmoAsync(Distrito.Shared.Api.DistritoApiClient backend, Guid character)
    {
        if (!_flushing.Add(character))
            return;
        try
        {
            foreach (var w in Weapons.All)
            {
                var key = (character, w.AmmoItem);
                var rounds = _pending.GetValueOrDefault(key);
                if (rounds <= 0)
                    continue;
                var result = await backend.UseAmmoAsync(_network.ServerKey, new AmmoUseRequest(character, w.AmmoItem, rounds, Guid.NewGuid().ToString()));
                if (result.IsSuccess)
                    _pending[key] = Math.Max(0, _pending.GetValueOrDefault(key) - rounds);
            }
        }
        finally
        {
            _flushing.Remove(character);
        }
        if (_network.PeerOfCharacter(character) is { } peer)
            _economy.RefreshPeer(peer);
    }

    /// <summary>Antes de tirar la mercadería de un herido hay que descontar lo que disparó, y vuelve a las piñas.</summary>
    /// <summary>
    /// Antes de que un objeto salga del inventario (darlo o soltarlo): descuenta las balas ya disparadas y, si es
    /// munición, vacía el cargador para que nadie siga tirando balas que ya no tiene.
    /// </summary>
    public async Task BeforeItemLeavesAsync(long peer, string itemCode)
    {
        foreach (var w in Weapons.All.Where(w => w.AmmoItem == itemCode))
            _loaded.Remove((peer, w.Code));
        if (_network.Backend is { } backend && _network.TryGetSession(peer, out var s))
            await FlushAmmoAsync(backend, s.CharacterId);
        SendAmmo(peer);
    }

    private async Task BeforeKnockoutAsync(PlayerCharacter victim)
    {
        SetArmed(victim.PeerId, Weapons.Fists);
        foreach (var w in Weapons.All)
            _loaded.Remove((victim.PeerId, w.Code));
        if (_network.Backend is { } backend && _network.TryGetSession(victim.PeerId, out var s))
        {
            await FlushAmmoAsync(backend, s.CharacterId);
            foreach (var w in Weapons.All)
                _ammo.Remove((s.CharacterId, w.AmmoItem));
        }
    }

    /// <summary>Prueba de impacto contra NPC de asaltos (la registra PveNetwork): id y distancia, o null.</summary>
    public Func<Vector3, Vector3, float, (string Id, float Distance)?>? NpcShot { get; set; }

    /// <summary>Cuerpos que no frenan balas (el camión de reparto: el tiro le llega al chofer).</summary>
    public Func<IEnumerable<Rid>>? ShotTransparent { get; set; }

    /// <summary>Un jugador le pegó un tiro a un NPC de asaltos.</summary>
    public Action<string, PlayerCharacter, int>? NpcHit { get; set; }

    /// <summary>¿Hay pared entre dos puntos? (para que los guardias no tiren a través de los muros)</summary>
    public bool ClearShot(Vector3 from, Vector3 to) => WallDistance(from, (to - from).Normalized(), from.DistanceTo(to)) >= from.DistanceTo(to) - 0.3f;

    /// <summary>Servidor: un NPC (guardia) le dispara a un jugador.</summary>
    public void NpcAttack(PlayerCharacter target, int damage, string attackerName, Vector3 from)
    {
        if (damage <= 0 || target.Dead || target.Health <= 0 && !target.Downed)
            return;
        ShotFxEverywhere(0, from, target.GlobalPosition + Vector3.Up * 1.2f);
        if (target.Downed)
        {
            Kill(target, null);
            return;
        }
        target.Health = Math.Max(0, target.Health - damage);
        if (target.Health > 0)
            return;
        target.Downed = true;
        _bleedUntil[target.PeerId] = _clock + CombatRules.BleedSeconds + Skill(target.PeerId, Distrito.Shared.Progression.SkillTree.ColdBlood);
        GD.Print($"[DISTRITO] COMBAT_KO {attackerName} dejó herido a {target.DisplayName}");
        SendMessage(target.PeerId, $"{attackerName} te dejó herido. Si en {CombatRules.BleedSeconds:0} s no te levanta un compañero, morís.");
    }

    /// <summary>Servidor: avisa algo a un jugador (asaltos, eventos).</summary>
    public void Notify(long peer, string text) => SendMessage(peer, text);

    /// <summary>Servidor: avisa algo a todos los conectados.</summary>
    public void NotifyAll(string text)
    {
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowMessage, text);
        if (_network.GetPlayer(Multiplayer.GetUniqueId()) is not null)
            ShowMessage(text);
    }

    private void SendAmmo(long peer)
    {
        var code = _armed.GetValueOrDefault(peer, Weapons.Fists);
        var text = Weapons.Find(code) is { } w
            ? $"{w.Name}: {_loaded.GetValueOrDefault((peer, code))}/{w.Magazine} · {(_network.Backend is null ? "∞" : Available(peer, w).ToString())} {w.AmmoName}"
              + (_reloadUntil.TryGetValue(peer, out var until) && _clock < until ? " · recargando…" : "")
            : "";
        if (peer == Multiplayer.GetUniqueId())
            ReceiveAmmo(code, text);
        else
            RpcId(peer, MethodName.ReceiveAmmo, code, text);
    }

    private void ShotFxEverywhere(long shooter, Vector3 from, Vector3 to)
    {
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShotFx, shooter, from, to);
        ShotFx(shooter, from, to);
    }

    // ================= Servidor → clientes =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveAmmo(string weapon, string text)
    {
        _myWeapon = weapon;
        AmmoText = text;
        AmmoChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReloadFx(long shooter) => _network.GetPlayer(shooter)?.PlayReload(Weapons.ReloadSeconds);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ShotFx(long shooter, Vector3 from, Vector3 to)
    {
        _network.GetPlayer(shooter)?.PlayRecoil();
        if (DisplayServer.GetName() == "headless")
            return;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        mesh.SurfaceAddVertex(from);
        mesh.SurfaceAddVertex(to);
        mesh.SurfaceEnd();
        var line = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        AddChild(line);
        GetTree().CreateTimer(0.06).Timeout += line.QueueFree;
    }
}
