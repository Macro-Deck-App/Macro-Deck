using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;

namespace MacroDeck.Plugin.Protocol.Capabilities.Events;

/// <summary>
/// Mirrors the SDK's <c>EventDefinition</c>. <see cref="DeliveryKind" /> is a string, not the SDK's
/// <c>EventDeliveryKind</c> enum - see <c>ActionParameterDto</c>'s remarks for why every wire DTO in
/// this project follows that rule. Parameters reuse <see cref="ActionParameterDto" /> rather than a
/// parallel type: an event's configuration and payload fields are described with exactly the same
/// schema an action's parameters are.
/// </summary>
public sealed record EventDescriptorDto
{
	/// <summary>Provider-local event id, unqualified.</summary>
	public required string LocalId { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	public LocalizedText? Category { get; init; }

	public string? IconName { get; init; }

	/// <summary>One of the SDK's <c>EventDeliveryKind</c> member names, "Push" or "Scheduled".</summary>
	public required string DeliveryKind { get; init; }

	public required IReadOnlyList<ActionParameterDto> ConfigurationParameters { get; init; }

	public required IReadOnlyList<ActionParameterDto> PayloadParameters { get; init; }
}

/// <summary>The full result of the <c>events</c> capability's <c>describe</c> operation.</summary>
public sealed record EventCatalogPayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<EventDescriptorDto> Events { get; init; }

	/// <summary>
	/// Whether this plugin's events provider also implements <c>IDynamicEventOptionsProvider</c>. There
	/// was no wire signal for this before this kind existed, so the host's adapter factory hard-coded
	/// it to false - this flag is what lets it pick the right leaf. See
	/// <c>RemotePluginIntegrationFactory</c> and <c>RemotePluginCapabilitySnapshot.HasDynamicEventOptions</c>.
	/// </summary>
	public bool HasDynamicEventOptions { get; init; }
}

/// <summary>Arguments for the <c>options</c> operation, mirroring <c>EventOptionsContext</c>.</summary>
public sealed record EventOptionsArguments
{
	/// <summary>Provider-local event id, without the <c>providerId::</c> prefix.</summary>
	public required string EventId { get; init; }

	public required string ParameterName { get; init; }

	public string? Filter { get; init; }

	public IReadOnlyDictionary<string, System.Text.Json.JsonElement> CurrentParameters { get; init; }
		= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
}
