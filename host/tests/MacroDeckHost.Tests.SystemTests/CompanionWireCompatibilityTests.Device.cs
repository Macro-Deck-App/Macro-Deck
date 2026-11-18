using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

internal sealed partial class CompanionWireCompatibilityTests
{
	private const string SyncLicense = """
									   [{"license":null,"proof":null,"trialDeviceId":"compat-trial-1","trialStarted":false}]
									   """;

	private const string UnknownVideoSession = """[{"sessionId":"compat-video-1"}]""";

	private const string ImageData
		= """{"label":"Image","imageUrl":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==","flows":[{"triggerType":"onShortPress","children":[]}]}""";

	private static readonly string[] _videoSessionRequests
		= ["KeepAliveVideoStream", "SuspendVideoStream", "ResumeVideoStream"];

	[Test]
	public async Task An_installed_companion_follows_its_screensaver()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);

		var settings = await companion.RequestAsync("4-screensaver", "GetDeviceScreenSaver", "[]");
		using (var changed = await host.Api.PatchAsJsonAsync($"/api/devices/{connection.DeviceId}/screensaver",
			new { enabled = true, idleSeconds = 60, screenSaverId = (string?)null, configuration = (string?)null }))
		{
			changed.EnsureSuccessStatusCode();
		}

		var settingsChanged = await companion.UntilAsync(frame => IsMessage(frame, "DeviceScreenSaverChangedEvent"));
		await SendAsync(host, HttpMethod.Post, $"/api/devices/{connection.DeviceId}/screensaver/show", null);
		var show = await companion.UntilAsync(frame => IsMessage(frame, "ShowDeviceScreenSaverEvent"));

		var opened = await companion.RequestAsync("5-open", "OpenScreenSaverUiSession", "[]");
		var sessionId = (string?)opened["payload"]?["sessionId"];
		var attached = await companion.RequestAsync("6-attach",
			"AttachUiSession",
			$$"""[{"sessionId":"{{sessionId}}"}]""");
		var tree = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionTreeUpdatedEvent") && (string?)frame["payload"]?["sessionId"] == sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(Kind(settings), Is.EqualTo("response"), settings.ToJsonString());
			Assert.That(IsNullOr(settings["payload"]?["enabled"], JsonValueKind.True, JsonValueKind.False), Is.True);
			Assert.That(IsNullOr(settings["payload"]?["idleSeconds"], JsonValueKind.Number), Is.True);

			Assert.That(settingsChanged["payload"]?["enabled"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				settingsChanged.ToJsonString());
			Assert.That((int?)settingsChanged["payload"]?["idleSeconds"], Is.EqualTo(60));
			Assert.That(show["payload"]?.GetValueKind(), Is.Null.Or.EqualTo(JsonValueKind.Object));

			Assert.That(opened["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				opened.ToJsonString());
			Assert.That(sessionId, Is.Not.Null.And.Not.Empty);
			Assert.That(IsNullOr(opened["payload"]?["interactive"], JsonValueKind.True, JsonValueKind.False), Is.True);
			Assert.That(attached["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				attached.ToJsonString());
			Assert.That(tree["payload"]?["tree"], Is.Not.Null);
		});
	}

	[Test]
	public async Task An_installed_companion_takes_commands_and_syncs_its_license_notifications_and_video()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);
		await companion.SendAsync(ReportCompanionState);
		await companion.UntilAsync(frame => Answers(frame, "2-report"));

		var vibrate = await Poll.ForAsync(async () =>
			{
				var run = await host.RunActionAsync("app.macro-deck.companion",
					"vibrate",
					new { configuration = connection.DeviceId });
				return run["success"]?.GetValueKind() == JsonValueKind.True ? run : null;
			},
			_frameTimeout,
			"the host sets up a reporting Companion as an integration configuration");
		var command = await companion.UntilAsync(frame => IsMessage(frame, "CompanionCommandEvent"));

		var license = await companion.RequestAsync("3-license", "SyncCompanionLicense", SyncLicense);
		var notification = await companion.RequestAsync("4-notification",
			"RespondToNotification",
			$$"""[{"id":"{{Guid.NewGuid()}}","actionId":null}]""");

		var streams = await companion.RequestAsync("5-streams", "GetVideoStreams", "[{}]");
		var unknownProvider = await companion.RequestAsync("6-open-video",
			"OpenVideoStream",
			"""[{"providerId":"compat.provider::none","streamId":"none","acceptedTransports":["hls","mjpeg"]}]""");
		var unknownSessions = new List<JsonObject>();
		foreach (var request in _videoSessionRequests)
		{
			unknownSessions.Add(await companion.RequestAsync($"7-{request}", request, UnknownVideoSession));
		}

		var closedVideo = await companion.RequestAsync("8-close-video", "CloseVideoStream", UnknownVideoSession);

		Assert.Multiple(() =>
		{
			Assert.That((string?)command["payload"]?["command"], Is.EqualTo("vibrate"), vibrate.ToJsonString());
			Assert.That(IsNullOr(command["payload"]?["requestId"], JsonValueKind.String), Is.True);

			Assert.That(Kind(license), Is.EqualTo("response"), license.ToJsonString());
			Assert.That(license["payload"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Object));
			Assert.That(IsNullOr(license["payload"]?["license"], JsonValueKind.String), Is.True);
			Assert.That(IsNullOr(license["payload"]?["trialStartedAt"], JsonValueKind.Number), Is.True);
			Assert.That(IsKind(license["payload"]?["revokedLicenseIds"], JsonValueKind.Array), Is.True);

			Assert.That((string?)notification["payload"]?["status"],
				Is.EqualTo("not-found"),
				notification.ToJsonString());

			Assert.That(IsNullOr(streams["payload"]?["providers"], JsonValueKind.Array),
				Is.True,
				streams.ToJsonString());
			Assert.That(Kind(unknownProvider), Is.EqualTo("error"), unknownProvider.ToJsonString());
			Assert.That((string?)unknownProvider["error"]?["code"], Is.EqualTo("unknown_provider"));
			foreach (var refused in unknownSessions)
			{
				Assert.That((string?)refused["error"]?["code"], Is.EqualTo("unknown_session"), refused.ToJsonString());
			}

			Assert.That(Kind(closedVideo), Is.EqualTo("response"), closedVideo.ToJsonString());

			Assert.That(companion.Received.Select(frame => (int?)frame["protocolVersion"]), Is.All.EqualTo(1));
		});
	}

	[Test]
	public async Task An_installed_companion_follows_the_hosts_language_appearance_and_lock_screen()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);

		await SendAsync(host, HttpMethod.Put, "/api/settings/localization", new { culture = "de" });
		var culture = await companion.UntilAsync(frame => IsMessage(frame, "LocalizationCultureChangedEvent"));
		await SendAsync(host, HttpMethod.Put, "/api/settings/appearance", new { themeMode = "dark" });
		var appearance = await companion.UntilAsync(frame => IsMessage(frame, "AppearanceChangedEvent"));
		await host.InstallAsync(FixtureArtifacts.WellBehaved(ArtifactsDirectory));
		var catalog = await companion.UntilAsync(frame => IsMessage(frame, "LocalizationCatalogChangedEvent"));

		// Last, since a host the OS reports locked refuses presses once the lock screen is on.
		await SendAsync(host, HttpMethod.Put, "/api/settings/lock-screen", new { enabled = true });
		var lockState = await companion.UntilAsync(frame => IsMessage(frame, "HostLockStateChangedEvent"));

		Assert.Multiple(() =>
		{
			Assert.That(culture["payload"]?.GetValueKind(), Is.Null.Or.EqualTo(JsonValueKind.Object));
			Assert.That(catalog["payload"]?.GetValueKind(), Is.Null.Or.EqualTo(JsonValueKind.Object));

			Assert.That((string?)appearance["payload"]?["themeMode"], Is.EqualTo("dark"), appearance.ToJsonString());
			Assert.That(IsNullOr(appearance["payload"]?["accentColor"], JsonValueKind.String), Is.True);
			Assert.That(IsNullOr(appearance["payload"]?["fontFamily"], JsonValueKind.String), Is.True);

			Assert.That(IsKind(lockState["payload"]?["locked"], JsonValueKind.True, JsonValueKind.False),
				Is.True,
				lockState.ToJsonString());
			Assert.That(lockState["payload"]?["lockScreenEnabled"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True));
			Assert.That(IsKind(lockState["payload"]?["supported"], JsonValueKind.True, JsonValueKind.False), Is.True);
		});
	}

	[Test]
	public async Task An_installed_companion_reads_its_deck_and_the_host_state_over_rest()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);
		var fontFile = await ImportFontAsync(host);

		var (profileId, folderId) = await CreateProfileAsync(host);
		var widgetId = await CreateWidgetAsync(host, folderId, 0, ImageData);
		var (_, _, tree) = await OpenWidgetSessionAsync(companion, widgetId, "4");
		var resourceReference = ResourceIn(tree["payload"]?["tree"]);
		var resourceId = (string?)resourceReference?["resourceId"];
		var resourcePath = $"/api/ui/resources/{Uri.EscapeDataString(resourceId ?? "none")}"
			+ $"?v={Uri.EscapeDataString((string?)resourceReference?["contentHash"] ?? "")}&size=64";

		using var deck = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };
		deck.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
		var folders = await deck.GetFromJsonAsync<JsonObject>(
			$"/api/folders?profileId={Uri.EscapeDataString(profileId)}");
		var profiles = await deck.GetFromJsonAsync<JsonObject>("/api/profiles");
		var localization = await deck.GetFromJsonAsync<JsonObject>("/api/localization");
		var appearance = await deck.GetFromJsonAsync<JsonObject>("/api/settings/appearance");
		var lockState = await deck.GetFromJsonAsync<JsonObject>("/api/system/lock-state");
		var time = await deck.GetFromJsonAsync<JsonObject>("/api/system/time");
		var fonts = await deck.GetFromJsonAsync<JsonObject>("/api/system/fonts");
		var face = fonts?["faces"]?.AsArray().FirstOrDefault(entry => (string?)entry?["family"] == fontFile.Family);
		using var faceFile
			= await deck.GetAsync($"/api/system/fonts/{Uri.EscapeDataString((string?)face?["faceId"] ?? "none")}/file");
		using var cachedFaceRequest = new HttpRequestMessage(HttpMethod.Get, faceFile.RequestMessage!.RequestUri);
		cachedFaceRequest.Headers.IfNoneMatch.Add(faceFile.Headers.ETag ?? EntityTagHeaderValue.Any);
		using var cachedFace = await deck.SendAsync(cachedFaceRequest);
		using var resource = await deck.GetAsync(resourcePath);
		using var unknownResource = await deck.GetAsync("/api/ui/resources/no-such-resource");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(folders?["folders"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Array));
			Assert.That(folders?["folders"]?.AsArray().Select(folder => (string?)folder?["id"]),
				Does.Contain(folderId));
			foreach (var folder in folders?["folders"]?.AsArray() ?? [])
			{
				AssertFolder(folder);
			}

			Assert.That(profiles?["profiles"]?.AsArray().Select(profile => (string?)profile?["id"]),
				Does.Contain(profileId));
			foreach (var profile in profiles?["profiles"]?.AsArray() ?? [])
			{
				AssertProfile(profile);
			}

			Assert.That(IsKind(localization?["culture"], JsonValueKind.String), Is.True);
			Assert.That(IsKind(localization?["fallbackCulture"], JsonValueKind.String), Is.True);
			Assert.That(IsKind(localization?["translations"], JsonValueKind.Object), Is.True);
			Assert.That((localization?["translations"] as JsonObject ?? [])
				.All(entry => entry.Value?.GetValueKind() == JsonValueKind.String),
				Is.True,
				"every translation is text");
			Assert.That(IsNullOr(localization?["hourCycle"], JsonValueKind.String), Is.True);
			Assert.That(localization?["hourPadded"]?.GetValueKind(),
				Is.Null.Or.AnyOf(JsonValueKind.True, JsonValueKind.False));

			Assert.That(IsNullOr(appearance?["themeMode"], JsonValueKind.String),
				Is.True,
				appearance?.ToJsonString());
			Assert.That(IsNullOr(appearance?["accentColor"], JsonValueKind.String), Is.True);
			Assert.That(IsNullOr(appearance?["fontFamily"], JsonValueKind.String), Is.True);

			foreach (var field in _lockStateFields)
			{
				Assert.That(IsKind(lockState?[field], JsonValueKind.True, JsonValueKind.False),
					Is.True,
					$"{field} in {lockState?.ToJsonString()}");
			}

			Assert.That(time?["utcMs"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number), time?.ToJsonString());

			Assert.That(face, Is.Not.Null, fonts?.ToJsonString());
			Assert.That(face?["faceId"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(IsKind(face?["weight"], JsonValueKind.Number), Is.True);
			Assert.That(IsKind(face?["slant"], JsonValueKind.String), Is.True);
			Assert.That(IsKind(face?["remoteRenderable"], JsonValueKind.True, JsonValueKind.False), Is.True);
			Assert.That(face?["userImported"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True));
			Assert.That(faceFile.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That((await faceFile.Content.ReadAsByteArrayAsync()).Length, Is.EqualTo(fontFile.Length));
			Assert.That(faceFile.Headers.ETag, Is.Not.Null);
			Assert.That(cachedFace.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));

			Assert.That(resourceId, Is.Not.Null, tree.ToJsonString());
			Assert.That(IsNullOr(resourceReference?["contentHash"], JsonValueKind.String), Is.True);
			Assert.That(resource.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That((await resource.Content.ReadAsByteArrayAsync()).Length, Is.GreaterThan(0));
			Assert.That(unknownResource.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
		});
	}

	[Test]
	public async Task A_signed_out_companion_cannot_reconnect_with_its_old_token()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using (var companion = connection.Socket)
		{
			await RegisterAsync(companion);
			await SendAsync(host, HttpMethod.Post, $"/api/devices/{connection.DeviceId}/logout", null);
		}

		using var deck = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.PublicPort}") };
		using var ticket = await RequestTicketAsync(deck, connection.AccessToken);

		Assert.That(ticket.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
	}

	private static async Task<(string Family, long Length)> ImportFontAsync(HostInstance host)
	{
		var path = Path.Combine(RepositoryRoot(), "docs", "public", "fonts", "Anta-Regular.ttf");
		var bytes = await File.ReadAllBytesAsync(path);
		using var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(bytes);
		file.Headers.ContentType = new MediaTypeHeaderValue("font/ttf");
		form.Add(file, "files", "Anta-Regular.ttf");
		using var response = await host.Api.PostAsync("/api/fonts/import", form);
		var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
		var family = (string?)body["results"]?[0]?["font"]?["family"];
		Assert.That(family, Is.Not.Null, body.ToJsonString());
		return (family!, bytes.Length);
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
	}

	private static JsonObject? ResourceIn(JsonNode? node)
	{
		return node switch
		{
			JsonObject element when element["resourceId"]?.GetValueKind() == JsonValueKind.String => element,
			JsonObject element => element.Select(field => ResourceIn(field.Value))
				.FirstOrDefault(found => found is not null),
			JsonArray items => items.Select(ResourceIn).FirstOrDefault(found => found is not null),
			_ => null
		};
	}
}
