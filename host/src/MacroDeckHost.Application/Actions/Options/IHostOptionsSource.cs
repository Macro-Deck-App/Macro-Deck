using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Actions.Options;

public interface IHostOptionsSource
{
	string Id { get; }

	Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken);
}
