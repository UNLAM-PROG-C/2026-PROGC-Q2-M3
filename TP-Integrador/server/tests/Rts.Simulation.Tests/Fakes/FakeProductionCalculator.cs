using Rts.Production;
using Rts.Production.Contracts;

namespace Rts.Simulation.Tests.Fakes;

/// <summary>
/// Fake del calculador de Production (el real es de Joaco): devuelve lo que indique cada prueba (por defecto, nada) y guarda las
/// entradas para revisar qué le armó Simulation.
/// </summary>
internal sealed class FakeProductionCalculator : IProductionCalculator
{
  private readonly List<ProductionTickInput> _inputs = [];

  public Func<ProductionTickInput, ProductionTickResult> Respond { get; set; } = input => ProductionTickResult.Empty(input.Tick);

  public IReadOnlyList<ProductionTickInput> Inputs => _inputs;

  public ProductionTickResult CalculateTick(ProductionTickInput input)
  {
    _inputs.Add(input);
    return Respond(input);
  }
}
