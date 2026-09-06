using System.Text.Json;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordRpcEventArgs : EventArgs
{
	public DiscordRpcEventArgs(string eventName, JsonElement data)
	{
		EventName = eventName;
		Data = data;
	}

	public string EventName { get; }

	public JsonElement Data { get; }
}

internal interface IDiscordRpcClient : IDisposable
{
	bool IsConnected { get; }

	event EventHandler<DiscordRpcEventArgs>? EventReceived;

	event EventHandler<string?>? Disconnected;

	Task<JsonElement> ConnectAsync(string clientId, CancellationToken cancellationToken);

	Task<JsonElement> SendCommandAsync(
		string command,
		object? args = null,
		string? eventName = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);
}
