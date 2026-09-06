using System.Text.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Plugins;

public static class PluginProtocolHttp
{
	public static async Task<T?> ReadBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return await JsonSerializer.DeserializeAsync<T>(request.Body,
				PluginProtocolJson.Options,
				cancellationToken);
		}
		catch (JsonException)
		{
			return default;
		}
	}

	public static IActionResult Json<T>(T value, int statusCode) => new ContentResult
	{
		Content = JsonSerializer.Serialize(value, PluginProtocolJson.Options),
		ContentType = ProtocolConstants.JsonMediaType,
		StatusCode = statusCode
	};

	public static IActionResult Error(ProtocolError error, int statusCode) => Json(error, statusCode);
}
