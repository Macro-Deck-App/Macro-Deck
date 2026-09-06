using System.Text.Json;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal sealed record QWebChannelPropertyUpdate(string Object, IReadOnlyDictionary<string, JsonElement> Properties);
