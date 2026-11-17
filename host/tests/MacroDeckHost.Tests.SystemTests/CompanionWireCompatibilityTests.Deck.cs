using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

internal sealed partial class CompanionWireCompatibilityTests
{
	private const string StateModeData
		= """{"stateMode":true,"activeStateId":"off","states":[{"id":"off","label":"Off"},{"id":"on","label":"On"}],"flows":[{"triggerType":"onShortPress","children":[]}]}""";

	private const string SlowRunData
		= """{"label":"Slow","flows":[{"triggerType":"onShortPress","children":[{"id":"wait","type":"delay","parameters":[{"name":"duration","type":"duration","value":6000}]}]}]}""";

	[Test]
	public async Task An_installed_companion_follows_widget_folder_and_profile_changes()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);

		var (profileId, folderId) = await CreateProfileAsync(host);
		var widgetIds = await CreateWidgetsAsync(host, folderId);
		var widgetsCreated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetsCreatedEvent") && (string?)frame["payload"]?["folderId"] == folderId);

		await UpdateWidgetAsync(host, folderId, widgetIds[0]);
		var widgetUpdated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetUpdatedEvent") && (string?)frame["payload"]?["widget"]?["id"] == widgetIds[0]);

		await SendAsync(host,
			HttpMethod.Put,
			"/api/widgets/positions",
			new
			{
				folderId,
				positions = new[] { new { id = widgetIds[1], positionX = 0, positionY = 1, width = 1, height = 1 } }
			});
		var positionsUpdated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetPositionsUpdatedEvent") && (string?)frame["payload"]?["folderId"] == folderId);

		await SendAsync(host,
			HttpMethod.Put,
			"/api/widgets/pinned/batch",
			new { folderId, widgetIds, pinned = true });
		var widgetsUpdated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetsUpdatedEvent") && (string?)frame["payload"]?["folderId"] == folderId);

		await DeleteWidgetAsync(host, folderId, widgetIds[0]);
		var widgetDeleted = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetDeletedEvent") && (string?)frame["payload"]?["widgetId"] == widgetIds[0]);

		await SendAsync(host,
			HttpMethod.Post,
			"/api/widgets/batch/delete",
			new { folderId, ids = new[] { widgetIds[1] } });
		var widgetsDeleted = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetsDeletedEvent") && (string?)frame["payload"]?["folderId"] == folderId);

		await SendAsync(host, HttpMethod.Put, "/api/folders", new { id = folderId, name = "Renamed Folder" });
		var folderUpdated = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderUpdatedEvent") && (string?)frame["payload"]?["folder"]?["id"] == folderId);

		var subfolder = await SendAsync(host,
			HttpMethod.Post,
			"/api/folders",
			new { profileId, name = "Sub", parentId = folderId });
		var subfolderId = (string)subfolder["folder"]!["id"]!;
		await SendAsync(host, HttpMethod.Delete, $"/api/folders/{subfolderId}", null);
		var folderDeleted = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderDeletedEvent") && (string?)frame["payload"]?["folderId"] == subfolderId);

		await SendAsync(host, HttpMethod.Put, "/api/profiles", new { id = profileId, name = "Renamed Deck" });
		var profileUpdated = await companion.UntilAsync(frame
			=> IsMessage(frame, "ProfileUpdatedEvent") && (string?)frame["payload"]?["profile"]?["id"] == profileId);

		var (otherProfileId, _) = await CreateProfileAsync(host);
		await SendAsync(host, HttpMethod.Delete, $"/api/profiles/{otherProfileId}", null);
		var profileDeleted = await companion.UntilAsync(frame
			=> IsMessage(frame, "ProfileDeletedEvent") && (string?)frame["payload"]?["profileId"] == otherProfileId);

		await host.RunActionAsync("app.macro-deck.deck", "go-back", new { });
		var back = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderNavigationEvent") && (string?)frame["payload"]?["command"] == "back");
		await host.RunActionAsync("app.macro-deck.deck", "go-to-parent", new { });
		var parent = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderNavigationEvent") && (string?)frame["payload"]?["command"] == "parent");
		await SendAsync(host,
			HttpMethod.Post,
			$"/api/devices/{connection.DeviceId}/open-profile",
			new { profileId });
		var changeTo = await companion.UntilAsync(frame
			=> IsMessage(frame, "FolderNavigationEvent") && (string?)frame["payload"]?["command"] == "changeTo");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(widgetsCreated["payload"]?["widgets"]?.AsArray().Count,
				Is.EqualTo(2),
				widgetsCreated.ToJsonString());
			AssertWidgets(widgetsCreated["payload"]?["widgets"]);
			AssertWidget(widgetUpdated["payload"]?["widget"]);
			Assert.That((string?)widgetUpdated["payload"]?["folderId"], Is.EqualTo(folderId));
			AssertWidgets(widgetsUpdated["payload"]?["widgets"]);
			Assert.That(widgetsUpdated["payload"]?["widgets"]
					?.AsArray()
					.All(widget => widget?["isPinned"]?.GetValueKind() == JsonValueKind.True),
				Is.True,
				widgetsUpdated.ToJsonString());

			AssertWidgets(positionsUpdated["payload"]?["widgets"]);
			var moved = positionsUpdated["payload"]?["widgets"]
				?.AsArray()
				.SingleOrDefault(widget => (string?)widget?["id"] == widgetIds[1]);
			Assert.That((int?)moved?["positionY"], Is.EqualTo(1), positionsUpdated.ToJsonString());

			Assert.That((string?)widgetDeleted["payload"]?["folderId"], Is.EqualTo(folderId));
			Assert.That(widgetsDeleted["payload"]?["widgetIds"]?.AsArray().Select(id => (string?)id),
				Is.EqualTo(new[] { widgetIds[1] }));

			AssertFolder(folderUpdated["payload"]?["folder"]);
			Assert.That((string?)folderUpdated["payload"]?["folder"]?["name"], Is.EqualTo("Renamed Folder"));
			Assert.That(folderDeleted["payload"]?["folderId"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));

			AssertProfile(profileUpdated["payload"]?["profile"]);
			Assert.That((string?)profileUpdated["payload"]?["profile"]?["name"], Is.EqualTo("Renamed Deck"));
			Assert.That(profileDeleted["payload"]?["profileId"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));

			Assert.That((string?)back["type"], Is.EqualTo("FolderNavigationEvent"));
			Assert.That((string?)parent["type"], Is.EqualTo("FolderNavigationEvent"));
			Assert.That((string?)changeTo["payload"]?["profileId"], Is.EqualTo(profileId));
			Assert.That(changeTo["payload"]?["folderId"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(IsNullOr(changeTo["payload"]?["navigationToken"], JsonValueKind.String), Is.True);

			var folders = await host.GetJsonAsync($"/api/folders?profileId={profileId}");
			Assert.That((string?)changeTo["payload"]?["folderId"],
				Is.AnyOf(folders["folders"]!.AsArray().Select(folder => (string?)folder?["id"]).ToArray()));
		});
	}

	[Test]
	public async Task An_installed_companion_follows_its_widget_sessions_and_runs()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);

		var (_, folderId) = await CreateProfileAsync(host);
		var widgetId = await CreateWidgetAsync(host, folderId, 0);
		var (_, _, tree) = await OpenWidgetSessionAsync(companion, widgetId, "4");
		var sessionId = (string?)tree["payload"]?["sessionId"];
		await SendAsync(host, HttpMethod.Put, "/api/folders", new { id = folderId, widgetBorderRadius = 12 });
		var invalidated = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionInvalidatedEvent") && (string?)frame["payload"]?["sessionId"] == sessionId);

		var stateWidgetId = await CreateWidgetAsync(host, folderId, 1, StateModeData);
		// The first state the host reconciles is a seed, not a change: only the second press transitions.
		for (var press = 0; press < 2; press++)
		{
			using var pressed = await ExecuteTriggerAsync(host, connection.AccessToken, stateWidgetId, folderId);
			pressed.EnsureSuccessStatusCode();
		}

		var stateInvalidated = await companion.UntilAsync(frame
			=> IsMessage(frame, "WidgetStateInvalidatedEvent")
			&& (string?)frame["payload"]?["widgetId"] == stateWidgetId);

		var slowWidgetId = await CreateWidgetAsync(host, folderId, 2, SlowRunData);
		using var slowRun = await ExecuteTriggerAsync(host, connection.AccessToken, slowWidgetId, folderId);
		var slowAnswer = await ObjectAsync(slowRun);
		var executionId = (string?)slowAnswer["executionId"];
		var runStatus = await companion.UntilAsync(frame
			=> IsMessage(frame, "ActionExecutionStatusEvent")
			&& (string?)frame["payload"]?["executionId"] == executionId);

		var folderView = await companion.RequestAsync("5-folder-view",
			"OpenFolderUiSession",
			$$"""[{"folderId":"{{folderId}}"}]""");

		Assert.Multiple(() =>
		{
			Assert.That(IsNullOr(invalidated["payload"]?["code"], JsonValueKind.String),
				Is.True,
				invalidated.ToJsonString());
			Assert.That(IsKind(invalidated["payload"]?["retryable"], JsonValueKind.True, JsonValueKind.False),
				Is.True);

			Assert.That((string?)stateInvalidated["payload"]?["widgetId"], Is.EqualTo(stateWidgetId));

			Assert.That((string?)slowAnswer["status"], Is.EqualTo("Accepted"), slowAnswer.ToJsonString());
			Assert.That(executionId, Is.Not.Null.And.Not.Empty);
			Assert.That(_runStatuses, Does.Contain((string?)runStatus["payload"]?["status"]), runStatus.ToJsonString());
			Assert.That(IsNullOr(runStatus["payload"]?["error"], JsonValueKind.Object), Is.True);

			Assert.That(Kind(folderView), Is.EqualTo("response"), folderView.ToJsonString());
			Assert.That(folderView["payload"]?["accepted"]?.GetValueKind(), Is.EqualTo(JsonValueKind.False));
			Assert.That(IsNullOr(folderView["payload"]?["code"], JsonValueKind.String), Is.True);
			Assert.That(IsNullOr(folderView["payload"]?["viewId"], JsonValueKind.String), Is.True);

			Assert.That(companion.Received.Select(frame => (int?)frame["protocolVersion"]), Is.All.EqualTo(1));
		});
	}

	[Test]
	public async Task An_installed_companion_shows_and_completes_a_dialog_the_host_opens_for_it()
	{
		var host = await StartHostAsync();
		var connection = await ConnectCompanionAsync(host);
		await using var companion = connection.Socket;
		await RegisterAsync(companion);

		var (_, folderId) = await CreateProfileAsync(host);
		var countdownId = await CreateWidgetAsync(host, folderId, 0, """{"mode":"ask"}""", "Countdown");
		var (_, _, tree) = await OpenWidgetSessionAsync(companion, countdownId, "4");
		var sessionId = (string?)tree["payload"]?["sessionId"];
		var pressNode = NodeDeclaring(tree["payload"]?["tree"]?["root"], "press");
		var pressed = await companion.RequestAsync("5-press",
			"SendUiEvent",
			$$"""[{"sessionId":"{{sessionId}}","nodeId":"{{pressNode}}","name":"press"}]""");
		var modalOpened = await companion.UntilAsync(frame => IsMessage(frame, "UiModalOpenedEvent"));
		var modalId = (string?)modalOpened["payload"]?["modalId"];

		var opened = await companion.RequestAsync("6-modal", "OpenModalUiSession", $$"""[{"modalId":"{{modalId}}"}]""");
		var modalSessionId = (string?)opened["payload"]?["sessionId"];
		var attached = await companion.RequestAsync("7-attach",
			"AttachUiSession",
			$$"""[{"sessionId":"{{modalSessionId}}"}]""");
		var modalTree = await companion.UntilAsync(frame
			=> IsMessage(frame, "UiSessionTreeUpdatedEvent")
			&& (string?)frame["payload"]?["sessionId"] == modalSessionId);
		var completed = await companion.RequestAsync("8-complete",
			"CompleteUiModal",
			$$"""[{"modalId":"{{modalId}}","cancelled":true}]""");

		Assert.Multiple(() =>
		{
			Assert.That(pressNode, Is.Not.Null, tree.ToJsonString());
			Assert.That(pressed["payload"]?["accepted"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True));
			Assert.That(modalId, Is.Not.Null.And.Not.Empty, modalOpened.ToJsonString());
			Assert.That(IsNullOr(modalOpened["payload"]?["title"], JsonValueKind.String)
				|| modalOpened["payload"]?["title"]?["$localized"] is JsonObject,
				Is.True,
				modalOpened.ToJsonString());

			Assert.That(opened["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				opened.ToJsonString());
			Assert.That(modalSessionId, Is.Not.Null.And.Not.Empty);
			Assert.That(attached["payload"]?["accepted"]?.GetValueKind(),
				Is.EqualTo(JsonValueKind.True),
				attached.ToJsonString());
			Assert.That(modalTree["payload"]?["tree"], Is.Not.Null);
			Assert.That(Kind(completed), Is.EqualTo("response"), completed.ToJsonString());
			Assert.That(completed["payload"]?["accepted"]?.GetValueKind(), Is.EqualTo(JsonValueKind.True));
		});
	}

	private static async Task RegisterAsync(CompanionSocket companion)
	{
		await companion.SendAsync(Hello);
		await companion.UntilAsync(frame => Kind(frame) == "welcome");
		await companion.SendAsync(RegisterClient);
		await companion.UntilAsync(frame => Answers(frame, "1-register"));
	}

	private static async Task<string[]> CreateWidgetsAsync(HostInstance host, string folderId)
	{
		var body = await SendAsync(host,
			HttpMethod.Post,
			"/api/widgets/batch",
			new
			{
				folderId,
				widgets = new[]
				{
					new
					{
						type = "ActionButton", positionX = 0, positionY = 0, width = 1, height = 1,
						data = WidgetData("A")
					},
					new
					{
						type = "ActionButton", positionX = 2, positionY = 0, width = 1, height = 1,
						data = WidgetData("B")
					}
				}
			});
		return body["widgets"]!.AsArray().Select(widget => (string)widget!["id"]!).ToArray();
	}

	private static async Task<JsonNode> SendAsync(HostInstance host, HttpMethod method, string path, object? body)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		using var response = await host.Api.SendAsync(request);
		var text = await response.Content.ReadAsStringAsync();
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{method} {path}: {text}");
		var node = text.Length == 0 ? new JsonObject() : JsonNode.Parse(text)!;
		Assert.That(node["success"]?.GetValueKind(), Is.Not.EqualTo(JsonValueKind.False), $"{method} {path}: {text}");
		return node;
	}

	private static void AssertWidgets(JsonNode? widgets)
	{
		Assert.That(widgets?.GetValueKind(), Is.EqualTo(JsonValueKind.Array));
		foreach (var widget in widgets?.AsArray() ?? [])
		{
			AssertWidget(widget);
		}
	}

	private static void AssertWidget(JsonNode? widget)
	{
		var text = widget?.ToJsonString();
		Assert.That(widget?["id"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String), text);
		Assert.That(widget?["type"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String), text);
		foreach (var field in _widgetGeometry)
		{
			Assert.That(IsKind(widget?[field], JsonValueKind.Number), Is.True, $"{field} in {text}");
		}

		Assert.That(IsOptional(widget, "isPinned", JsonValueKind.True, JsonValueKind.False), Is.True, text);
		Assert.That(IsNullOr(widget?["data"], JsonValueKind.String), Is.True, text);
		Assert.That(IsNullOr(widget?["pinScope"], JsonValueKind.String), Is.True, text);
	}

	private static void AssertFolder(JsonNode? folder)
	{
		var text = folder?.ToJsonString();
		Assert.That(folder?["id"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String), text);
		Assert.That(IsOptional(folder, "name", JsonValueKind.String), Is.True, text);
		Assert.That(IsOptional(folder, "order", JsonValueKind.Number), Is.True, text);
		Assert.That(IsOptional(folder, "isDefault", JsonValueKind.True, JsonValueKind.False), Is.True, text);
		foreach (var field in _folderTexts)
		{
			Assert.That(IsNullOr(folder?[field], JsonValueKind.String), Is.True, $"{field} in {text}");
		}

		foreach (var field in _folderNumbers)
		{
			Assert.That(IsNullOr(folder?[field], JsonValueKind.Number), Is.True, $"{field} in {text}");
		}

		Assert.That(IsOptional(folder, "widgets", JsonValueKind.Array), Is.True, text);
		foreach (var widget in folder?["widgets"] as JsonArray ?? [])
		{
			AssertWidget(widget);
		}
	}

	private static void AssertProfile(JsonNode? profile)
	{
		var text = profile?.ToJsonString();
		Assert.That(profile?["id"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String), text);
		Assert.That(IsOptional(profile, "name", JsonValueKind.String), Is.True, text);
		Assert.That(IsOptional(profile, "order", JsonValueKind.Number), Is.True, text);
		foreach (var field in _profileNumbers)
		{
			Assert.That(IsNullOr(profile?[field], JsonValueKind.Number), Is.True, $"{field} in {text}");
		}

		Assert.That(IsNullOr(profile?["defaultEmptyCellStyle"], JsonValueKind.String), Is.True, text);
		Assert.That(IsNullOr(profile?["defaultWidgetShadows"], JsonValueKind.True, JsonValueKind.False), Is.True, text);
	}

	// A field the Companion decodes with a default may be missing, but an explicit null fails its decode.
	private static bool IsOptional(JsonNode? parent, string field, params JsonValueKind[] kinds)
	{
		if (parent is not JsonObject element)
		{
			return false;
		}

		return !element.TryGetPropertyValue(field, out var value)
			|| value is not null && kinds.Contains(value.GetValueKind());
	}

	private static bool IsKind(JsonNode? node, params JsonValueKind[] kinds)
	{
		return node is not null && kinds.Contains(node.GetValueKind());
	}

	private static bool IsNullOr(JsonNode? node, params JsonValueKind[] kinds)
	{
		return node is null || kinds.Contains(node.GetValueKind());
	}
}
