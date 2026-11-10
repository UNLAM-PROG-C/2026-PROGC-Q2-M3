using Rts.Shared.Errors;

namespace Rts.Production;

/// <summary>
/// Códigos de error del área Production (D-007). Para productores inexistentes o ajenos se usan <c>Common.EntityNotFound</c>
/// y <c>Common.NotOwner</c>. La falta de comida no es un error: la solicitud queda en <c>WaitingForResources</c>.
/// </summary>
public static class ProductionErrors
{
    /// <summary>El edificio no entrena unidades.</summary>
    public static readonly ErrorCode NotAProducer = new("Production.NotAProducer");

    /// <summary>El productor no entrena ese tipo (el Centro Principal solo trabajadores y el Cuartel solo soldados).</summary>
    public static readonly ErrorCode WrongUnitType = new("Production.WrongUnitType");

    /// <summary>El productor todavía no está completado u operativo (un Cuartel terminado en N opera desde N+1).</summary>
    public static readonly ErrorCode ProducerNotOperational = new("Production.ProducerNotOperational");

    /// <summary>La cola de pendientes está llena; se rechaza sin efectos (05 §2).</summary>
    public static readonly ErrorCode QueueFull = new("Production.QueueFull");
}
