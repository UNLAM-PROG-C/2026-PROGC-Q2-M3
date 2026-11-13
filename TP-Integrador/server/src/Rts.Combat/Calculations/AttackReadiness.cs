using Rts.Shared.Ids;

namespace Rts.Combat.Calculations;

/// <summary>
/// Cooldown (RECOMENDADO D-022): el valor pertenece al atacante, no a la orden, así que cambiar de objetivo no lo
/// reinicia. Un atacante nuevo arranca listo, por eso su primer ataque es inmediato.
/// </summary>
internal static class AttackReadiness
{
  /// <summary>Un atacante está listo si <c>Tick &gt;= ReadyAtTick</c>.</summary>
  public static bool IsReady(TickId tick, TickId readyAtTick) => tick >= readyAtTick;

  /// <summary>Al atacar en el tick T, vuelve a estar listo en <c>T + CooldownTicks</c> de su tipo.</summary>
  public static TickId NextReadyTick(TickId attackTick, int cooldownTicks) => attackTick.Plus(cooldownTicks);
}
