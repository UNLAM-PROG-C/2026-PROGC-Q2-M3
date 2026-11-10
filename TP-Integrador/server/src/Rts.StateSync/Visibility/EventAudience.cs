using Rts.Shared.Events;
using Rts.Shared.Ids;

namespace Rts.StateSync.Visibility;

/// <summary>Destinatarios de un evento: ambos jugadores o uno solo (09 §4).</summary>
public readonly record struct EventAudience
{
  private EventAudience(PlayerId? onlyPlayer) => OnlyPlayer = onlyPlayer;

  /// <summary>Visible para ambos jugadores.</summary>
  public static EventAudience AllPlayers => new(null);

  /// <summary>Jugador único destinatario, o <c>null</c> si es para ambos.</summary>
  public PlayerId? OnlyPlayer { get; }

  public bool IsForAllPlayers => OnlyPlayer is null;

  /// <summary>Visible solo para ese jugador (dueño del dato o emisor del comando).</summary>
  public static EventAudience Only(PlayerId player)
  {
    if (!player.IsValid)
    {
      throw new ArgumentException($"PlayerId inválido: {player}.", nameof(player));
    }

    return new EventAudience(player);
  }

  /// <summary>Indica si el jugador recibe el evento.</summary>
  public bool Includes(PlayerId player) => OnlyPlayer is not { } only || only == player;
}

/// <summary>
/// Política de visibilidad de los eventos de dominio (contrato 09 §3). Decide <b>solo con el evento</b>, sin estado: los eventos
/// privados traen su dueño (D-063). Lanza si recibe un tipo de evento que no conoce, para que un evento nuevo no se publique
/// por accidente a quien no corresponde.
/// </summary>
public interface IEventAudiencePolicy
{
  EventAudience GetAudience(IDomainEventPayload payload);
}
