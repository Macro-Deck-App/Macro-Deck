using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

// COMPAT-02: frozen Companion requests on the public listener, judged by engineering/api (Companion and
// Macro Deck 2 app pages), ADR 0062 and ADR 0083 rather than by today's response DTOs.
[TestFixture]
internal sealed class CompanionWireCompatibilityTests : SystemTestFixture
{
	private const string StatusRequest = "/api/auth/status";
	private const string ClientId = "compat-client-1";
	private const string Hello = """{"protocolVersion":1,"kind":"hello"}""";
	private const string Ping = """{"protocolVersion":1,"kind":"ping","id":"ping-1"}""";
	private const string PongToHostPing = """{"protocolVersion":1,"kind":"pong","correlationId":null}""";

	private const string RegisterClient
		= """{"protocolVersion":1,"kind":"request","type":"RegisterClient","id":"1-register","payload":["compat-client-1"]}""";

	private const string ReportCompanionState = """
												{"protocolVersion":1,"kind":"request","type":"ReportCompanionState","id":"2-report","payload":[{
												 "batteryLevelPercent":80,"charging":false,"orientation":null,"screenBrightnessPercent":50,
												 "model":"Google Pixel 8","platform":"Android 14","appVersion":"26.2.0",
												 "capabilities":["screenOn","screenshotDeck"],"requestableCapabilities":[],"inFocus":true,
												 "networkType":"wifi","networkMetered":false,"networkValidated":null,"networkName":null,
												 "cpuUsagePercent":null,"memoryUsedPercent":12}]}
												""";

	private const string UnknownRequest
		= """{"protocolVersion":1,"kind":"request","type":"NoSuchRequest","id":"3-unknown","payload":[]}""";

	private static readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(20);

	private static readonly string[] _widgetGeometry = ["positionX", "positionY", "width", "height"];

	private static readonly string[] _runStatuses = ["Accepted", "Succeeded", "PartiallyFailed", "Failed", "Cancelled"];

	[Test]
	public async Task An_installed_companion_detects_pairs_and_opens_the_ui_socket_on_the_current_host()
	{
		var host = await StartHostAsync();
		using var companion = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };
		await SetUpOwnerAsync(host);

		using var status = await companion.GetAsync(StatusRequest);
		var statusBody = await ObjectAsync(status);

		using var pairing = await host.PostAsync("/api/auth/pairing-code");
		var code = (string)(await ObjectAsync(pairing))["code"]!;

		using var redeemed = await companion.PostAsync("/api/auth/device-enrollment/redeem",
			new StringContent(RedeemRequest(code), Encoding.UTF8, "application/json"));
		var session = await ObjectAsync(redeemed);
		var accessToken = (string?)session["accessToken"];
		var deviceId = (string?)session["device"]?["deviceId"];

		var devices = await host.GetJsonAsync("/api/devices");
		var device = devices["devices"]?.AsArray().SingleOrDefault(entry => (string?)entry?["id"] == deviceId);

		using var ticketResponse = await RequestTicketAsync(companion, accessToken);
		var ticket = (string?)(await ObjectAsync(ticketResponse))["value"];

		using var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol("macrodeck.ui.v1");
		using var connectTimeout = new CancellationTokenSource(_frameTimeout);
		await socket.ConnectAsync(
			new Uri($"ws://127.0.0.1:{host.PublicPort}/ws/ui?ticket={Uri.EscapeDataString(ticket!)}"),
			connectTimeout.Token);

