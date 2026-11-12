using Rts.Construction;
using Rts.Construction.Contracts;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake del calculador de Construction (el real es de Ivan): devuelve lo que indique cada prueba (por defecto, nada) y guarda las
/// entradas para revisar qué le armó Simulation.
/// </summary>
internal sealed class FakeConstructionCalculator : IConstructionCalculator
{
  private readonly List<ConstructionTickInput> _inputs = [];

  public Func<ConstructionTickInput, ConstructionTickResult> Respond { get; set; } = input => ConstructionTickResult.Empty(input.Tick);

  public IReadOnlyList<ConstructionTickInput> Inputs => _inputs;

  public ConstructionTickResult CalculateTick(ConstructionTickInput input)
  {
    _inputs.Add(input);
    return Respond(input);
  }
}
