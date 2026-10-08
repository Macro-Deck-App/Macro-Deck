using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.Jellyfin.Protocol;

internal sealed class JellyfinClient : IJellyfinClient
{
	internal const int MaxMessageBytes = 16 * 1024 * 1024;

	internal const string ClientName = "Macro Deck";

	private const string SessionsInterval = "0,1500";

	private static readonly TimeSpan _defaultKeepAlive = TimeSpan.FromSeconds(30);

	private static readonly TimeSpan _defaultSubscriptionTimeout = TimeSpan.FromSeconds(10);

	private static readonly ILogger _logger = IntegrationLog.For<JellyfinClient>(JellyfinIntegration.IntegrationId);

	private static readonly HttpClient _sharedHttp = IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15));

	internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly JellyfinServerSettings _settings;
	private readonly HttpClient _http;
	private readonly Func<Uri, string, CancellationToken, Task<WebSocket>> _socketConnector;
	private readonly TimeSpan _subscriptionTimeout;

	public JellyfinClient(JellyfinServerSettings settings)
		: this(settings, _sharedHttp, ConnectClientWebSocketAsync)
	{
	}

	internal JellyfinClient(
		JellyfinServerSettings settings,
		HttpClient http,
		Func<Uri, string, CancellationToken, Task<WebSocket>> socketConnector,
		TimeSpan? subscriptionTimeout = null)
	{
		_settings = settings;
		_http = http;
		_socketConnector = socketConnector;
		_subscriptionTimeout = subscriptionTimeout ?? _defaultSubscriptionTimeout;
	}

	internal string AuthorizationHeader => BuildAuthorizationHeader(_settings.DeviceId, _settings.Token);

	// Jellyfin 12 turns legacy authorization off by default, so only the MediaBrowser scheme is sent:
	// no X-Emby-Token, no api_key query.
	internal static string BuildAuthorizationHeader(string deviceId, string? token)
	{
		var builder = new StringBuilder("MediaBrowser ");
		builder.Append("Client=\"").Append(Uri.EscapeDataString(ClientName)).Append("\", ");
		builder.Append("Device=\"").Append(Uri.EscapeDataString(Environment.MachineName)).Append("\", ");
		builder.Append("DeviceId=\"").Append(Uri.EscapeDataString(deviceId)).Append("\", ");
		builder.Append("Version=\"").Append(Uri.EscapeDataString(ClientVersion)).Append('"');
		if (!string.IsNullOrEmpty(token))
		{
			builder.Append(", Token=\"").Append(Uri.EscapeDataString(token)).Append('"');
		}

		return builder.ToString();
	}

	private static string ClientVersion
		=> typeof(JellyfinClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

	internal Uri Endpoint(string relative)
	{
		var baseText = _settings.BaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
		return new Uri($"{baseText}/{relative.TrimStart('/')}");
	}

	internal Uri SocketEndpoint()
	{
		var builder = new UriBuilder(Endpoint("socket"))
		{
			Scheme = _settings.BaseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
		};
		return builder.Uri;
	}

	public async Task<JellyfinPublicSystemInfo> GetPublicInfoAsync(CancellationToken cancellationToken)
		=> await SendAsync<JellyfinPublicSystemInfo>(HttpMethod.Get, "System/Info/Public", null, cancellationToken)
			.ConfigureAwait(false) ?? new JellyfinPublicSystemInfo();

	public async Task VerifyTokenAsync(CancellationToken cancellationToken)
	{
		try
		{
			await SendAsync(HttpMethod.Get, "System/Info", null, cancellationToken).ConfigureAwait(false);
		}
		catch (JellyfinRequestException ex) when (ex.StatusCode == (int)HttpStatusCode.Forbidden)
		{
			await SendAsync(HttpMethod.Get, "Users/Me", null, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<JellyfinAuthenticationResult> AuthenticateAsync(
		string username,
		string password,
		CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, string> { ["Username"] = username, ["Pw"] = password };
		using var request = CreateRequest(HttpMethod.Post, "Users/AuthenticateByName", JsonContent.Create(body),
			includeToken: false);
		var result = await ReadAsync<JellyfinAuthenticationResult>(request, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrEmpty(result?.AccessToken))
		{
			throw new JellyfinAuthenticationException("Jellyfin returned no access token.");
		}

		return result;
	}

	public async Task<IReadOnlyList<JellyfinSessionDto>> GetSessionsAsync(CancellationToken cancellationToken)
		=> await SendAsync<List<JellyfinSessionDto>>(HttpMethod.Get, "Sessions", null, cancellationToken)
			.ConfigureAwait(false) ?? [];

	public Task SendPlaystateAsync(
		string sessionId,
		string command,
		long? seekPositionTicks,
		CancellationToken cancellationToken)
	{
		var path = $"Sessions/{Uri.EscapeDataString(sessionId)}/Playing/{Uri.EscapeDataString(command)}";
		if (seekPositionTicks is { } ticks)
		{
			path += $"?seekPositionTicks={ticks.ToString(CultureInfo.InvariantCulture)}";
		}

		return SendAsync(HttpMethod.Post, path, null, cancellationToken);
	}

	public Task SendGeneralCommandAsync(
		string sessionId,
		string command,
		IReadOnlyDictionary<string, string>? arguments,
		CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, object>
		{
			["Name"] = command,
			["Arguments"] = arguments ?? new Dictionary<string, string>()
		};

		return SendAsync(HttpMethod.Post,
			$"Sessions/{Uri.EscapeDataString(sessionId)}/Command",
			JsonContent.Create(body),
			cancellationToken);
	}

	public Task SendMessageAsync(
		string sessionId,
		string? header,
		string text,
		int? timeoutMs,
		CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, object?> { ["Text"] = text };
		if (!string.IsNullOrEmpty(header))
		{
			body["Header"] = header;
		}

		if (timeoutMs is { } timeout)
		{
			body["TimeoutMs"] = timeout;
		}

		return SendAsync(HttpMethod.Post,
			$"Sessions/{Uri.EscapeDataString(sessionId)}/Message",
			JsonContent.Create(body),
			cancellationToken);
	}

	public Task PlayNowAsync(string sessionId, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
	{
		var ids = string.Join(',', itemIds.Select(Uri.EscapeDataString));
		return SendAsync(HttpMethod.Post,
			$"Sessions/{Uri.EscapeDataString(sessionId)}/Playing?playCommand=PlayNow&itemIds={ids}",
			null,
			cancellationToken);
	}

	public async Task<IReadOnlyList<JellyfinItemDto>> SearchItemsAsync(
		string searchTerm,
		IReadOnlyList<string> itemTypes,
		string? userId,
		CancellationToken cancellationToken)
	{
		// Jellyfin 12 only applies recursive with filters when includeItemTypes is present.
		var query = new StringBuilder("Items?recursive=true&limit=25&searchTerm=")
			.Append(Uri.EscapeDataString(searchTerm))
			.Append("&includeItemTypes=")
			.Append(Uri.EscapeDataString(string.Join(',', itemTypes)));
		if (!string.IsNullOrEmpty(userId))
		{
			query.Append("&userId=").Append(Uri.EscapeDataString(userId));
		}

		var result = await SendAsync<JellyfinItemsResult>(HttpMethod.Get, query.ToString(), null, cancellationToken)
			.ConfigureAwait(false);
		return result?.Items ?? [];
	}

	public async Task<JellyfinImage?> GetPrimaryImageAsync(
		string itemId,
		string? tag,
		CancellationToken cancellationToken)
	{
		var path = $"Items/{Uri.EscapeDataString(itemId)}/Images/Primary?maxWidth=512&quality=90";
		if (!string.IsNullOrEmpty(tag))
		{
			path += $"&tag={Uri.EscapeDataString(tag)}";
		}

		using var request = CreateRequest(HttpMethod.Get, path, null, includeToken: true);
		using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		EnsureSuccess(response);
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		var mime = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
		return bytes.Length == 0 ? null : new JellyfinImage(bytes, mime);
	}

	public async Task RunSessionSocketAsync(
		Action<IReadOnlyList<JellyfinSessionDto>> onSessions,
		CancellationToken cancellationToken)
	{
		WebSocket socket;
		try
		{
			socket = await _socketConnector(SocketEndpoint(), AuthorizationHeader, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			throw new JellyfinSocketException("The Jellyfin WebSocket could not be opened.", ex);
		}

		using (socket)
		using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			var sendLock = new SemaphoreSlim(1, 1);
			var keepAlive = new KeepAliveInterval(_defaultKeepAlive);
			var keepAliveLoop = Task.Run(() => KeepAliveLoopAsync(socket, sendLock, keepAlive, linked.Token),
				CancellationToken.None);

			// Jellyfin logs a refused subscription (10.10 with an API key, non-admin users) and keeps the
			// socket open, so silence after SessionsStart is the only sign the session list will never come.
			var delivered = 0;
			using var subscriptionTimeout = new CancellationTokenSource(_subscriptionTimeout);
			using var watchdog = subscriptionTimeout.Token.Register(() =>
			{
				if (Volatile.Read(ref delivered) == 0)
				{
					linked.Cancel();
				}
			});

			try
			{
				await SendSocketMessageAsync(socket, sendLock, "SessionsStart", SessionsInterval, linked.Token)
					.ConfigureAwait(false);
				await ReceiveLoopAsync(socket,
						keepAlive,
						sessions =>
						{
							Volatile.Write(ref delivered, 1);
							onSessions(sessions);
						},
						linked.Token)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
				Volatile.Read(ref delivered) == 0)
			{
				throw new JellyfinSocketException("Jellyfin sent no session updates on the WebSocket.");
			}
			finally
			{
				await linked.CancelAsync().ConfigureAwait(false);
				try
				{
					await keepAliveLoop.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}

				if (socket.State == WebSocketState.Open)
				{
					try
					{
						using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
						await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token)
							.ConfigureAwait(false);
					}
					catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
					{
					}
				}
			}
		}
	}

	private static async Task ReceiveLoopAsync(
		WebSocket socket,
		KeepAliveInterval keepAlive,
		Action<IReadOnlyList<JellyfinSessionDto>> onSessions,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var message = new MemoryStream();

		while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
		{
			message.SetLength(0);
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}

				message.Write(buffer, 0, result.Count);
				if (message.Length > MaxMessageBytes)
				{
					throw new JellyfinRequestException("A Jellyfin WebSocket message exceeded the size limit.");
				}
			} while (!result.EndOfMessage);

			Handle(message.ToArray(), keepAlive, onSessions);
		}
	}

	private static void Handle(
		byte[] payload,
		KeepAliveInterval keepAlive,
		Action<IReadOnlyList<JellyfinSessionDto>> onSessions)
	{
		JellyfinSocketMessage? message;
		try
		{
			message = JsonSerializer.Deserialize<JellyfinSocketMessage>(payload, JsonOptions);
		}
		catch (JsonException ex)
		{
			_logger.Debug(ex, "Ignoring a malformed Jellyfin WebSocket message");
			return;
		}

		switch (message?.MessageType)
		{
			case "ForceKeepAlive" when message.Data.ValueKind == JsonValueKind.Number &&
				message.Data.TryGetInt32(out var seconds) && seconds > 1:
				keepAlive.Value = TimeSpan.FromSeconds(seconds / 2.0);
				break;
			case "Sessions" when message.Data.ValueKind == JsonValueKind.Array:
				var sessions = message.Data.Deserialize<List<JellyfinSessionDto>>(JsonOptions) ?? [];
				onSessions(sessions);
				break;
		}
	}

	private static async Task KeepAliveLoopAsync(
		WebSocket socket,
		SemaphoreSlim sendLock,
		KeepAliveInterval keepAlive,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(keepAlive.Value, cancellationToken).ConfigureAwait(false);
			if (socket.State != WebSocketState.Open)
			{
				return;
			}

			await SendSocketMessageAsync(socket, sendLock, "KeepAlive", null, cancellationToken).ConfigureAwait(false);
		}
	}

	private static async Task SendSocketMessageAsync(
		WebSocket socket,
		SemaphoreSlim sendLock,
		string messageType,
		string? data,
		CancellationToken cancellationToken)
	{
		var payload = data is null
			? JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["MessageType"] = messageType })
			: JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
				{ ["MessageType"] = messageType, ["Data"] = data });

		await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			sendLock.Release();
		}
	}

	private static async Task<WebSocket> ConnectClientWebSocketAsync(
		Uri uri,
		string authorization,
		CancellationToken cancellationToken)
	{
		var socket = IntegrationHttp.CreateWebSocket();
		socket.Options.SetRequestHeader("Authorization", authorization);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(10));
		try
		{
			await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private async Task SendAsync(
		HttpMethod method,
		string path,
		HttpContent? content,
		CancellationToken cancellationToken)
	{
		using var request = CreateRequest(method, path, content, includeToken: true);
		using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		EnsureSuccess(response);
	}

	private async Task<T?> SendAsync<T>(
		HttpMethod method,
		string path,
		HttpContent? content,
		CancellationToken cancellationToken)
	{
		using var request = CreateRequest(method, path, content, includeToken: true);
		return await ReadAsync<T>(request, cancellationToken).ConfigureAwait(false);
	}

	private async Task<T?> ReadAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		EnsureSuccess(response);
		try
		{
			return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		catch (JsonException ex)
		{
			throw new JellyfinRequestException("Jellyfin returned a response that could not be read.", null, ex);
		}
	}

	private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content, bool includeToken)
	{
		var request = new HttpRequestMessage(method, Endpoint(path)) { Content = content };
		request.Headers.TryAddWithoutValidation("Authorization",
			BuildAuthorizationHeader(_settings.DeviceId, includeToken ? _settings.Token : null));
		return request;
	}

	private static void EnsureSuccess(HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		var status = (int)response.StatusCode;
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new JellyfinAuthenticationException("Jellyfin rejected the credentials.");
		}

		throw new JellyfinRequestException($"Jellyfin answered HTTP {status}.", status);
	}

	private sealed class KeepAliveInterval(TimeSpan initial)
	{
		private long _ticks = initial.Ticks;

		public TimeSpan Value
		{
			get => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));
			set => Interlocked.Exchange(ref _ticks, value.Ticks);
		}
	}
}
