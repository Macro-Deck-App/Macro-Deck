using System.Text.Json;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal interface IYtmDesktopRealtimeClient : IDisposable
{
	event EventHandler<JsonElement>? StateUpdated;

	event EventHandler<YtmPlaylist>? PlaylistCreated;

	event EventHandler<string>? PlaylistDeleted;

	event EventHandler<string?>? Disconnected;

	bool IsConnected { get; }

	Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken);

	Task DisconnectAsync();
}
