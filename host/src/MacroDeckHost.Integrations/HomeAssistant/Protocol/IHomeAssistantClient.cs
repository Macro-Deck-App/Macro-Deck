using System.Text.Json;

namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal interface IHomeAssistantClient : IDisposable
{
	event EventHandler<HomeAssistantEventMessage>? EventReceived;

	event EventHandler<string?>? Disconnected;

	bool IsConnected { get; }

	Task<HomeAssistantHello> ConnectAsync(Uri uri, string token, CancellationToken cancellationToken);

	Task<JsonElement> SendCommandAsync(
		string type,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default);

	Task DisconnectAsync();
}
