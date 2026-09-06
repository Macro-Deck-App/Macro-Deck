using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Events;

/// <summary>
/// Optional companion to <see cref="IEventProvider" /> for events whose configuration parameters can
/// only be filled in at edit time - the OBS scene list, a device name, a channel.
///
/// Mirrors <c>IDynamicOptionsActionDefinition</c>, but hangs off the provider rather than off a
/// definition: an event has no executor to ask.
/// </summary>
public interface IDynamicEventOptionsProvider
{
	Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context, CancellationToken cancellationToken);
}

public sealed class EventOptionsContext
{
	/// <summary>Provider-local event id, without the <c>providerId::</c> prefix.</summary>
	public required string EventId { get; init; }

	public required string ParameterName { get; init; }

	/// <summary>Free-text the user has typed, for a filterable autocomplete.</summary>
	public string? Filter { get; init; }

	/// <summary>The other configuration values, so options can depend on an earlier selection.</summary>
	public IReadOnlyDictionary<string, object?> CurrentParameters { get; init; }
		= new Dictionary<string, object?>(StringComparer.Ordinal);
}
