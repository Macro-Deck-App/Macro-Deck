using System.Net;
using System.Net.Http.Headers;
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


	[Test]
	public async Task An_installed_companion_detects_pairs_and_opens_the_ui_socket_on_the_current_host()
	{
		var host = await StartHostAsync();
		using var companion = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };

		using (var setup = await host.PostAsync("/api/auth/setup",
			new { username = "owner", password = "compat-pass-1" }))
		{
			Assert.That(setup.IsSuccessStatusCode, Is.True, "first-run setup on the loopback listener");
		}

		using var status = await companion.GetAsync(StatusRequest);
		var statusBody = await ObjectAsync(status);

		using var pairing = await host.PostAsync("/api/auth/pairing-code");
		var code = (string)(await ObjectAsync(pairing))["code"]!;

		using var redeemed = await companion.PostAsync("/api/auth/device-enrollment/redeem",
			new StringContent(RedeemRequest(code), Encoding.UTF8, "application/json"));
		var session = await ObjectAsync(redeemed);
		var accessToken = (string?)session["accessToken"];

		using var ticketRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = new StringContent("{}", Encoding.UTF8, "application/json")
		};
		ticketRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		ticketRequest.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		using var ticketResponse = await companion.SendAsync(ticketRequest);
		var ticket = (string?)(await ObjectAsync(ticketResponse))["value"];

		using var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol("macrodeck.ui.v1");
		using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
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
			Assert.That(redeemed.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(),
				Is.True,
				"the code yields the same session and cookies as an enrollment token");

			Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(Base64UrlBytes(ticket), Is.EqualTo(32), "a 256-bit ticket");
			Assert.That(socket.State, Is.EqualTo(WebSocketState.Open));
			Assert.That(socket.SubProtocol, Is.EqualTo("macrodeck.ui.v1"));
		});

		socket.Abort();
	}

	// The device block's field names are not documented in this repository; unknown fields are ignored on
	// read, so a mismatch here could only make the device anonymous, never fail the redeem.
	private static string RedeemRequest(string code)
	{
		return $$$"""
				  {"token":"{{{code}}}","device":{"clientType":"companion","proposedName":"Compat Phone",
				   "platform":"Android","formFactor":"phone","appVersion":"26.2.0"}}
				  """;
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
}
