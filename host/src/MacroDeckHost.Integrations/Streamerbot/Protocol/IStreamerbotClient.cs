using System.Text.Json;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal interface IStreamerbotClient : IDisposable
{
	event EventHandler<StreamerbotEventMessage>? EventReceived;

	event EventHandler<string?>? Disconnected;

	bool IsConnected { get; }

	Task<StreamerbotHello?> ConnectAsync(Uri uri, CancellationToken cancellationToken);

	Task<JsonElement> RequestAsync(
		string request,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default);

	Task DisconnectAsync();
}

internal sealed class StreamerbotRequestException : Exception
{
	public StreamerbotRequestException(string request, string? error)
		: base($"Streamer.bot rejected '{request}': {error ?? "unknown error"}")
	{
		Request = request;
	}

	public StreamerbotRequestException(string message)
		: base(message)
	{
		Request = string.Empty;
	}

	public StreamerbotRequestException(string message, Exception innerException)
		: base(message, innerException)
	{
		Request = string.Empty;
	}

	public string Request { get; }
}

internal sealed class StreamerbotAuthenticationException : Exception
{
	public StreamerbotAuthenticationException(string message)
		: base(message)
	{
	}

	public StreamerbotAuthenticationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
