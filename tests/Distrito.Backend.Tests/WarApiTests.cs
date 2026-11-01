using Distrito.Shared.Api;
using Distrito.Shared.Crews;

namespace Distrito.Backend.Tests;

/// <summary>Guerras: declarar, aviso de 10 minutos, quiénes quedan en guerra y límites.</summary>
[Trait("Category", "Database")]
public class WarApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<(Guid Leader, Guid Member, Guid Crew, string Name)> NewCrew(DistritoApiClient api)
    {
        async Task<Guid> NewCharacter()
        {
            var user = TestBackend.UniqueUser();
            await api.RegisterAsync(user, "contraseña-larga-1");
            var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
            return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        }
        var leader = await NewCharacter();
        var member = await NewCharacter();
        var name = "Banda " + Guid.NewGuid().ToString("N")[..10];
        var crew = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, name))).Value!.Status.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crew, true));
        return (leader, member, crew, name);
    }

    [Fact]
    public async Task Declarar_la_guerra_empieza_a_los_10_minutos_y_pone_en_guerra_a_las_dos_cuadrillas()
    {
        var t0 = new DateTimeOffset(2093, 4, 4, 15, 0, 0, TimeSpan.Zero);
        (Guid Leader, Guid Member, Guid Crew, string Name) a, b, c;
        await using (var factory = TestBackend.Create(clock: new TestBackend.FixedClock(t0)))
        {
            var api = TestBackend.Api(factory);
            (a, b, c) = (await NewCrew(api), await NewCrew(api), await NewCrew(api));

            Assert.Equal("not_leader", (await api.DeclareWarAsync(Key, new DeclareWarRequest(a.Member, b.Name))).Value!.ErrorCode);
            var declared = (await api.DeclareWarAsync(Key, new DeclareWarRequest(a.Leader, b.Name))).Value!;
            Assert.True(declared.Ok, declared.Message);
            var war = Assert.Single(declared.Status.Wars!);
            Assert.False(war.IsActive(t0));
            Assert.Equal("war_active", (await api.DeclareWarAsync(Key, new DeclareWarRequest(a.Leader, c.Name))).Value!.ErrorCode);
            Assert.Contains((await api.GetCrewStatusAsync(Key, b.Member)).Value!.Wars!, w => !w.DeclaredByUs);

            var before = (await api.GetCombatStateAsync(Key)).Value!;
            Assert.DoesNotContain(before.Wars ?? [], w => w.CrewA == a.Crew);
        }

        await using (var later = TestBackend.Create(clock: new TestBackend.FixedClock(t0 + WarRules.Warning + TimeSpan.FromMinutes(1))))
        {
            var api = TestBackend.Api(later);
            var state = (await api.GetCombatStateAsync(Key)).Value!;
            Assert.Contains(state.Wars!, w => w.CrewA == a.Crew && w.CrewB == b.Crew);
            Assert.Equal(a.Crew, state.WarMembers![a.Member]);
            Assert.Equal(b.Crew, state.WarMembers[b.Leader]);
            Assert.False(state.WarMembers.ContainsKey(c.Leader));
        }

        // Terminada la guerra, la misma pareja espera 24 h más.
        await using (var after = TestBackend.Create(clock: new TestBackend.FixedClock(t0 + WarRules.Warning + WarRules.Duration + TimeSpan.FromHours(1))))
        {
            var api = TestBackend.Api(after);
            Assert.Equal("war_cooldown", (await api.DeclareWarAsync(Key, new DeclareWarRequest(b.Leader, a.Name))).Value!.ErrorCode);
            Assert.True((await api.DeclareWarAsync(Key, new DeclareWarRequest(b.Leader, c.Name))).Value!.Ok);
        }
    }
}
