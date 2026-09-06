using System.Text.Json;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal sealed record QWebChannelSignalMessage(string Object, string Signal, IReadOnlyList<JsonElement> Args);
