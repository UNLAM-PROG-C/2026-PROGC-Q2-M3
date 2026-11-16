using Rts.Combat.Calculations;
using Rts.Combat.Contracts;
using Rts.Shared.Collections;

namespace Rts.Combat.Tests;

/// <summary>Envuelve el subcálculo real de soldados y lo hace terminar primero o segundo.</summary>
internal sealed class GatedSoldiers(ISoldierCombatCalculation inner, CompletionOrder order, bool finishesFirst)
    : ISoldierCombatCalculation
{
  public const string Name = "Soldiers";

  public SoldierCombatOutcome Calculate(CombatTickInput input) => order.Run(Name, finishesFirst, () => inner.Calculate(input));
}

/// <summary>Envuelve el subcálculo real de torres y lo hace terminar primero o segundo.</summary>
internal sealed class GatedTowers(ITowerCombatCalculation inner, CompletionOrder order, bool finishesFirst) : ITowerCombatCalculation
{
  public const string Name = "Towers";

  public EquatableArray<AttackPerformed> Calculate(CombatTickInput input) =>
      order.Run(Name, finishesFirst, () => inner.Calculate(input));
}