		Assert.Multiple(() =>
		{
			Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK), "detection is anonymous");
			Assert.That(statusBody["setupComplete"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True));
			Assert.That(statusBody["version"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(statusBody["minimumCompanionVersion"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(IsSemanticVersion((string?)statusBody["minimumCompanionVersion"]), Is.True);

			Assert.That(redeemed.StatusCode, Is.EqualTo(HttpStatusCode.OK), session.ToJsonString());
			Assert.That((string?)session["scope"], Is.EqualTo("client"), "a paired device runs a deck, never admin");
			Assert.That(accessToken, Is.Not.Null.And.Not.Empty);
			Assert.That(session["expiresInSeconds"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number));
			Assert.That(Guid.TryParse(deviceId, out _), Is.True, session.ToJsonString());
			Assert.That((string?)session["device"]?["deviceSecret"], Is.Not.Null.And.Not.Empty);
			Assert.That(redeemed.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(),
				Is.True,
				"the code yields the same session and cookies as an enrollment token");

			Assert.That(device, Is.Not.Null, devices.ToJsonString());
			Assert.That((string?)device?["clientType"], Is.EqualTo("native"));
			Assert.That((string?)device?["proposedName"], Is.EqualTo("Compat Phone"));
			Assert.That((string?)device?["platform"], Is.EqualTo("Android 14"));
			Assert.That((string?)device?["formFactor"], Is.EqualTo("phone"));
			Assert.That((string?)device?["appVersion"], Is.EqualTo("26.2.0"));

			Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(Base64UrlBytes(ticket), Is.EqualTo(32), "a 256-bit ticket");
			Assert.That(socket.State, Is.EqualTo(WebSocketState.Open));
			Assert.That(socket.SubProtocol, Is.EqualTo("macrodeck.ui.v1"));
		});

		socket.Abort();
	}

	[Test]
	public async Task An_installed_companion_handshakes_registers_and_reports_its_state()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;

		await companion.SendAsync(Hello);
		var welcome = await companion.NextAsync();
		await companion.SendAsync(Ping);
		var pong = await companion.UntilAsync(frame => Kind(frame) == "pong");
		await companion.SendAsync(PongToHostPing);
		await companion.SendAsync(RegisterClient);
		var registered = await companion.UntilAsync(frame => Answers(frame, "1-register"));
		var notifications = companion.Received.FindIndex(frame => IsMessage(frame, "ActiveNotificationsEvent"));
		await companion.SendAsync(ReportCompanionState);
		var reported = await companion.UntilAsync(frame => Answers(frame, "2-report"));
		var wakeOnLan = await companion.UntilAsync(frame => IsMessage(frame, "CompanionWakeOnLanEvent"));
		await companion.SendAsync(UnknownRequest);
		var unknown = await companion.UntilAsync(frame => Answers(frame, "3-unknown"));

		Assert.Multiple(() =>
		{
			Assert.That(Kind(welcome), Is.EqualTo("welcome"), "the welcome is the first frame after the hello");
			Assert.That(welcome.ContainsKey("correlationId"),
				Is.False,
				"a hello without an id is welcomed without one");
			Assert.That((string?)pong["correlationId"], Is.EqualTo("ping-1"));

			Assert.That(Kind(registered), Is.EqualTo("response"), registered.ToJsonString());
			Assert.That((string?)registered["type"], Is.EqualTo("RegisterClient"));
			Assert.That(registered["payload"], Is.Null);
			Assert.That(notifications,
				Is.GreaterThanOrEqualTo(0).And.LessThan(companion.Received.IndexOf(registered)),
				"the notification snapshot precedes the answer");
			Assert.That(companion.Received[notifications]["payload"]?["notifications"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.Array));

			Assert.That(Kind(reported), Is.EqualTo("response"), reported.ToJsonString());
			Assert.That(reported["payload"], Is.Null, "a report is answered with nothing");
			Assert.That(wakeOnLan["payload"]?["instanceName"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(wakeOnLan["payload"]?["macAddresses"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Array));

			Assert.That(Kind(unknown), Is.EqualTo("error"), "the socket outlived the pong without an id");
			Assert.That((string?)unknown["error"]?["code"], Is.EqualTo("unknown_type"));
			Assert.That((string?)unknown["type"], Is.EqualTo("NoSuchRequest"));

			Assert.That(companion.Received.Select(frame => (int?)frame["protocolVersion"]), Is.All.EqualTo(1));
		});
	}

	[Test]
	public async Task An_installed_companion_follows_its_deck_and_presses_a_widget()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		var accessToken = connection.AccessToken;
		await using var companion = connection.Socket;
		await companion.SendAsync(Hello);
		await companion.UntilAsync(frame => Kind(frame) == "welcome");
		await companion.SendAsync(RegisterClient);
		await companion.UntilAsync(frame => Answers(frame, "1-register"));

		var (profileId, folderId) = await CreateProfileAsync(host);
		var widgetId = await CreateWidgetAsync(host, folderId, 0);
		var profileCreated = await companion.UntilAsync(frame
			=> IsMessage(frame, "ProfileCreatedEvent") && (string?)frame["payload"]?["profile"]?["id"] == profileId);
		var folderCreated = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderCreatedEvent") && (string?)frame["payload"]?["folder"]?["id"] == folderId);
		var widgetCreated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetCreatedEvent") && (string?)frame["payload"]?["widget"]?["id"] == widgetId);

		var folderReported = await companion.RequestAsync("4-folder",
			"ReportFolderChanged",
			$$"""[{"folderId":"{{folderId}}","clientId":"{{ClientId}}","isResync":false}]""");
		var (opened, attached, tree) = await OpenWidgetSessionAsync(companion, widgetId, "5");
		var sessionId = (string?)opened["payload"]?["sessionId"];
		var revision = (int?)tree["payload"]?["revision"];
		var pressNode = NodeDeclaring(tree["payload"]?["tree"]?["root"], "press");
		var pressed = await companion.RequestAsync("6-press",
			"SendUiEvent",
			$$"""[{"sessionId":"{{sessionId}}","nodeId":"{{pressNode}}","name":"press","revision":{{revision}}}]""");

		using var execute = await ExecuteTriggerAsync(host, accessToken, widgetId, folderId);
		var run = await ObjectAsync(execute);

		var closed = await companion.RequestAsync("7-close", "CloseUiSession", $$"""["{{sessionId}}"]""");
		var pressedAfterClose = await companion.RequestAsync("8-press",
			"SendUiEvent",
			$$"""[{"sessionId":"{{sessionId}}","nodeId":"{{pressNode}}","name":"press","revision":{{revision}}}]""");

		var secondWidgetId = await CreateWidgetAsync(host, folderId, 1);
		var (_, _, secondTree) = await OpenWidgetSessionAsync(companion, secondWidgetId, "9");
		var secondSessionId = (string?)secondTree["payload"]?["sessionId"];
		await UpdateWidgetAsync(host, folderId, secondWidgetId);
		var patched = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionPatchedEvent") && (string?)frame["payload"]?["sessionId"] == secondSessionId);
		await DeleteWidgetAsync(host, folderId, secondWidgetId);
		var ended = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionClosedEvent") && (string?)frame["payload"]?["sessionId"] == secondSessionId);

		Assert.Multiple(() =>
		{
			Assert.That((string?)profileCreated["payload"]?["profile"]?["name"], Is.EqualTo("Compat Deck"));
			Assert.That((string?)folderCreated["payload"]?["folder"]?["profileId"], Is.EqualTo(profileId));
			Assert.That((string?)widgetCreated["payload"]?["folderId"], Is.EqualTo(folderId));
			var widget = widgetCreated["payload"]?["widget"];
			Assert.That((string?)widget?["type"], Is.EqualTo("ActionButton"));
			foreach (var field in _widgetGeometry)
			{
				Assert.That(widget?[field]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number), field);
			}

			Assert.That(widget?["data"]?.GetValueKind(), Is.Null.Or.EqualTo(JsonValueKind.String));

			Assert.That(folderReported["payload"]?["success"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				folderReported.ToJsonString());
			Assert.That(opened["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				opened.ToJsonString());
			Assert.That(sessionId, Is.Not.Null.And.Not.Empty);
			Assert.That(attached["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				attached.ToJsonString());
			Assert.That(attached["payload"]?["revision"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number));
			Assert.That(revision, Is.Not.Null);
			Assert.That(pressNode, Is.Not.Null, tree.ToJsonString());
			Assert.That(pressed["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				pressed.ToJsonString());

			Assert.That(execute.StatusCode, Is.EqualTo(HttpStatusCode.OK), run.ToJsonString());
			Assert.That(run["success"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True), run.ToJsonString());
			Assert.That((string?)run["status"], Is.AnyOf("Accepted", "Succeeded"));
			Assert.That(_runStatuses, Does.Contain((string?)run["status"]));
			Assert.That(run["executionId"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));

			Assert.That(closed["payload"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True), closed.ToJsonString());
			Assert.That(pressedAfterClose["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.False),
				pressedAfterClose.ToJsonString());
			Assert.That((string?)pressedAfterClose["payload"]?["code"],
				Is.AnyOf("SESSION_CLOSED", "SESSION_NOT_FOUND"));

			Assert.That(patched["payload"]?["fromRevision"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number));
			Assert.That((int?)patched["payload"]?["toRevision"],
				Is.GreaterThan((int?)patched["payload"]?["fromRevision"] ?? int.MaxValue));
			Assert.That(patched["payload"]?["patch"], Is.Not.Null, patched.ToJsonString());

			// The Companion tells a deleted widget from any other end by this diagnostic text (DeckClient.kt).
			Assert.That((string?)ended["payload"]?["reason"], Is.EqualTo("The widget was deleted."));

			Assert.That(companion.Received.Select(frame => (int?)frame["protocolVersion"]), Is.All.EqualTo(1));
		});
	}

	private static string RedeemRequest(string code)
	{
		return $$$"""
				  {"token":"{{{code}}}","device":{"clientType":"native","proposedName":"Compat Phone",
				   "platform":"Android 14","formFactor":"phone","appVersion":"26.2.0"}}
				  """;
	}

	private static async Task SetUpOwnerAsync(HostInstance host)
	{
		using var setup = await host.PostAsync("/api/auth/setup",
			new { username = "owner", password = "compat-pass-1" });
		Assert.That(setup.IsSuccessStatusCode, Is.True, "first-run setup on the loopback listener");
	}

	private static async Task<HttpResponseMessage> RequestTicketAsync(HttpClient companion, string? accessToken)
	{
		using var ticketRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets");
		ticketRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
		ticketRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		ticketRequest.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		return await companion.SendAsync(ticketRequest);
	}

	private static async Task<(CompanionSocket Socket, string AccessToken)> ConnectCompanionAsync(HostInstance host)
	{
		using var companion = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };
		await SetUpOwnerAsync(host);

		using var pairing = await host.PostAsync("/api/auth/pairing-code");
		var code = (string)(await ObjectAsync(pairing))["code"]!;
		using var redeemed = await companion.PostAsync("/api/auth/device-enrollment/redeem",
			new StringContent(RedeemRequest(code), Encoding.UTF8, "application/json"));
		var accessToken = (string)(await ObjectAsync(redeemed))["accessToken"]!;
		using var ticketResponse = await RequestTicketAsync(companion, accessToken);
		var ticket = (string)(await ObjectAsync(ticketResponse))["value"]!;

		var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol("macrodeck.ui.v1");
		using var connectTimeout = new CancellationTokenSource(_frameTimeout);
		await socket.ConnectAsync(
			new Uri($"ws://127.0.0.1:{host.PublicPort}/ws/ui?ticket={Uri.EscapeDataString(ticket)}"),
			connectTimeout.Token);
		return (new CompanionSocket(socket), accessToken);
	}

	private static async Task<(JsonObject Opened, JsonObject Attached, JsonObject Tree)> OpenWidgetSessionAsync(
		CompanionSocket companion,
		string widgetId,
		string idPrefix)
	{
		var opened = await companion.RequestAsync($"{idPrefix}-open",
			"OpenWidgetUiSession",
			$$"""[{"widgetId":"{{widgetId}}"}]""");
		var sessionId = (string?)opened["payload"]?["sessionId"];
		var attached = await companion.RequestAsync($"{idPrefix}-attach",
			"AttachUiSession",
			$$"""[{"sessionId":"{{sessionId}}"}]""");
		var tree = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionTreeUpdatedEvent") && (string?)frame["payload"]?["sessionId"] == sessionId);
		return (opened, attached, tree);
	}

	private static async Task<HttpResponseMessage> ExecuteTriggerAsync(
		HostInstance host,
		string accessToken,
		string widgetId,
		string folderId)
	{
		using var companion = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/actions/execute");
		request.Content = new StringContent(
			$$"""{"widgetId":"{{widgetId}}","folderId":"{{folderId}}","triggerType":"onShortPress","clientId":"{{ClientId}}"}""",
			Encoding.UTF8,
			"application/json");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		var response = await companion.SendAsync(request);
		await response.Content.LoadIntoBufferAsync();
		return response;
	}

	private static async Task<(string ProfileId, string FolderId)> CreateProfileAsync(HostInstance host)
	{
		using var created = await host.PostAsync("/api/profiles",
			new { name = "Compat Deck", defaultColumns = 3, defaultRows = 2 });
		created.EnsureSuccessStatusCode();
		var profileId = (string)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["profile"]!["id"]!;

		var folders = await host.GetJsonAsync($"/api/folders?profileId={profileId}");
		var folderId = (string)folders["folders"]!.AsArray()
			.Single(folder => (string?)folder!["profileId"] == profileId && folder!["parentId"] is null)!["id"]!;
		return (profileId, folderId);
	}

	private static async Task<string> CreateWidgetAsync(HostInstance host, string folderId, int column)
	{
		using var response = await host.Api.PostAsJsonAsync("/api/widgets",
			new
			{
				folderId,
				type = "ActionButton",
				positionX = column,
				positionY = 0,
				width = 1,
				height = 1,
				data = WidgetData("One")
			});
		response.EnsureSuccessStatusCode();
		var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
		return (string?)body["widget"]?["id"] ?? throw new InvalidOperationException(body.ToJsonString());
	}

	private static async Task UpdateWidgetAsync(HostInstance host, string folderId, string widgetId)
	{
		using var response = await host.Api.PutAsJsonAsync("/api/widgets",
			new
			{
				id = widgetId,
				folderId,
				type = "ActionButton",
				positionX = 1,
				positionY = 0,
				width = 1,
				height = 1,
				data = WidgetData("Two")
			});
		response.EnsureSuccessStatusCode();
	}

	private static async Task DeleteWidgetAsync(HostInstance host, string folderId, string widgetId)
	{
		using var response = await host.Api.DeleteAsync($"/api/widgets/{widgetId}?folderId={folderId}");
		response.EnsureSuccessStatusCode();
	}

	private static string WidgetData(string label)
	{
		return $$"""{"label":"{{label}}","flows":[{"triggerType":"onShortPress","children":[]}]}""";
	}

	private static string? NodeDeclaring(JsonNode? node, string eventName)
	{
		if (node is not JsonObject element)
		{
			return null;
		}

		if (element["properties"]?["events"] is JsonArray events
			&& events.Any(entry => entry?.GetValueKind() == JsonValueKind.String && (string?)entry == eventName))
		{
			return (string?)element["id"];
		}

		return (element["children"] as JsonArray ?? [])
			.Append(element["fallback"])
			.Select(child => NodeDeclaring(child, eventName))
			.FirstOrDefault(id => id is not null);
	}

	private static string? Kind(JsonObject frame)
	{
		return (string?)frame["kind"];
	}

	private static bool Answers(JsonObject frame, string requestId)
	{
		return Kind(frame) is "response" or "error" && (string?)frame["correlationId"] == requestId;
	}

	private static bool IsMessage(JsonObject frame, string type)
	{
		return Kind(frame) == "message" && (string?)frame["type"] == type;
	}

	private static async Task<JsonObject> ObjectAsync(HttpResponseMessage response)
	{
		return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject ?? new JsonObject();
	}

	// Semantic Versioning 2.0 core, which the version documentation says both sides compare by.
	private static bool IsSemanticVersion(string? value)
	{
		var core = value?.Split('+')[0].Split('-')[0].Split('.');
		return core is { Length: 3 } && core.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
	}

	private static int Base64UrlBytes(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return 0;
		}

		var padded = value.Replace('-', '+').Replace('_', '/');
		padded += new string('=', (4 - padded.Length % 4) % 4);
		try
		{
			return Convert.FromBase64String(padded).Length;
		}
		catch (FormatException)
		{
			return -1;
		}
	}

	private sealed class CompanionSocket(ClientWebSocket socket) : IAsyncDisposable
	{
		public List<JsonObject> Received { get; } = [];

		public async Task SendAsync(string frame)
		{
			using var timeout = new CancellationTokenSource(_frameTimeout);
			await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, timeout.Token);
		}

		public async Task<JsonObject> RequestAsync(string id, string type, string payload)
		{
			await SendAsync(
				$$"""{"protocolVersion":1,"kind":"request","type":"{{type}}","id":"{{id}}","payload":{{payload}}}""");
			return await UntilAsync(frame => Answers(frame, id));
		}

		public async Task<JsonObject> NextAsync()
		{
			using var timeout = new CancellationTokenSource(_frameTimeout);
			return await ReceiveAsync(timeout.Token);
		}

		public async Task<JsonObject> UntilAsync(Func<JsonObject, bool> match)
		{
			if (Received.FirstOrDefault(match) is { } seen)
			{
				return seen;
			}

			using var timeout = new CancellationTokenSource(_frameTimeout);
			while (true)
			{
				var frame = await ReceiveAsync(timeout.Token);
				if (match(frame))
				{
					return frame;
				}
			}
		}

		private async Task<JsonObject> ReceiveAsync(CancellationToken cancellationToken)
		{
			using var buffer = new MemoryStream();
			var chunk = new byte[16 * 1024];
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(chunk, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					throw new InvalidOperationException(
						$"The host closed the socket: {result.CloseStatus} {result.CloseStatusDescription}");
				}

				buffer.Write(chunk, 0, result.Count);
			} while (!result.EndOfMessage);

			var frame = JsonNode.Parse(buffer.ToArray()) as JsonObject
				?? throw new InvalidOperationException("The host sent a frame that is not a JSON object.");
			Received.Add(frame);
			return frame;
		}

		public ValueTask DisposeAsync()
		{
			socket.Abort();
			socket.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
