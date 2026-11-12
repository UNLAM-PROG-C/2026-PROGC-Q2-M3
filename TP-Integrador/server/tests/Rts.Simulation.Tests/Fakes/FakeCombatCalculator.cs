using Rts.Combat;
using Rts.Combat.Contracts;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake del calculador de Combat (el real es de Coro): devuelve lo que indique cada prueba (por defecto, nada) y guarda las
/// entradas para revisar qué le armó Simulation.
/// </summary>
internal sealed class FakeCombatCalculator : ICombatCalculator
{
  private readonly List<CombatTickInput> _inputs = [];

  public Func<CombatTickInput, CombatTickResult> Respond { get; set; } = input => CombatTickResult.Empty(input.Tick);

  public IReadOnlyList<CombatTickInput> Inputs => _inputs;

  public CombatTickResult CalculateTick(CombatTickInput input)
  {
    lock (_inputs)
    {
      _inputs.Add(input);
    }

    return Respond(input);
  }
}
