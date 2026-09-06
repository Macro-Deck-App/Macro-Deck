using System.Text.Json;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal interface IQWebChannelClient : IDisposable
{
	event EventHandler<QWebChannelSignalMessage>? SignalReceived;

	event EventHandler<QWebChannelPropertyUpdate>? PropertyUpdated;

	event EventHandler<string?>? Disconnected;

	bool IsConnected { get; }

	Task<IReadOnlyDictionary<string, QWebChannelObjectInfo>> ConnectAsync(Uri uri, CancellationToken cancellationToken);

	Task<JsonElement> InvokeAsync(
		string objectName,
		string method,
		IReadOnlyList<object?> args,
		CancellationToken cancellationToken);

	Task ConnectToSignalAsync(string objectName, string signal, CancellationToken cancellationToken);

	Task DisconnectAsync();
}

internal class QWebChannelException : Exception
{
	public QWebChannelException(string message)
		: base(message)
	{
	}

	public QWebChannelException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class QWebChannelHandshakeException : QWebChannelException
{
	public QWebChannelHandshakeException(string message)
		: base(message)
	{
	}

	public QWebChannelHandshakeException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
