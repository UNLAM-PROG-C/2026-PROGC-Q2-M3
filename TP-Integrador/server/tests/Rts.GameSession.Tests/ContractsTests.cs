using Rts.GameSession.Actions;
using Rts.GameSession.Configuration;
using Rts.GameSession.Model;
using Rts.Shared.Ids;
using Rts.Shared.Session;

namespace Rts.GameSession.Tests;

public class ContractsTests
{
    private static readonly SessionId Session = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));

    [Fact]
    public void Config_RequiresPositiveValues()
    {
        var valid = new GameSessionConfig { ReadyTimeout = TimeSpan.FromSeconds(30), EventQueueCapacity = 64 };
        GameSessionConfig invalid = valid with { ReadyTimeout = TimeSpan.Zero, EventQueueCapacity = 0 };

        Assert.Empty(valid.GetValidationErrors());
        Assert.Equal(2, invalid.GetValidationErrors().Count);
        Assert.Throws<ArgumentException>(invalid.Validate);
    }

    [Fact]
    public void Transitions_AreStructurallyEqual()
    {
        // Habilita pruebas de determinismo: mismo estado + mismo evento ⇒ transición igual.
        GameSessionState state = GameSessionState.Initial(Session);
        var conn = new ConnectionId(7);

        SessionTransition a = new(state, [new SendSessionMessage(conn, new PlayerAssigned(Session, PlayerId.One)), new StartSimulation()]);
        SessionTransition b = new(state, [new SendSessionMessage(conn, new PlayerAssigned(Session, PlayerId.One)), new StartSimulation()]);

        Assert.Equal(a, b);
        Assert.Empty(SessionTransition.Unchanged(state).Actions);
    }

    [Fact]
    public void Errors_BelongToSessionArea()
    {
        Assert.All(
            [SessionErrors.MatchFull, SessionErrors.NotRegistered, SessionErrors.AlreadyRegistered, SessionErrors.NotStarting,
             SessionErrors.StaleInitialization, SessionErrors.ReadyTimeoutExpired],
            code => Assert.Equal("Session", code.Area));
    }
}
