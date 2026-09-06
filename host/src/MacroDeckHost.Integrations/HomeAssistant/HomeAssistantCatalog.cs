using MacroDeckHost.Integrations.HomeAssistant.Protocol;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal sealed record HomeAssistantCatalog
{
	public static HomeAssistantCatalog Empty { get; } = new();

	public IReadOnlyDictionary<string, HomeAssistantEntityState> Entities { get; init; }
		= new Dictionary<string, HomeAssistantEntityState>(StringComparer.Ordinal);

	public IReadOnlyList<string> Domains { get; init; } = [];

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Services { get; init; }
		= HomeAssistantResponses.EmptyServices;

	public IReadOnlyList<HomeAssistantAreaInfo> Areas { get; init; } = [];

	public IReadOnlyList<HomeAssistantDeviceInfo> Devices { get; init; } = [];

	public IReadOnlyDictionary<string, string> EntityAreas { get; init; } = HomeAssistantResponses.EmptyEntityAreas;

	public HomeAssistantEntityState? Entity(string? entityId)
		=> entityId is { Length: > 0 } id && Entities.TryGetValue(id, out var state) ? state : null;

	public string? AreaOf(string entityId) => EntityAreas.GetValueOrDefault(entityId);
}
