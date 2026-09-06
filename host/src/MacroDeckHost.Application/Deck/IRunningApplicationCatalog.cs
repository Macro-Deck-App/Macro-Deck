namespace MacroDeckHost.Application.Deck;

public interface IRunningApplicationCatalog
{
	Task<IReadOnlyList<RunningApplication>> GetAsync(string? filter, CancellationToken cancellationToken);
}
