using Distrito.Server.Core.Movement;

namespace Distrito.Server.Core.Tests;

public class MovementTests
{
    [Fact]
    public void Una_direccion_valida_no_se_modifica()
    {
        var input = new MovementInput(0.6f, -0.8f, Jump: false);
        Assert.Equal(input, MovementRules.Sanitize(input));
    }

    [Fact]
    public void Un_vector_gigante_se_limita_a_longitud_uno()
    {
        var sanitized = MovementRules.Sanitize(new MovementInput(300f, 400f, Jump: false));

        Assert.Equal(0.6f, sanitized.X, precision: 4);
        Assert.Equal(0.8f, sanitized.Z, precision: 4);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, float.NaN)]
    public void Valores_no_finitos_se_descartan(float x, float z)
    {
        var sanitized = MovementRules.Sanitize(new MovementInput(x, z, Jump: true));

        Assert.Equal(0f, sanitized.X);
        Assert.Equal(0f, sanitized.Z);
        Assert.True(sanitized.Jump);
    }

    [Fact]
    public void La_velocidad_nunca_supera_la_de_caminar()
    {
        var velocity = MovementRules.HorizontalVelocity(MovementRules.Sanitize(new MovementInput(1e6f, 1e6f, false)));
        Assert.True(velocity.Length() <= MovementRules.WalkSpeed + 1e-4f);
    }

    [Fact]
    public void La_entrada_vence_si_el_cliente_deja_de_mandar()
    {
        var state = new PlayerInputState();
        state.Update(new MovementInput(1f, 0f, false), nowSeconds: 10.0);

        Assert.Equal(1f, state.Current(10.1).X);
        Assert.Equal(MovementInput.None, state.Current(10.0 + PlayerInputState.InputTimeout.TotalSeconds + 0.01));
    }

    [Fact]
    public void Sin_entradas_el_personaje_queda_quieto()
    {
        Assert.Equal(MovementInput.None, new PlayerInputState().Current(0));
    }

    [Fact]
    public void El_estado_guarda_la_entrada_ya_saneada()
    {
        var state = new PlayerInputState();
        state.Update(new MovementInput(10f, 0f, false), nowSeconds: 0);
        Assert.Equal(1f, state.Current(0).X);
    }
}
