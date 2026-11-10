namespace Rts.Economy.Model;

/// <summary>Estado económico de un árbol (01 §3).</summary>
public enum TreeStatus
{
  /// <summary>Visible y recolectable; tiene madera.</summary>
  Available = 1,

  /// <summary>Agotado: invisible y no recolectable hasta su regeneración. Conserva ID, posición y celdas reservadas.</summary>
  Depleted = 2,
}
