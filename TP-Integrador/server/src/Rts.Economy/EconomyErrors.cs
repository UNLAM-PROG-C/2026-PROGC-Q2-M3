using Rts.Shared.Errors;

namespace Rts.Economy;

/// <summary>Códigos de error del área Economy (D-007).</summary>
public static class EconomyErrors
{
  /// <summary>La madera del jugador no alcanza para el costo.</summary>
  public static readonly ErrorCode InsufficientWood = new("Economy.InsufficientWood");

  /// <summary>La comida del jugador no alcanza para el costo.</summary>
  public static readonly ErrorCode InsufficientFood = new("Economy.InsufficientFood");

  /// <summary>No alcanzan ni la madera ni la comida.</summary>
  public static readonly ErrorCode InsufficientResources = new("Economy.InsufficientResources");

  /// <summary>El objetivo de un <c>GatherWood</c> existe pero no es un árbol (RECOMENDADO D-092).</summary>
  public static readonly ErrorCode InvalidGatherTarget = new("Economy.InvalidGatherTarget");

  /// <summary>El árbol objetivo de un <c>GatherWood</c> está agotado: no es recolectable hasta regenerarse (RECOMENDADO D-092).</summary>
  public static readonly ErrorCode TreeDepleted = new("Economy.TreeDepleted");
}
