using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Tests.SystemTests.Support;

// Hand-written JSON from the published protocol reference instead of the SDK, which always requests the
// newest major, so the probe can speak a major an older plugin was built for.
internal sealed class RawProtocolPlugin : IAsyncDisposable
{
	public const string SubProtocol = "macrodeck.plugin.v1";

	private readonly ClientWebSocket _socket = new();

	private RawProtocolPlugin(string sessionId, int negotiatedVersion)
	{
		SessionId = sessionId;
		NegotiatedVersion = negotiatedVersion;
	}

	public string SessionId { get; }

	public int NegotiatedVersion { get; }

	public List<JsonNode> Received { get; } = [];

	// Developer-token enrollment, the documented headless path: the admin mints the token, the plugin
	// trades it for its own secret.
	public static async Task<(string PluginId, string Secret)> EnrollAsync(HostInstance host, string pluginId)
	{
		using (var developer = await host.Api.PutAsJsonAsync("/api/settings/developer", new { enabled = true }))
		{
			developer.EnsureSuccessStatusCode();
		}

		using var minted = await host.PostAsync("/api/plugin-tokens", new { name = "compat", expiresInDays = 1 });
		minted.EnsureSuccessStatusCode();
		var token = (string)JsonNode.Parse(await minted.Content.ReadAsStringAsync())!["plaintext"]!;

		using var plugin = new HttpClient { BaseAddress = host.Api.BaseAddress };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/plugins/registration")
		{
			Content = Json($$"""{"pluginId":"{{pluginId}}","displayName":"Compat Probe"}""")
		};
		request.Headers.Add("X-MacroDeck-Enrollment-Token", token);
		using var registered = await plugin.SendAsync(request);
		var body = JsonNode.Parse(await registered.Content.ReadAsStringAsync())!;
		Assert.That((int)registered.StatusCode, Is.EqualTo(201), body.ToJsonString());
		return ((string)body["pluginId"]!, (string)body["pluginSecret"]!);
	}

	public static async Task<(int Status, JsonNode Body)> RequestSessionAsync(
		HostInstance host,
		(string PluginId, string Secret) credential,
		int minimum,
		int maximum)
	{
		using var plugin = new HttpClient { BaseAddress = host.Api.BaseAddress };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/plugins/sessions")
		{
			Content = Json($$"""
							 {
							   "requestedVersion": { "minimum": {{minimum}}, "maximum": {{maximum}} },
							   "capabilities": [
							     { "kind": "events", "localId": "compat-probe",
							       "versionRange": { "minimum": 1, "maximum": 1 } }
							   ],
							   "declaredName": "Compat Probe",
							   "declaredVersion": "1.0.0"
							 }
							 """)
		};
		request.Headers.Add("X-MacroDeck-Plugin-Id", credential.PluginId);
		request.Headers.Add("X-MacroDeck-Plugin-Secret", credential.Secret);
		using var response = await plugin.SendAsync(request);
		return ((int)response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
	}

	public static async Task<RawProtocolPlugin> ConnectAsync(HostInstance host, JsonNode session)
	{
		var plugin = new RawProtocolPlugin((string)session["sessionId"]!, (int)session["negotiatedVersion"]!);
		plugin._socket.Options.AddSubProtocol(SubProtocol);
		plugin._socket.Options.SetRequestHeader("Authorization", $"Bearer {(string)session["sessionToken"]!}");
		await plugin._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{host.LoopbackPort}/plugins/ws"), Timeout());
		return plugin;
	}

	public async Task<JsonNode> HelloAsync()
	{
		var id = NewId();
		await SendAsync($$$"""
						   {"type":"session.hello","id":"{{{id}}}","protocolVersion":{{{NegotiatedVersion}}},
						    "payload":{"protocolVersion":{{{NegotiatedVersion}}},"sessionId":"{{{SessionId}}}"}}
						   """);
		return await AnswerToAsync(id);
	}

	public async Task<JsonNode> PingAsync()
	{
		var id = NewId();
		await SendAsync($$"""{"type":"session.ping","id":"{{id}}","protocolVersion":{{NegotiatedVersion}}}""");
		return await AnswerToAsync(id);
	}

	public async Task GoodbyeAsync()
	{
		await SendAsync($$$"""
						   {"type":"session.goodbye","id":"{{{NewId()}}}","protocolVersion":{{{NegotiatedVersion}}},
						    "payload":{"reason":"compat test finished"}}
						   """);
	}

	public async ValueTask DisposeAsync()
	{
		if (_socket.State == WebSocketState.Open)
		{
			try
			{
				await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
			}
			catch (WebSocketException)
			{
			}
		}

		_socket.Dispose();
	}

	// Waits for the reply to one of our own messages, answering what the host asks in between the way a
	// plugin of that era did: the events catalog with plain-string names, everything else unsupported.
	private async Task<JsonNode> AnswerToAsync(string id)
	{
		while (true)
		{
			var message = await ReceiveAsync();
			Received.Add(message);
			if ((string?)message["correlationId"] == id)
			{
				return message;
			}

			if ((string?)message["type"] == "capability.invoke")
			{
				await AnswerInvokeAsync(message);
			}
		}
	}

	private async Task AnswerInvokeAsync(JsonNode invoke)
	{
		var correlationId = (string)invoke["id"]!;
		var payload = invoke["payload"]!;
		if ((string?)payload["kind"] == "events" && (string?)payload["operation"] == "describe")
		{
			await SendAsync($$$"""
							   {"type":"capability.result","id":"{{{NewId()}}}","correlationId":"{{{correlationId}}}",
							    "protocolVersion":{{{NegotiatedVersion}}},
							    "payload":{"data":{"providerName":"Compat Probe","hasDynamicEventOptions":false,
							      "events":[{"localId":"probe-fired","name":"Compat probe fired","deliveryKind":"Push",
							        "configurationParameters":[],"payloadParameters":[]}]} } }
							   """);
			return;
		}

		await SendAsync($$$"""
						   {"type":"capability.result","id":"{{{NewId()}}}","correlationId":"{{{correlationId}}}",
						    "protocolVersion":{{{NegotiatedVersion}}},
						    "error":{"code":"CAPABILITY_UNSUPPORTED","message":"Not implemented.","retryable":false}}
						   """);
	}

	private async Task SendAsync(string json)
	{
		await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, Timeout());
	}

	private async Task<JsonNode> ReceiveAsync()
	{
		var buffer = new byte[64 * 1024];
		using var message = new MemoryStream();
		while (true)
		{
			var result = await _socket.ReceiveAsync(buffer, Timeout());
			if (result.MessageType == WebSocketMessageType.Close)
			{
				throw new InvalidOperationException(
					$"The host closed the session: {_socket.CloseStatus} {_socket.CloseStatusDescription}");
			}

			message.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				return JsonNode.Parse(message.ToArray())!;
			}
		}
	}

	private static StringContent Json(string json)
	{
		return new StringContent(json, Encoding.UTF8, "application/json");
	}

	private static string NewId()
	{
		return Guid.CreateVersion7().ToString();
	}

	private static CancellationToken Timeout()
	{
		return new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
	}
}
