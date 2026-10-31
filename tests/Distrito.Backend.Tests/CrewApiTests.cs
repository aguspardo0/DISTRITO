using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Npgsql;

namespace Distrito.Backend.Tests;

[Trait("Category", "Database")]
public class CrewApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    private static string UniqueCrewName() => "Banda " + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Fundar_invitar_aceptar_y_ver_miembros()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var recruit = await NewCharacter(api);
        var name = UniqueCrewName();

        var created = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, name))).Value!;
        Assert.True(created.Ok, created.Message);
        Assert.Equal(name, created.Status.CrewName);
        Assert.True(created.Status.IsLeader);

        var invited = (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, recruit))).Value!;
        Assert.True(invited.Ok, invited.Message);

        var pending = (await api.GetCrewStatusAsync(Key, recruit)).Value!;
        var invitation = Assert.Single(pending.Invitations);
        Assert.Equal(name, invitation.CrewName);

        var joined = (await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(recruit, invitation.CrewId, Accept: true))).Value!;
        Assert.True(joined.Ok, joined.Message);
        Assert.Equal(2, joined.Status.Members.Count);
        Assert.False(joined.Status.IsLeader);
        Assert.Empty(joined.Status.Invitations);
    }

    [Fact]
    public async Task Solo_el_jefe_invita_y_nadie_esta_en_dos_cuadrillas()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var member = await NewCharacter(api);
        var outsider = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));

        var byMember = (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(member, outsider))).Value!;
        Assert.Equal("not_allowed", byMember.ErrorCode);

        var secondCrew = (await api.CreateCrewAsync(Key, new CreateCrewRequest(member, UniqueCrewName()))).Value!;
        Assert.Equal("already_in_crew", secondCrew.ErrorCode);

        var otherLeader = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(otherLeader, UniqueCrewName()));
        var poach = (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(otherLeader, member))).Value!;
        Assert.Equal("target_in_crew", poach.ErrorCode);
    }

    [Fact]
    public async Task Nombre_de_cuadrilla_unico_y_valido()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var name = UniqueCrewName();
        await api.CreateCrewAsync(Key, new CreateCrewRequest(await NewCharacter(api), name));

        var taken = (await api.CreateCrewAsync(Key, new CreateCrewRequest(await NewCharacter(api), name.ToLowerInvariant()))).Value!;
        Assert.Equal("name_taken", taken.ErrorCode);

        var invalid = (await api.CreateCrewAsync(Key, new CreateCrewRequest(await NewCharacter(api), "x"))).Value!;
        Assert.Equal("invalid_name", invalid.ErrorCode);
    }

    [Fact]
    public async Task Rechazar_invitacion_la_elimina()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var target = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, target));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;

        var declined = (await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(target, crewId, false))).Value!;
        Assert.True(declined.Ok);
        Assert.False(declined.Status.InCrew);
        Assert.Empty(declined.Status.Invitations);

        var lateAccept = (await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(target, crewId, true))).Value!;
        Assert.Equal("no_invitation", lateAccept.ErrorCode);
    }

    [Fact]
    public async Task Si_el_jefe_se_va_hereda_el_mas_antiguo_y_al_vaciarse_se_disuelve()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var first = await NewCharacter(api);
        var second = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        foreach (var recruit in new[] { first, second })
        {
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, recruit));
            await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(recruit, crewId, true));
        }

        Assert.True((await api.LeaveCrewAsync(Key, new LeaveCrewRequest(leader))).Value!.Ok);
        Assert.True((await api.GetCrewStatusAsync(Key, first)).Value!.IsLeader);
        Assert.False((await api.GetCrewStatusAsync(Key, second)).Value!.IsLeader);

        await api.LeaveCrewAsync(Key, new LeaveCrewRequest(first));
        Assert.True((await api.GetCrewStatusAsync(Key, second)).Value!.IsLeader);

        var last = (await api.LeaveCrewAsync(Key, new LeaveCrewRequest(second))).Value!;
        Assert.True(last.Ok);
        Assert.Contains("disolvió", last.Message);
    }

    [Fact]
    public async Task El_cupo_se_respeta_aunque_acepten_todos_a_la_vez()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;

        // Se invita a más gente que lugares libres, antes de que nadie acepte.
        var recruits = new List<Guid>();
        var capacity = CrewRules.CapacityForLevel(1);
        for (var i = 0; i < capacity + 2; i++)
        {
            var id = await NewCharacter(api);
            recruits.Add(id);
            Assert.True((await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, id))).Value!.Ok);
        }

        var results = await Task.WhenAll(recruits.Select(r =>
            api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(r, crewId, true))));

        Assert.Equal(capacity - 1, results.Count(r => r.Value!.Ok));
        Assert.Equal(capacity, (await api.GetCrewStatusAsync(Key, leader)).Value!.Members.Count);
    }

    [Fact]
    public async Task La_cuadrilla_queda_formada_al_llegar_a_tres_y_el_cupo_inicial_es_cinco()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var created = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()))).Value!;
        Assert.False(created.Status.IsFormed);
        Assert.Equal(3, created.Status.Capacity); // hoja de ruta v0.2.1: capacidad inicial 3
        var crewId = created.Status.CrewId!.Value;

        CrewActionResult? last = null;
        for (var i = 0; i < 2; i++)
        {
            var recruit = await NewCharacter(api);
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, recruit));
            last = (await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(recruit, crewId, true))).Value!;
        }

        Assert.True(last!.Status.IsFormed);
        Assert.Contains("formada", last.Message);

        // Historial visible: fundación y dos altas, el más reciente primero.
        var history = last.Status.History!;
        Assert.Equal(3, history.Count);
        Assert.Equal("joined", history[0].Kind);
        Assert.Equal("founded", history[^1].Kind);

        // Con 3 integrantes está llena: no se puede invitar a un cuarto.
        var fourth = await NewCharacter(api);
        Assert.Equal("crew_full", (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, fourth))).Value!.ErrorCode);
    }

    [Fact]
    public async Task Jefe_inactivo_mas_de_14_dias_pierde_el_mando_ante_un_miembro_activo()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var member = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));

        // Activo hace poco: no cambia nada.
        Assert.True((await api.GetCrewStatusAsync(Key, leader)).Value!.IsLeader);

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using (var age = db.CreateCommand("UPDATE distrito.characters SET last_seen_at = now() - interval '15 days' WHERE id = @c"))
        {
            age.Parameters.AddWithValue("c", leader);
            await age.ExecuteNonQueryAsync();
        }

        var memberView = (await api.GetCrewStatusAsync(Key, member)).Value!;
        Assert.True(memberView.IsLeader);
        Assert.False((await api.GetCrewStatusAsync(Key, leader)).Value!.IsLeader);

        await using var events = db.CreateCommand("SELECT count(*) FROM distrito.crew_events WHERE crew_id = @crew AND kind = 'leadership_inactivity'");
        events.Parameters.AddWithValue("crew", crewId);
        Assert.Equal(1L, (long)(await events.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Si_nadie_esta_activo_el_jefe_inactivo_conserva_el_mando()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var member = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using (var age = db.CreateCommand("UPDATE distrito.characters SET last_seen_at = now() - interval '30 days' WHERE id = ANY(@ids)"))
        {
            age.Parameters.AddWithValue("ids", new[] { leader, member });
            await age.ExecuteNonQueryAsync();
        }

        Assert.True((await api.GetCrewStatusAsync(Key, leader)).Value!.IsLeader);
    }

    [Theory]
    [InlineData("Los Pibes", true)]
    [InlineData("Banda 77", true)]
    [InlineData("ab", false)]
    [InlineData("Doble  espacio", false)]
    [InlineData("Signos!!", false)]
    public void Validacion_de_nombre(string name, bool valid) =>
        Assert.Equal(valid, CrewRules.ValidateName(name) is null);

    [Fact]
    public async Task Roles_segundo_invita_y_solo_el_jefe_nombra_y_expulsa()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var second = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, second));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(second, crewId, true));

        // Un miembro común no puede nombrar ni expulsar.
        Assert.Equal("not_leader", (await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(second, leader, CrewRoles.Second))).Value!.ErrorCode);
        Assert.Equal("not_leader", (await api.KickFromCrewAsync(Key, new KickFromCrewRequest(second, leader))).Value!.ErrorCode);

        // El jefe lo nombra segundo; ahora puede invitar.
        var appointed = (await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(leader, second, CrewRoles.Second))).Value!;
        Assert.True(appointed.Ok, appointed.Message);
        var recruit = await NewCharacter(api);
        var invite = (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(second, recruit))).Value!;
        Assert.True(invite.Ok, invite.Message);
        var secondView = (await api.GetCrewStatusAsync(Key, second)).Value!;
        Assert.Equal(CrewRoles.Second, secondView.MyRole);
        Assert.True(secondView.CanInvite);

        // El segundo tampoco puede expulsar; el jefe sí.
        Assert.Equal("not_leader", (await api.KickFromCrewAsync(Key, new KickFromCrewRequest(second, leader))).Value!.ErrorCode);
        var kicked = (await api.KickFromCrewAsync(Key, new KickFromCrewRequest(leader, second))).Value!;
        Assert.True(kicked.Ok, kicked.Message);
        Assert.False((await api.GetCrewStatusAsync(Key, second)).Value!.InCrew);
        Assert.Contains(kicked.Status.History!, e => e.Kind == "kicked");
        Assert.Contains(kicked.Status.History!, e => e.Kind == "role_second");

        // No se puede expulsar a alguien de otra cuadrilla ni a uno mismo.
        Assert.Equal("invalid_target", (await api.KickFromCrewAsync(Key, new KickFromCrewRequest(leader, leader))).Value!.ErrorCode);
        Assert.Equal("not_member", (await api.KickFromCrewAsync(Key, new KickFromCrewRequest(leader, second))).Value!.ErrorCode);
    }

    [Fact]
    public async Task Solo_hay_un_segundo_a_la_vez()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, UniqueCrewName()));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        var a = await NewCharacter(api);
        var b = await NewCharacter(api);
        foreach (var m in new[] { a, b })
        {
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, m));
            await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(m, crewId, true));
        }

        await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(leader, a, CrewRoles.Second));
        var status = (await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(leader, b, CrewRoles.Second))).Value!.Status;

        Assert.Single(status.Members, m => m.Role == CrewRoles.Second);
        Assert.Equal(CrewRoles.Second, status.Members.Single(m => m.CharacterId == b).Role);
    }

    [Fact]
    public void Capacidad_progresiva_3_4_5()
    {
        Assert.Equal(3, CrewRules.CapacityForLevel(1));
        Assert.Equal(4, CrewRules.CapacityForLevel(2));
        Assert.Equal(5, CrewRules.CapacityForLevel(3));
    }
}
