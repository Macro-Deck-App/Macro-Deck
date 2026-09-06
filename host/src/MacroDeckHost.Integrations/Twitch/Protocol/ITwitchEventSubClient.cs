namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal interface ITwitchEventSubClient : IDisposable
{
	event EventHandler<TwitchEventSubMessage>? MessageReceived;

	event EventHandler<string?>? Disconnected;

	bool IsConnected { get; }

	Task<TwitchSessionWelcome> ConnectAsync(Uri uri, CancellationToken cancellationToken);

	Task DisconnectAsync();
}
