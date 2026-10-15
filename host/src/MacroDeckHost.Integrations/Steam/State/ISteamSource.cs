namespace MacroDeckHost.Integrations.Steam.State;

public interface ISteamSource
{
	event EventHandler? Changed;

	bool IsAvailable { get; }

	SteamSnapshot Snapshot { get; }

	Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken);
}
