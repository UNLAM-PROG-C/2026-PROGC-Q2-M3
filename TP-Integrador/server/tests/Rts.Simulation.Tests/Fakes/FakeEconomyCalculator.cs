using Rts.Economy;
using Rts.Economy.Contracts;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake del calculador de Economy (el real es de Coro): devuelve lo que indique cada prueba (por defecto, nada) y guarda las
/// entradas para revisar qué le armó Simulation.
/// </summary>
internal sealed class FakeEconomyCalculator : IEconomyCalculator
{
  private readonly List<EconomyTickInput> _inputs = [];

  public Func<EconomyTickInput, EconomyTickResult> Respond { get; set; } = input => EconomyTickResult.Empty(input.Tick);

  public IReadOnlyList<EconomyTickInput> Inputs => _inputs;

  public EconomyTickResult CalculateTick(EconomyTickInput input)
  {
    _inputs.Add(input);
    return Respond(input);
  }
}
