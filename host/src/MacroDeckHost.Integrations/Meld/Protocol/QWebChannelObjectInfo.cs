using System.Text.Json;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal sealed record QWebChannelObjectInfo(
	string Name,
	IReadOnlyDictionary<string, int> Methods,
	IReadOnlyDictionary<string, int> Signals,
	IReadOnlyDictionary<int, string> SignalNames,
	IReadOnlyDictionary<int, string> PropertyNames,
	IReadOnlyDictionary<string, JsonElement> InitialProperties);
