using System.Text.Json;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal readonly record struct StreamlabsEvent(string ResourceId, JsonElement Data);

internal interface IStreamlabsClient : IDisposable
{
	bool IsConnected { get; }

	event EventHandler<StreamlabsEvent>? EventReceived;

	event EventHandler<string?>? Disconnected;

	Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken);

	Task<JsonElement> InvokeAsync(
		string resource,
		string method,
		IReadOnlyList<object?>? args,
		CancellationToken cancellationToken);

	Task<string> SubscribeAsync(string service, string observable, CancellationToken cancellationToken);

	Task DisconnectAsync();
}
