using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed record HostCallbackResult
{
	public JsonElement? Data { get; private init; }

	public ProtocolError? Error { get; private init; }

	public static HostCallbackResult Ok(object? data = null)
		=> new() { Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options) };

	public static HostCallbackResult Fail(string code, string message, bool retryable = false)
		=> new() { Error = new ProtocolError { Code = code, Message = message, Retryable = retryable } };
}
